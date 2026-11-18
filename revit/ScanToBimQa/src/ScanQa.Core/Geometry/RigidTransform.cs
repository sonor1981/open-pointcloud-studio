using System;

namespace ScanQa.Core.Geometry
{
    /// <summary>
    /// Affine transform in the same form as Autodesk.Revit.DB.Transform:
    /// p_out = Origin + BasisX * p.X + BasisY * p.Y + BasisZ * p.Z.
    /// Revit point cloud instance and section box transforms are rigid, but the inverse is
    /// computed for a general 3x3 matrix so a scaled transform is still handled exactly.
    /// </summary>
    public sealed class RigidTransform
    {
        public Vec3 Origin { get; }
        public Vec3 BasisX { get; }
        public Vec3 BasisY { get; }
        public Vec3 BasisZ { get; }

        public RigidTransform(Vec3 origin, Vec3 basisX, Vec3 basisY, Vec3 basisZ)
        {
            Origin = origin;
            BasisX = basisX;
            BasisY = basisY;
            BasisZ = basisZ;
        }

        public static RigidTransform Identity { get; } =
            new RigidTransform(Vec3.Zero, Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ);

        public static RigidTransform Translation(Vec3 t) => new RigidTransform(t, Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ);

        /// <summary>Rotation about the model Z axis through the origin, followed by a translation.</summary>
        public static RigidTransform RotationZ(double radians, Vec3 translation)
        {
            double c = Math.Cos(radians), s = Math.Sin(radians);
            return new RigidTransform(translation, new Vec3(c, s, 0), new Vec3(-s, c, 0), Vec3.UnitZ);
        }

        /// <summary>Rotation about an arbitrary unit axis (Rodrigues), followed by a translation.</summary>
        public static RigidTransform Rotation(Vec3 axis, double radians, Vec3 translation)
        {
            var a = axis.Normalize();
            double c = Math.Cos(radians), s = Math.Sin(radians), t = 1 - c;
            Vec3 Col(Vec3 e) =>
                e * c + a.Cross(e) * s + a * (a.Dot(e) * t);
            return new RigidTransform(translation, Col(Vec3.UnitX), Col(Vec3.UnitY), Col(Vec3.UnitZ));
        }

        public Vec3 OfPoint(Vec3 p) => Origin + BasisX * p.X + BasisY * p.Y + BasisZ * p.Z;
        public Vec3 OfVector(Vec3 v) => BasisX * v.X + BasisY * v.Y + BasisZ * v.Z;

        /// <summary>this ∘ other: apply <paramref name="other"/> first, then this.</summary>
        public RigidTransform Multiply(RigidTransform other) =>
            new RigidTransform(OfPoint(other.Origin), OfVector(other.BasisX), OfVector(other.BasisY), OfVector(other.BasisZ));

        public RigidTransform Inverse()
        {
            // Columns of M are the basis vectors. Inverse of M = adjugate / det.
            Vec3 c0 = BasisX, c1 = BasisY, c2 = BasisZ;
            double det = c0.Dot(c1.Cross(c2));
            if (Math.Abs(det) < 1e-12) throw new InvalidOperationException("Transform is singular.");
            // Rows of M^-1 are (c1 x c2, c2 x c0, c0 x c1) / det.
            Vec3 r0 = c1.Cross(c2) / det;
            Vec3 r1 = c2.Cross(c0) / det;
            Vec3 r2 = c0.Cross(c1) / det;
            // Columns of the inverse are built from the rows above.
            var bx = new Vec3(r0.X, r1.X, r2.X);
            var by = new Vec3(r0.Y, r1.Y, r2.Y);
            var bz = new Vec3(r0.Z, r1.Z, r2.Z);
            var inv = new RigidTransform(Vec3.Zero, bx, by, bz);
            var o = inv.OfVector(Origin);
            return new RigidTransform(-o, bx, by, bz);
        }
    }
}
