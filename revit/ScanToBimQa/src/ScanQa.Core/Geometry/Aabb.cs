using System;
using System.Collections.Generic;

namespace ScanQa.Core.Geometry
{
    /// <summary>Axis-aligned bounding box.</summary>
    public readonly struct Aabb
    {
        public readonly Vec3 Min;
        public readonly Vec3 Max;

        public Aabb(Vec3 min, Vec3 max)
        {
            Min = min;
            Max = max;
        }

        public static readonly Aabb Empty = new Aabb(
            new Vec3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity),
            new Vec3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));

        public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;
        public Vec3 Center => (Min + Max) * 0.5;
        public Vec3 Size => Max - Min;

        public Aabb Include(Vec3 p) => new Aabb(Vec3.Min(Min, p), Vec3.Max(Max, p));
        public Aabb Union(Aabb b) => new Aabb(Vec3.Min(Min, b.Min), Vec3.Max(Max, b.Max));
        public Aabb Inflate(double d) => new Aabb(Min - new Vec3(d, d, d), Max + new Vec3(d, d, d));

        public bool Contains(Vec3 p) =>
            p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;

        public bool Intersects(Aabb b) =>
            Min.X <= b.Max.X && Max.X >= b.Min.X &&
            Min.Y <= b.Max.Y && Max.Y >= b.Min.Y &&
            Min.Z <= b.Max.Z && Max.Z >= b.Min.Z;

        /// <summary>Squared distance from a point to the box (0 when inside).</summary>
        public double DistanceSquared(Vec3 p)
        {
            double dx = Math.Max(0, Math.Max(Min.X - p.X, p.X - Max.X));
            double dy = Math.Max(0, Math.Max(Min.Y - p.Y, p.Y - Max.Y));
            double dz = Math.Max(0, Math.Max(Min.Z - p.Z, p.Z - Max.Z));
            return dx * dx + dy * dy + dz * dz;
        }

        public static Aabb FromPoints(IEnumerable<Vec3> points)
        {
            var box = Empty;
            foreach (var p in points) box = box.Include(p);
            return box;
        }
    }
}
