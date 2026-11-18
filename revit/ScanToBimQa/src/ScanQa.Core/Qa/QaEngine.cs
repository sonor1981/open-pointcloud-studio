using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ScanQa.Core.Geometry;
using ScanQa.Core.Spatial;

namespace ScanQa.Core.Qa
{
    /// <summary>
    /// Scan-to-BIM QA: per-point distance to the true element surface (triangulated geometry), per-element
    /// statistics and status, and clusters of scan points that no model surface explains.
    /// Input points and geometry are model coordinates in feet; results are in millimetres.
    /// </summary>
    public static class QaEngine
    {
        public static QaResult Run(IReadOnlyList<Vec3> points, IReadOnlyList<QaElement> elements, QaSettings settings,
            OrientedBox region = null)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (elements == null) throw new ArgumentNullException(nameof(elements));
            settings = settings ?? new QaSettings();
            settings.Validate();

            double threshold = Units.MmToFeet(settings.MissingThresholdMm);
            double axialMargin = Units.MmToFeet(settings.EffectiveAxialMarginMm);

            // Bounding-box pre-filter: only elements whose box reaches the region take part.
            var regionBox = region?.ModelAabb().Inflate(threshold);
            var active = new bool[elements.Count];
            var tris = new List<Triangle>();
            var owners = new List<int>();
            for (int e = 0; e < elements.Count; e++)
            {
                var el = elements[e];
                if (el.Triangles.Count == 0) continue;
                if (regionBox.HasValue && !regionBox.Value.Intersects(el.Bounds)) continue;
                active[e] = true;
                foreach (var t in el.Triangles)
                {
                    tris.Add(t);
                    owners.Add(e);
                }
            }
            var bvh = new TriangleBvh(tris, owners);

            int n = points.Count;
            var cls = new PointClass[n];
            var dev = new float[n];
            var owner = new int[n];
            var foot = new Vec3[n];
            var pts = new Vec3[n];

            Parallel.For(0, n, i =>
            {
                var p = points[i];
                pts[i] = p;
                Func<int, bool> accept = null;
                if (settings.UseAxialEndRule)
                {
                    accept = e =>
                    {
                        var axis = elements[e].Axis;
                        if (axis == null) return true;
                        double s = axis.AxialPosition(p);
                        return s >= -axialMargin && s <= axis.Length + axialMargin;
                    };
                }
                var hit = bvh.Nearest(p, threshold, accept);
                if (!hit.Found)
                {
                    cls[i] = PointClass.Unexplained;
                    dev[i] = float.NaN;
                    owner[i] = -1;
                    return;
                }
                owner[i] = hit.Owner;
                dev[i] = (float)Units.FeetToMm(hit.Distance);
                foot[i] = hit.ClosestPoint;
                cls[i] = elements[hit.Owner].Role == ElementRole.Target ? PointClass.Evaluated : PointClass.Context;
            });

            var result = new QaResult
            {
                Settings = settings,
                Points = pts,
                PointClasses = cls,
                DeviationMm = dev,
                PointOwner = owner,
                PointCluster = Enumerable.Repeat(-1, n).ToArray(),
            };

            BuildElementResults(result, elements, active, foot, settings, region);
            BuildClusters(result, elements, active, settings);
            return result;
        }

