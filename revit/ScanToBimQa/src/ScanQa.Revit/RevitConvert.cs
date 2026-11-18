using Autodesk.Revit.DB;
using ScanQa.Core.Geometry;

namespace ScanQa.Revit
{
    /// <summary>Conversions between the Revit API types and the Revit-independent core types (both in feet).</summary>
    internal static class RevitConvert
    {
        public static Vec3 ToVec(XYZ p) => new Vec3(p.X, p.Y, p.Z);
        public static XYZ ToXyz(Vec3 p) => new XYZ(p.X, p.Y, p.Z);

        public static RigidTransform ToRigid(Transform t) =>
            new RigidTransform(ToVec(t.Origin), ToVec(t.BasisX), ToVec(t.BasisY), ToVec(t.BasisZ));

        /// <summary>View3D.GetSectionBox(): Min/Max in box coordinates, Transform maps box to model (may be rotated).</summary>
        public static OrientedBox ToOrientedBox(BoundingBoxXYZ box) =>
            new OrientedBox(ToRigid(box.Transform), ToVec(box.Min), ToVec(box.Max));

        public static long ToLong(ElementId id)
        {
#if REVIT2024_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        public static ElementId ToId(long value)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(value);
#else
            return new ElementId((int)value);
#endif
        }
    }
}
