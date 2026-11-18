using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using ScanQa.Core.Geometry;
using Units = ScanQa.Core.Geometry.Units;
using ScanQa.Core.Qa;

namespace ScanQa.Revit
{
    /// <summary>
    /// Draws the QA layers in the checked 3D view with DirectContext3D:
    /// the deviation heatmap per scan point, and the Suspected Missing points as small crosses.
    /// Nothing is written to the model.
    /// </summary>
    internal sealed class QaDisplayServer : IDirectContext3DServer
    {
        private static readonly Guid ServerGuid = new Guid("6C7E1B2A-9F7D-4E0B-8C53-1A6F0D9E4B21");
        private const int VerticesPerBuffer = 60_000;
        public const int MaxHeatmapPoints = 600_000;

        private readonly object _gate = new object();
        private string _docKey;
        private long _viewId = long.MinValue;
        private Vec3[] _heatPos = new Vec3[0];
        private Rgb[] _heatCol = new Rgb[0];
        private Vec3[] _missPos = new Vec3[0];
        private Outline _bounds;
        private bool _dirty;
        private readonly List<Batch> _heatBatches = new List<Batch>();
        private readonly List<Batch> _missBatches = new List<Batch>();

        public bool ShowHeatmap { get; set; } = true;
        public bool ShowMissing { get; set; } = true;
        public bool IsRegistered { get; private set; }

        private sealed class Batch : IDisposable
        {
            public VertexBuffer Vertices;
            public IndexBuffer Indices;
            public int VertexCount;
            public int PrimitiveCount;
            public PrimitiveType Type;
            public VertexFormat Format;
            public EffectInstance Effect;

            public void Dispose()
            {
                Vertices?.Dispose();
                Indices?.Dispose();
                Format?.Dispose();
                Effect?.Dispose();
            }
        }

        public void Register()
        {
            if (IsRegistered) return;
            var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService) as MultiServerService;
            if (service == null) return;
            service.AddServer(this);
            var active = service.GetActiveServerIds();
            active.Add(GetServerId());
            service.SetActiveServers(active);
            IsRegistered = true;
        }