        private static void BuildElementResults(QaResult result, IReadOnlyList<QaElement> elements, bool[] active,
            Vec3[] foot, QaSettings s, OrientedBox region)
        {
            var devs = new List<float>[elements.Count];
            var foots = new List<Vec3>[elements.Count];
            for (int i = 0; i < result.Points.Length; i++)
            {
                if (result.PointClasses[i] != PointClass.Evaluated) continue;
                int e = result.PointOwner[i];
                (devs[e] = devs[e] ?? new List<float>()).Add(result.DeviationMm[i]);
                (foots[e] = foots[e] ?? new List<Vec3>()).Add(foot[i]);
            }

            for (int e = 0; e < elements.Count; e++)
            {
                var el = elements[e];
                if (!active[e] || el.Role != ElementRole.Target) continue;
                double coverage = Coverage(el, foots[e], s, region, out int samplesInRegion);
                // An element whose box touches the region but whose surface lies outside it is not reported.
                if (samplesInRegion == 0) continue;
                var d = devs[e] ?? new List<float>();
                var r = new ElementResult
                {
                    ElementId = el.Id,
                    Category = el.Category,
                    Name = el.Name,
                    PointCount = d.Count,
                    Bounds = el.Bounds,
                    Coverage = coverage,
                    P95Mm = double.NaN,
                    MaxMm = double.NaN,
                    MeanMm = double.NaN,
                    PercentWithinTolerance = double.NaN,
                };
                if (d.Count > 0)
                {
                    var sorted = d.ToArray();
                    Array.Sort(sorted);
                    r.P95Mm = Percentile(sorted, s.DecisionPercentile);
                    r.MaxMm = sorted[sorted.Length - 1];
                    r.MeanMm = sorted.Average(x => (double)x);
                    r.PercentWithinTolerance = 100.0 * sorted.Count(x => x <= s.ToleranceMm) / sorted.Length;
                }

                if (d.Count < s.MinPoints)
                {
                    r.Status = QaStatus.InsufficientScanData;
                    r.Reason = $"{d.Count} scan points (minimum {s.MinPoints})";
                }
                else if (r.Coverage < s.MinCoverage)
                {
                    r.Status = QaStatus.InsufficientScanData;
                    r.Reason = $"Scan covers {r.Coverage:P0} of the surface in the region (minimum {s.MinCoverage:P0})";
                }
                else if (r.P95Mm <= s.ToleranceMm)
                {
                    r.Status = QaStatus.Pass;
                    r.Reason = $"P{s.DecisionPercentile:0} {r.P95Mm:0.0} mm ≤ {s.ToleranceMm:0.#} mm";
                }
                else
                {
                    r.Status = QaStatus.Fail;
                    r.Reason = $"P{s.DecisionPercentile:0} {r.P95Mm:0.0} mm > {s.ToleranceMm:0.#} mm";
                }
                result.Elements.Add(r);
            }
        }

