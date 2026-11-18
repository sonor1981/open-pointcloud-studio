using System.Collections.Generic;
using ScanQa.Core.Geometry;

namespace ScanQa.Core.Qa
{
    /// <summary>
    /// Brings raw point cloud samples into the check region. Revit returns PointCloudInstance.GetPoints in the
    /// cloud's own coordinates; the instance transform maps them into the model, and the section box (which may
    /// be rotated) is tested in its own local frame.
    /// </summary>
    public static class PointPreparation
    {
        /// <summary>Section box planes in cloud-local coordinates, for Revit's PointCloudFilterFactory.CreateMultiPlaneFilter.</summary>
        public static IReadOnlyList<ClipPlane> CloudLocalFilterPlanes(OrientedBox sectionBox, RigidTransform cloudToModel) =>
            sectionBox.InwardPlanes(cloudToModel.Inverse());

        /// <summary>
        /// Transforms cloud-local points to model coordinates and keeps those inside the section box. The Revit
        /// plane filter is coarse (it works on cloud cells), so the exact test is repeated here.
        /// </summary>
        public static List<Vec3> ToModelInsideBox(IEnumerable<Vec3> cloudLocalPoints, RigidTransform cloudToModel,
            OrientedBox sectionBox)
        {
            var result = new List<Vec3>();
            foreach (var p in cloudLocalPoints)
            {
                var m = cloudToModel.OfPoint(p);
                if (sectionBox == null || sectionBox.Contains(m)) result.Add(m);
            }
            return result;
        }
    }
}