        public void Unregister()
        {
            if (!IsRegistered) return;
            var service = ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService) as MultiServerService;
            service?.RemoveServer(GetServerId());
            IsRegistered = false;
            ClearBatches();
        }

        /// <summary>Replaces the drawn data with a new QA result (null clears).</summary>
        public void SetResult(Document doc, View view, QaResult result)
        {
            lock (_gate)
            {
                if (result == null)
                {
                    _heatPos = new Vec3[0];
                    _heatCol = new Rgb[0];
                    _missPos = new Vec3[0];
                    _bounds = null;
                    _dirty = true;
                    return;
                }
                _docKey = DocKey(doc);
                _viewId = RevitConvert.ToLong(view.Id);
                double tol = result.Settings.ToleranceMm;

                var heat = new List<int>();
                var miss = new List<Vec3>();
                for (int i = 0; i < result.Points.Length; i++)
                {
                    if (result.PointClasses[i] == PointClass.Evaluated) heat.Add(i);
                    else if (result.PointCluster[i] > 0) miss.Add(result.Points[i]);
                }
                // Uniform decimation keeps the view responsive on large regions.
                int step = Math.Max(1, (int)Math.Ceiling(heat.Count / (double)MaxHeatmapPoints));
                var pos = new List<Vec3>(heat.Count / step + 1);
                var col = new List<Rgb>(heat.Count / step + 1);
                for (int k = 0; k < heat.Count; k += step)
                {
                    int i = heat[k];
                    pos.Add(result.Points[i]);
                    col.Add(Heatmap.ForDeviation(result.DeviationMm[i], tol));
                }
                int mstep = Math.Max(1, (int)Math.Ceiling(miss.Count / (double)(MaxHeatmapPoints / 4)));
                var mpos = new List<Vec3>();
                for (int k = 0; k < miss.Count; k += mstep) mpos.Add(miss[k]);

                _heatPos = pos.ToArray();
                _heatCol = col.ToArray();
                _missPos = mpos.ToArray();
                var box = Aabb.Empty;
                foreach (var p in _heatPos) box = box.Include(p);
                foreach (var p in _missPos) box = box.Include(p);
                _bounds = box.IsEmpty ? null : new Outline(RevitConvert.ToXyz(box.Min), RevitConvert.ToXyz(box.Max));
                _dirty = true;
            }
        }

        private static string DocKey(Document doc) => doc.PathName + "|" + doc.Title;

        // IExternalServer
        public Guid GetServerId() => ServerGuid;
        public string GetVendorId() => "OPCS";
        public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
        public string GetName() => "Scan-to-BIM QA layers";
        public string GetDescription() => "Deviation heatmap and Suspected Missing points of the Scan-to-BIM QA add-in.";

        // IDirectContext3DServer
        public string GetApplicationId() => "";
        public string GetSourceId() => "";
        public bool UsesHandles() => false;
        public bool UseInTransparentPass(View view) => false;

        public bool CanExecute(View view)
        {
            lock (_gate)
            {
                if (_bounds == null || view == null) return false;
                return RevitConvert.ToLong(view.Id) == _viewId && DocKey(view.Document) == _docKey;
            }
        }

        public Outline GetBoundingBox(View view)
        {
            lock (_gate) return _bounds;
        }

        public void RenderScene(View view, DisplayStyle displayStyle)
        {
            try
            {
                if (DrawContext.IsTransparentPass()) return;
                lock (_gate)
                {
                    if (_dirty)
                    {
                        RebuildBatches();
                        _dirty = false;
                    }
                }
                if (ShowHeatmap) Flush(_heatBatches);
                if (ShowMissing) Flush(_missBatches);
            }
            catch (Exception)
            {
                // A drawing failure must never take down the Revit view.
            }
        }

        private static void Flush(List<Batch> batches)
        {
            foreach (var b in batches)
                DrawContext.FlushBuffer(b.Vertices, b.VertexCount, b.Indices, b.PrimitiveCount * IndexSize(b.Type),
                    b.Format, b.Effect, b.Type, 0, b.PrimitiveCount);
        }

        private static int IndexSize(PrimitiveType t) =>
            t == PrimitiveType.LineList ? IndexLine.GetSizeInShortInts() : IndexPoint.GetSizeInShortInts();

        private void ClearBatches()
        {
            foreach (var b in _heatBatches) b.Dispose();
            foreach (var b in _missBatches) b.Dispose();
            _heatBatches.Clear();
            _missBatches.Clear();
        }

        private void RebuildBatches()
        {
            ClearBatches();
            for (int start = 0; start < _heatPos.Length; start += VerticesPerBuffer)
            {
                int n = Math.Min(VerticesPerBuffer, _heatPos.Length - start);
                _heatBatches.Add(PointBatch(start, n));
            }
            // Each missing point becomes a 3-axis cross: 6 vertices, 3 lines.
            int perBuffer = VerticesPerBuffer / 6;
            for (int start = 0; start < _missPos.Length; start += perBuffer)
            {
                int n = Math.Min(perBuffer, _missPos.Length - start);
                _missBatches.Add(CrossBatch(start, n));
            }
        }

        private Batch PointBatch(int start, int n)
        {
            var vb = new VertexBuffer(n * VertexPositionColored.GetSizeInFloats());
            vb.Map(n * VertexPositionColored.GetSizeInFloats());
            var vs = vb.GetVertexStreamPositionColored();
            for (int i = 0; i < n; i++)
            {
                var c = _heatCol[start + i];
                vs.AddVertex(new VertexPositionColored(RevitConvert.ToXyz(_heatPos[start + i]), new ColorWithTransparency(c.R, c.G, c.B, 0)));
            }
            vb.Unmap();

            var ib = new IndexBuffer(n * IndexPoint.GetSizeInShortInts());
            ib.Map(n * IndexPoint.GetSizeInShortInts());
            var istr = ib.GetIndexStreamPoint();
            for (int i = 0; i < n; i++) istr.AddPoint(new IndexPoint(i));
            ib.Unmap();

            return new Batch
            {
                Vertices = vb,
                Indices = ib,
                VertexCount = n,
                PrimitiveCount = n,
                Type = PrimitiveType.PointList,
                Format = new VertexFormat(VertexFormatBits.PositionColored),
                Effect = new EffectInstance(VertexFormatBits.PositionColored),
            };
        }

        private Batch CrossBatch(int start, int n)
        {
            double h = Units.MmToFeet(12);
            var color = new ColorWithTransparency(Heatmap.Missing.R, Heatmap.Missing.G, Heatmap.Missing.B, 0);
            int vcount = n * 6;
            var vb = new VertexBuffer(vcount * VertexPositionColored.GetSizeInFloats());
            vb.Map(vcount * VertexPositionColored.GetSizeInFloats());
            var vs = vb.GetVertexStreamPositionColored();
            var axes = new[] { new XYZ(h, 0, 0), new XYZ(0, h, 0), new XYZ(0, 0, h) };
            for (int i = 0; i < n; i++)
            {
                var p = RevitConvert.ToXyz(_missPos[start + i]);
                foreach (var a in axes)
                {
                    vs.AddVertex(new VertexPositionColored(p - a, color));
                    vs.AddVertex(new VertexPositionColored(p + a, color));
                }
            }
            vb.Unmap();

            int lines = n * 3;
            var ib = new IndexBuffer(lines * IndexLine.GetSizeInShortInts());
            ib.Map(lines * IndexLine.GetSizeInShortInts());
            var istr = ib.GetIndexStreamLine();
            for (int l = 0; l < lines; l++) istr.AddLine(new IndexLine(2 * l, 2 * l + 1));
            ib.Unmap();

            return new Batch
            {
                Vertices = vb,
                Indices = ib,
                VertexCount = vcount,
                PrimitiveCount = lines,
                Type = PrimitiveType.LineList,
                Format = new VertexFormat(VertexFormatBits.PositionColored),
                Effect = new EffectInstance(VertexFormatBits.PositionColored),
            };
        }
    }
}
