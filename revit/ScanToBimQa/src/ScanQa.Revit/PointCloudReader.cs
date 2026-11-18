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
    /// PointCloudInstance.GetPoints. Filter planes are built in cloud coordinates; returned points are moved to
    /// the model with the instance transform and tested exactly against the (possibly rotated) section box.
    /// </summary>
    internal static class PointCloudReader
    {
        /// <summary>Points requested per GetPoints call. Larger regions are read in slabs.</summary>
        private const int PointsPerCall = 1_000_000;

        public static PointReadResult Read(PointCloudInstance cloud, OrientedBox sectionBox, double spacingMm, int maxPoints)
        {
            var result = new PointReadResult();
            var cloudToModel = RevitConvert.ToRigid(cloud.GetTransform());
            int slabs = Math.Max(1, (int)Math.Ceiling((double)maxPoints / PointsPerCall));
            int perSlab = Math.Max(1, maxPoints / slabs);
            double spacing = Units.MmToFeet(spacingMm);
            int returned = 0;
            bool usedModelPlanes = false;

            foreach (var slab in Slabs(sectionBox, slabs))
            {
                var local = ReadSlab(cloud, slab, cloudToModel, spacing, perSlab, cloudFrame: true, out int count);
                var inside = PointPreparation.ToModelInsideBox(local, cloudToModel, slab);
                if (count > 0 && inside.Count == 0)
                {
                    // Safety net: if Revit expects the planes in model coordinates for this cloud, the cloud-frame
                    // read lands outside the box. Read again with model-frame planes and keep what is inside.
                    local = ReadSlab(cloud, slab, cloudToModel, spacing, perSlab, cloudFrame: false, out count);
                    inside = PointPreparation.ToModelInsideBox(local, cloudToModel, slab);
                    if (inside.Count > 0) usedModelPlanes = true;
                }
                returned += count;
                result.ModelPoints.AddRange(inside);
            }

            result.Notes.Add($"{cloud.Name}: {returned:N0} points returned by Revit, {result.ModelPoints.Count:N0} inside the section box " +
                             $"(spacing {spacingMm:0.#} mm, {slabs} read(s)).");
            if (usedModelPlanes) result.Notes.Add($"{cloud.Name}: filter planes were applied in model coordinates (fallback).");
            if (returned >= maxPoints * 0.98)
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
            PointCollection points = cloud.GetPoints(filter, spacingFeet, numPoints);
            var local = new List<Vec3>(points.Count);
            foreach (CloudPoint cp in points) local.Add(new Vec3(cp.X, cp.Y, cp.Z));
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
