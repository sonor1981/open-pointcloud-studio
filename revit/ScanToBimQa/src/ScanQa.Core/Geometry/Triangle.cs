namespace ScanQa.Core.Geometry
{
    public readonly struct Triangle
    {
        public readonly Vec3 A;
        public readonly Vec3 B;
        public readonly Vec3 C;

        public Triangle(Vec3 a, Vec3 b, Vec3 c)
        {
            A = a;
            B = b;
            C = c;
        }

        public Aabb Bounds => new Aabb(Vec3.Min(Vec3.Min(A, B), C), Vec3.Max(Vec3.Max(A, B), C));
        public double Area => (B - A).Cross(C - A).Length * 0.5;

        /// <summary>Closest point on the triangle to p (Ericson, Real-Time Collision Detection, 5.1.5).</summary>
        public Vec3 ClosestPoint(Vec3 p)
        {
            Vec3 ab = B - A, ac = C - A, ap = p - A;
            double d1 = ab.Dot(ap), d2 = ac.Dot(ap);
            if (d1 <= 0 && d2 <= 0) return A;

            Vec3 bp = p - B;
            double d3 = ab.Dot(bp), d4 = ac.Dot(bp);
            if (d3 >= 0 && d4 <= d3) return B;

            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0)
            {
                double v = d1 / (d1 - d3);
                return A + ab * v;
            }

            Vec3 cp = p - C;
            double d5 = ab.Dot(cp), d6 = ac.Dot(cp);
            if (d6 >= 0 && d5 <= d6) return C;

            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0)
            {
                double w = d2 / (d2 - d6);
                return A + ac * w;
            }

            double va = d3 * d6 - d5 * d4;
            if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
            {
                double w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return B + (C - B) * w;
            }

            double denom = 1.0 / (va + vb + vc);
            double vv = vb * denom, ww = vc * denom;
            return A + ab * vv + ac * ww;
        }
    }
}
