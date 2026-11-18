using System.Collections.Generic;

namespace ScanQa.Core.Geometry
{
    /// <summary>Half-space boundary: points with (p - Origin)·Normal ≥ 0 are inside.</summary>
    public readonly struct ClipPlane
    {
        public readonly Vec3 Origin;
        public readonly Vec3 Normal;

        public ClipPlane(Vec3 origin, Vec3 normal)
        {
            Origin = origin;
            Normal = normal;
        }

        public double SignedDistance(Vec3 p) => (p - Origin).Dot(Normal);
    }

    /// <summary>
    /// A possibly rotated box, in the form of Revit's View3D.GetSectionBox():
    /// <see cref="Min"/>/<see cref="Max"/> are in box-local coordinates and
    /// <see cref="Transform"/> maps box-local to model coordinates.
    /// </summary>
    public sealed class OrientedBox
    {
        private readonly RigidTransform _inverse;

        public RigidTransform Transform { get; }
        public Vec3 Min { get; }
        public Vec3 Max { get; }

        public OrientedBox(RigidTransform transform, Vec3 min, Vec3 max)
        {
            Transform = transform;
            Min = min;
            Max = max;
            _inverse = transform.Inverse();
        }

        public bool Contains(Vec3 modelPoint, double tolerance = 0)
        {
            var q = _inverse.OfPoint(modelPoint);
            return q.X >= Min.X - tolerance && q.X <= Max.X + tolerance &&
                   q.Y >= Min.Y - tolerance && q.Y <= Max.Y + tolerance &&
                   q.Z >= Min.Z - tolerance && q.Z <= Max.Z + tolerance;
        }

        public IEnumerable<Vec3> ModelCorners()
        {
            for (int i = 0; i < 8; i++)
            {
                var local = new Vec3((i & 1) == 0 ? Min.X : Max.X, (i & 2) == 0 ? Min.Y : Max.Y, (i & 4) == 0 ? Min.Z : Max.Z);
                yield return Transform.OfPoint(local);
            }
        }

        /// <summary>Model-space AABB enclosing the rotated box (used for bounding-box pre-filters).</summary>
        public Aabb ModelAabb() => Aabb.FromPoints(ModelCorners());

        /// <summary>
        /// The six inward-facing boundary planes, expressed in the coordinate system that
        /// <paramref name="modelToTarget"/> maps model coordinates into (for example the inverse of a
        /// point cloud instance transform, giving planes in cloud-local coordinates for a Revit
        /// PointCloudFilter).
        /// </summary>
        public IReadOnlyList<ClipPlane> InwardPlanes(RigidTransform modelToTarget = null)
        {
            var map = modelToTarget ?? RigidTransform.Identity;
            var local = new (Vec3 origin, Vec3 normal)[]
            {
                (Min, Vec3.UnitX), (Max, -Vec3.UnitX),
                (Min, Vec3.UnitY), (Max, -Vec3.UnitY),
                (Min, Vec3.UnitZ), (Max, -Vec3.UnitZ),
            };
            var planes = new List<ClipPlane>(6);
            foreach (var (o, n) in local)
            {
                var modelOrigin = Transform.OfPoint(o);
                var modelNormal = Transform.OfVector(n).Normalize();
                planes.Add(new ClipPlane(map.OfPoint(modelOrigin), map.OfVector(modelNormal).Normalize()));
            }
            return planes;
        }
    }
}