        /// <summary>Nearest-rank percentile of an ascending array.</summary>
        public static double Percentile(float[] sorted, double percentile)
        {
            if (sorted.Length == 0) return double.NaN;
            int rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Length);
            rank = Math.Max(1, Math.Min(sorted.Length, rank));
            return sorted[rank - 1];
        }

        /// <summary>
        /// Share of surface samples (inside the region) with a scan footprint within half a coverage cell. Footprints are the
        /// closest surface points of the attributed scan points, so an element that is offset but well scanned
        /// still counts as covered and is judged Fail, not Insufficient.
        /// </summary>
        public static double Coverage(QaElement el, List<Vec3> footprints, QaSettings s, OrientedBox region,
            out int samplesInRegion)
        {
            double cell = Units.MmToFeet(s.CoverageCellMm);
            var samples = SurfaceSamples(el, cell)
                .Where(p => region == null || region.Contains(p))
                .ToList();
            samplesInRegion = samples.Count;
            if (samples.Count == 0) return 0;
            if (footprints == null || footprints.Count == 0) return 0;

            // A sample is covered when a footprint lies within half a cell of it.
            double radius = cell * 0.5;
            double r2 = radius * radius;
            var grid = new Dictionary<VoxelKey, List<Vec3>>();
            foreach (var f in footprints)
            {
                var key = VoxelKey.Of(f, radius);
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<Vec3>();
                list.Add(f);
            }
            int covered = 0;
            foreach (var p in samples)
            {
                var k = VoxelKey.Of(p, radius);
                bool hit = false;
                for (int dx = -1; dx <= 1 && !hit; dx++)
                for (int dy = -1; dy <= 1 && !hit; dy++)
                for (int dz = -1; dz <= 1 && !hit; dz++)
                {
                    if (!grid.TryGetValue(k.Offset(dx, dy, dz), out var list)) continue;
                    foreach (var f in list)
                        if (f.DistanceSquaredTo(p) <= r2) { hit = true; break; }
                }
                if (hit) covered++;
            }
            return (double)covered / samples.Count;
        }

        /// <summary>Deterministic, roughly uniform samples over the element surface, about one per cell².</summary>
        public static IEnumerable<Vec3> SurfaceSamples(QaElement el, double cell)
        {
            const double g1 = 0.7548776662466927, g2 = 0.5698402909980532; // R2 low-discrepancy sequence
            double cellArea = cell * cell;
            foreach (var t in el.Triangles)
            {
                int count = Math.Max(1, (int)Math.Round(t.Area / cellArea));
                for (int k = 0; k < count; k++)
                {
                    double u = (0.5 + g1 * (k + 1)) % 1.0;
                    double v = (0.5 + g2 * (k + 1)) % 1.0;
                    if (u + v > 1)
                    {
                        u = 1 - u;
                        v = 1 - v;
                    }
                    yield return t.A + (t.B - t.A) * u + (t.C - t.A) * v;
                }
            }
        }

        private static void BuildClusters(QaResult result, IReadOnlyList<QaElement> elements, bool[] active, QaSettings s)
        {
            double cell = Units.MmToFeet(s.ClusterCellMm);
            var voxels = new Dictionary<VoxelKey, List<int>>();
            for (int i = 0; i < result.Points.Length; i++)
            {
                if (result.PointClasses[i] != PointClass.Unexplained) continue;
                var k = VoxelKey.Of(result.Points[i], cell);
                if (!voxels.TryGetValue(k, out var list)) voxels[k] = list = new List<int>();
                list.Add(i);
            }

            // Only dense voxels seed and grow clusters, so isolated noise points never form one.
            var core = new HashSet<VoxelKey>(voxels.Where(kv => kv.Value.Count >= s.MinPointsPerCell).Select(kv => kv.Key));
            var seen = new HashSet<VoxelKey>();
            var clusters = new List<List<int>>();
            foreach (var start in core.OrderBy(k => k.X).ThenBy(k => k.Y).ThenBy(k => k.Z))
            {
                if (!seen.Add(start)) continue;
                var members = new List<int>();
                var queue = new Queue<VoxelKey>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var k = queue.Dequeue();
                    members.AddRange(voxels[k]);
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var nb = k.Offset(dx, dy, dz);
                        if (core.Contains(nb) && seen.Add(nb)) queue.Enqueue(nb);
                    }
                }
                if (members.Count >= s.MinClusterPoints) clusters.Add(members);
            }

            var openEnds = new List<(Vec3 p, long id)>();
            for (int e = 0; e < elements.Count; e++)
                if (active[e])
                    foreach (var oe in elements[e].OpenEnds) openEnds.Add((oe, elements[e].Id));
            double search = Units.MmToFeet(s.OpenEndSearchMm);

            int id = 1;
            foreach (var members in clusters.OrderByDescending(c => c.Count))
            {
                var box = Aabb.Empty;
                var sum = Vec3.Zero;
                foreach (int i in members)
                {
                    box = box.Include(result.Points[i]);
                    sum += result.Points[i];
                    result.PointCluster[i] = id;
                }
                var size = box.Size;
                var c = new MissingCluster
                {
                    Id = id,
                    PointCount = members.Count,
                    Centroid = sum / members.Count,
                    Bounds = box,
                    ExtentMm = Units.FeetToMm(Math.Max(size.X, Math.Max(size.Y, size.Z))),
                };
                double best = double.MaxValue;
                foreach (var (p, eid) in openEnds)
                {
                    double d = Math.Sqrt(box.DistanceSquared(p));
                    if (d <= search && d < best)
                    {
                        best = d;
                        c.NearOpenEndOfElementId = eid;
                        c.DistanceToOpenEndMm = Units.FeetToMm(d);
                    }
                }
                c.Note = c.NearOpenEndOfElementId.HasValue
                    ? $"Next to the open end of element {c.NearOpenEndOfElementId} — possible missing fitting"
                    : "Scan data without a model surface nearby";
                result.Clusters.Add(c);
                id++;
            }
        }
    }
}
