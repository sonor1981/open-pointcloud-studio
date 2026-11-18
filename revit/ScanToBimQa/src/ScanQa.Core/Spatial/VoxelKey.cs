using System;
using ScanQa.Core.Geometry;

namespace ScanQa.Core.Spatial
{
    public readonly struct VoxelKey : IEquatable<VoxelKey>
    {
        public readonly long X;
        public readonly long Y;
        public readonly long Z;

        public VoxelKey(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static VoxelKey Of(Vec3 p, double cell) =>
            new VoxelKey((long)Math.Floor(p.X / cell), (long)Math.Floor(p.Y / cell), (long)Math.Floor(p.Z / cell));

        public VoxelKey Offset(long dx, long dy, long dz) => new VoxelKey(X + dx, Y + dy, Z + dz);

        public bool Equals(VoxelKey o) => X == o.X && Y == o.Y && Z == o.Z;
        public override bool Equals(object obj) => obj is VoxelKey k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                long h = X * 73856093L ^ Y * 19349663L ^ Z * 83492791L;
                return (int)(h ^ (h >> 32));
            }
        }
    }
}
