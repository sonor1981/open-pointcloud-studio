using System.Collections.Generic;
using ScanQa.Core.Geometry;

namespace ScanQa.Core.Qa
{
    public enum ElementRole
    {
        /// <summary>Element under QA: gets deviation statistics and a status.</summary>
        Target,
        /// <summary>Other model geometry in the region (walls, slabs, other MEP). It explains scan points so they are not reported as missing, but gets no status.</summary>
        Context,
    }

    /// <summary>Straight centre line of a linear MEP element (pipe, duct, conduit, cable tray).</summary>
    public sealed class LinearAxis
    {
        public Vec3 Start { get; }
        public Vec3 End { get; }

        public LinearAxis(Vec3 start, Vec3 end)
        {
            Start = start;
            End = end;
        }

        public double Length => Start.DistanceTo(End);

        /// <summary>Signed distance along the axis from Start to the projection of p.</summary>
        public double AxialPosition(Vec3 p)
        {
            var d = End - Start;
            double len = d.Length;
            if (len < 1e-12) return 0;
            return (p - Start).Dot(d / len);
        }
    }

    /// <summary>One model element as seen by the QA engine. Coordinates are model coordinates in feet.</summary>
    public sealed class QaElement
    {
        public long Id { get; set; }
        public string Category { get; set; } = "";
        public string Name { get; set; } = "";
        public ElementRole Role { get; set; } = ElementRole.Target;
        public List<Triangle> Triangles { get; } = new List<Triangle>();

        /// <summary>Set for straight pipes/ducts. Scan points beyond its ends are not attributed to the element.</summary>
        public LinearAxis Axis { get; set; }

        /// <summary>Unconnected connector origins. Used to label missing clusters as "possible missing fitting".</summary>
        public List<Vec3> OpenEnds { get; } = new List<Vec3>();

        public Aabb Bounds
        {
            get
            {
                var b = Aabb.Empty;
                foreach (var t in Triangles) b = b.Union(t.Bounds);
                return b;
            }
        }
    }

    public enum QaStatus
    {
        Pass,
        Fail,
        InsufficientScanData,
    }

    public enum PointClass : byte
    {
        /// <summary>Nearest surface within the missing threshold belongs to a QA target.</summary>
        Evaluated = 0,
        /// <summary>Nearest surface within the missing threshold belongs to context geometry.</summary>
        Context = 1,
        /// <summary>No model surface within the missing threshold.</summary>
        Unexplained = 2,
    }

    public sealed class ElementResult
    {
        public long ElementId { get; set; }
        public string Category { get; set; } = "";
        public string Name { get; set; } = "";
        public int PointCount { get; set; }
        public double PercentWithinTolerance { get; set; }
        public double P95Mm { get; set; }
        public double MaxMm { get; set; }
        public double MeanMm { get; set; }
        /// <summary>Share (0..1) of the element surface inside the region that has scan points on it.</summary>
        public double Coverage { get; set; }
        public QaStatus Status { get; set; }
        public string Reason { get; set; } = "";
        public Aabb Bounds { get; set; }
    }

    public sealed class MissingCluster
    {
        public int Id { get; set; }
        public int PointCount { get; set; }
        public Vec3 Centroid { get; set; }
        public Aabb Bounds { get; set; }
        /// <summary>Largest box dimension in mm.</summary>
        public double ExtentMm { get; set; }
        /// <summary>Element whose unconnected end is nearest to the cluster, if within the search distance.</summary>
        public long? NearOpenEndOfElementId { get; set; }
        public double? DistanceToOpenEndMm { get; set; }
        public string Note { get; set; } = "";
    }

    public sealed class QaResult
    {
        public List<ElementResult> Elements { get; } = new List<ElementResult>();
        public List<MissingCluster> Clusters { get; } = new List<MissingCluster>();

        /// <summary>Model-space points that were evaluated (same order as the per-point arrays).</summary>
        public Vec3[] Points { get; set; } = new Vec3[0];
        public PointClass[] PointClasses { get; set; } = new PointClass[0];
        /// <summary>Absolute distance to the nearest accepted surface, mm (NaN when none within the threshold).</summary>
        public float[] DeviationMm { get; set; } = new float[0];
        /// <summary>Index into the input element list, or -1.</summary>
        public int[] PointOwner { get; set; } = new int[0];
        /// <summary>Missing cluster id, or -1.</summary>
        public int[] PointCluster { get; set; } = new int[0];

        public QaSettings Settings { get; set; }
    }
}
