using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.PointClouds;
using ScanQa.Core.Geometry;
using Units = ScanQa.Core.Geometry.Units;
using ScanQa.Core.Qa;

namespace ScanQa.Revit
{
    internal sealed class PointReadResult
    {
        public List<Vec3> ModelPoints { get; } = new List<Vec3>();
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>
    /// Reads points of a linked point cloud (.rcp/.rcs) inside the section box through
    /// PointCloudInstance.GetPoints. Verified in Revit 2026.5: the filter planes are taken in MODEL coordinates,
    /// while the returned points are in the cloud's own coordinates (metres for .rcs, so the instance transform
    /// carries a 3.2808 scale). Points are moved to the model with that transform and tested exactly against
    /// the (possibly rotated) section box.
    /// </summary>
    internal static class PointCloudReader
    {
        /// <summary>Points requested per GetPoints call (Revit 2026.5 rejects 1,000,000). Larger regions are read in slabs.</summary>
        public const int PointsPerCall = 999_999;

        [ThreadStatic] private static int NullReads;

        public static PointReadResult Read(PointCloudInstance cloud, OrientedBox sectionBox, double spacingMm, int maxPoints)
        {
            NullReads = 0;
            var result = new PointReadResult();
            var cloudToModel = RevitConvert.ToRigid(cloud.GetTransform());
            int slabs = Math.Max(1, (int)Math.Ceiling((double)maxPoints / PointsPerCall));
            int perSlab = Math.Max(1, Math.Min(PointsPerCall, maxPoints / slabs));
            double spacing = Units.MmToFeet(spacingMm);
            int returned = 0;
            bool usedCloudPlanes = false;

            foreach (var slab in Slabs(sectionBox, slabs))
            {
                var local = ReadSlab(cloud, slab, cloudToModel, spacing, perSlab, cloudFrame: false, out int count);
                var inside = PointPreparation.ToModelInsideBox(local, cloudToModel, slab);
                if (inside.Count == 0)
                {
                    // Safety net for releases that would take the planes in cloud coordinates.
                    local = ReadSlab(cloud, slab, cloudToModel, spacing, perSlab, cloudFrame: true, out int count2);
                    var inside2 = PointPreparation.ToModelInsideBox(local, cloudToModel, slab);
                    if (inside2.Count > 0)
                    {
                        usedCloudPlanes = true;
                        inside = inside2;
                        count = count2;
                    }
                }
                returned += count;
                result.ModelPoints.AddRange(inside);
            }

            result.Notes.Add($"{cloud.Name}: {returned:N0} points returned by Revit, {result.ModelPoints.Count:N0} inside the section box " +
                             $"(spacing {spacingMm:0.#} mm, {slabs} read(s)).");
            if (NullReads > 0)
                result.Notes.Add($"{cloud.Name}: Revit returned no point data for {NullReads} read(s); part of the box may be missing. Run Check again, or reduce Max points.");
            if (usedCloudPlanes) result.Notes.Add($"{cloud.Name}: filter planes were applied in cloud coordinates (fallback).");
            if (returned >= slabs * perSlab * 0.98)
                result.Notes.Add($"{cloud.Name}: point limit reached — increase the limit or the spacing, or reduce the section box.");
            return result;
        }

        private static List<Vec3> ReadSlab(PointCloudInstance cloud, OrientedBox slab, RigidTransform cloudToModel,
            double spacingFeet, int numPoints, bool cloudFrame, out int count)
        {
            var planes = (cloudFrame
                    ? PointPreparation.CloudLocalFilterPlanes(slab, cloudToModel)
                    : slab.InwardPlanes())
                .Select(p => Plane.CreateByNormalAndOrigin(RevitConvert.ToXyz(p.Normal), RevitConvert.ToXyz(p.Origin)))
                .ToList();
            PointCloudFilter filter = PointCloudFilterFactory.CreateMultiPlaneFilter(planes);
            // GetPoints can return null (seen in Revit 2026.5 on a sub-region after the view changed). Retry once.
            PointCollection points = cloud.GetPoints(filter, spacingFeet, numPoints)
                                     ?? cloud.GetPoints(filter, spacingFeet, numPoints);
            var local = new List<Vec3>(points?.Count ?? 0);
            if (points != null)
                foreach (CloudPoint cp in points) local.Add(new Vec3(cp.X, cp.Y, cp.Z));
            else
                NullReads++;
            count = local.Count;
            return local;
        }

        /// <summary>Splits the box along its longest local axis.</summary>
        private static IEnumerable<OrientedBox> Slabs(OrientedBox box, int count)
        {
            if (count <= 1)
            {
                yield return box;
                yield break;
            }
            var size = box.Max - box.Min;
            int axis = size.X >= size.Y && size.X >= size.Z ? 0 : (size.Y >= size.Z ? 1 : 2);
            for (int i = 0; i < count; i++)
            {
                double a = (double)i / count, b = (double)(i + 1) / count;
                Vec3 min = box.Min, max = box.Max;
                switch (axis)
                {
                    case 0:
                        min = new Vec3(box.Min.X + size.X * a, min.Y, min.Z);
                        max = new Vec3(box.Min.X + size.X * b, max.Y, max.Z);
                        break;
                    case 1:
                        min = new Vec3(min.X, box.Min.Y + size.Y * a, min.Z);
                        max = new Vec3(max.X, box.Min.Y + size.Y * b, max.Z);
                        break;
                    default:
                        min = new Vec3(min.X, min.Y, box.Min.Z + size.Z * a);
                        max = new Vec3(max.X, max.Y, box.Min.Z + size.Z * b);
                        break;
                }
                yield return new OrientedBox(box.Transform, min, max);
            }
        }
    }
}
