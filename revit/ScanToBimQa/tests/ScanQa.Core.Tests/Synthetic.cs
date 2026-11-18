using System;
using System.Collections.Generic;
using ScanQa.Core.Geometry;
using ScanQa.Core.Qa;

namespace ScanQa.Core.Tests
{
    /// <summary>Generators for model geometry and simulated scans. Lengths are given in mm and returned in feet.</summary>
    internal static class Synthetic
    {
        public static double F(double mm) => Units.MmToFeet(mm);
        public static Vec3 Mm(double x, double y, double z) => new Vec3(F(x), F(y), F(z));

        /// <summary>Deterministic pseudo-random numbers (no dependency on System.Random's algorithm).</summary>
        public sealed class Rng
        {
            private ulong _s;
            public Rng(ulong seed) { _s = seed * 6364136223846793005UL + 1442695040888963407UL; }
            public double Next()
            {
                _s = _s * 6364136223846793005UL + 1442695040888963407UL;
                return (_s >> 11) * (1.0 / 9007199254740992.0);
            }
            public double Range(double a, double b) => a + (b - a) * Next();
        }

        private static (Vec3 u, Vec3 v) Frame(Vec3 axisDir)
        {
            var w = axisDir.Normalize();
            var helper = Math.Abs(w.Z) < 0.9 ? Vec3.UnitZ : Vec3.UnitX;
            var u = w.Cross(helper).Normalize();
            var v = w.Cross(u).Normalize();
            return (u, v);
        }

        /// <summary>Closed cylinder mesh (lateral faces + end caps), like Revit's tessellation of a pipe solid.</summary>
        public static QaElement Pipe(long id, Vec3 start, Vec3 end, double radiusMm, string category = "Pipes",
            int segments = 48, bool withAxis = true)
        {
            var el = new QaElement { Id = id, Category = category, Name = "Pipe " + id };
            double r = F(radiusMm);
            var (u, v) = Frame(end - start);
            var ring0 = new Vec3[segments];
            var ring1 = new Vec3[segments];
            for (int k = 0; k < segments; k++)
            {
                double a = 2 * Math.PI * k / segments;
                var off = u * (Math.Cos(a) * r) + v * (Math.Sin(a) * r);
                ring0[k] = start + off;
                ring1[k] = end + off;
            }
            for (int k = 0; k < segments; k++)
            {
                int k1 = (k + 1) % segments;
                el.Triangles.Add(new Triangle(ring0[k], ring0[k1], ring1[k1]));
                el.Triangles.Add(new Triangle(ring0[k], ring1[k1], ring1[k]));
                el.Triangles.Add(new Triangle(start, ring0[k1], ring0[k]));
                el.Triangles.Add(new Triangle(end, ring1[k], ring1[k1]));
            }
            if (withAxis) el.Axis = new LinearAxis(start, end);
            return el;
        }

        /// <summary>Box mesh (12 triangles) for walls and slabs.</summary>
        public static QaElement Box(long id, Vec3 min, Vec3 max, string category = "Walls", ElementRole role = ElementRole.Context)
        {
            var el = new QaElement { Id = id, Category = category, Name = category + " " + id, Role = role };
            var c = new Vec3[8];
            for (int i = 0; i < 8; i++)
                c[i] = new Vec3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
            int[][] quads =
            {
                new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 }, new[] { 0, 4, 5, 1 },
                new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 },
            };
            foreach (var q in quads)
            {
                el.Triangles.Add(new Triangle(c[q[0]], c[q[1]], c[q[2]]));
                el.Triangles.Add(new Triangle(c[q[0]], c[q[2]], c[q[3]]));
            }
            return el;
        }

        /// <summary>
        /// Scan of the visible half of a true (smooth) cylinder: points every <paramref name="spacingMm"/>,
        /// radial noise ±<paramref name="noiseMm"/>, angles centred on <paramref name="viewDir"/>.
        /// </summary>
        public static List<Vec3> ScanCylinder(Vec3 start, Vec3 end, double radiusMm, Vec3 viewDir, double spacingMm,
            double noiseMm, Rng rng, double fromMm = 0, double toMm = double.NaN, double arcDegrees = 180)
        {
            var pts = new List<Vec3>();
            var w = end - start;
            double len = Units.FeetToMm(w.Length);
            if (double.IsNaN(toMm)) toMm = len;
            var (u, v) = Frame(w);
            // Angle of the view direction in the (u, v) frame.
            double a0 = Math.Atan2(viewDir.Dot(v), viewDir.Dot(u));
            double arc = arcDegrees * Math.PI / 180;
            int nAng = Math.Max(2, (int)Math.Ceiling(arc * radiusMm / spacingMm));
            var axis = w.Normalize();
            for (double s = fromMm; s <= toMm; s += spacingMm)
            {
                for (int k = 0; k <= nAng; k++)
                {
                    double a = a0 - arc / 2 + arc * k / nAng;
                    double r = radiusMm + rng.Range(-noiseMm, noiseMm);
                    var off = u * (Math.Cos(a) * F(r)) + v * (Math.Sin(a) * F(r));
                    pts.Add(start + axis * F(s) + off);
                }
            }
            return pts;
        }

        /// <summary>Scan of a 90° elbow (torus section) joining two pipes: centre of bend, start/end tangents.</summary>
        public static List<Vec3> ScanElbow(Vec3 bendCentre, Vec3 dirToStart, Vec3 dirToEnd, double bendRadiusMm,
            double pipeRadiusMm, double spacingMm, double noiseMm, Rng rng)
        {
            var pts = new List<Vec3>();
            var e1 = dirToStart.Normalize();
            var e2 = dirToEnd.Normalize();
            var normal = e1.Cross(e2).Normalize();
            double arcLen = Math.PI / 2 * bendRadiusMm;
            int nArc = (int)Math.Ceiling(arcLen / spacingMm);
            int nRing = (int)Math.Ceiling(2 * Math.PI * pipeRadiusMm / spacingMm);
            for (int i = 0; i <= nArc; i++)
            {
                double t = Math.PI / 2 * i / nArc;
                var radial = e1 * Math.Cos(t) + e2 * Math.Sin(t);
                var centre = bendCentre + radial * F(bendRadiusMm);
                for (int k = 0; k < nRing; k++)
                {
                    double a = 2 * Math.PI * k / nRing;
                    // Only the upper/outer half is visible to a scanner above the run.
                    if (Math.Sin(a) < 0) continue;
                    double r = pipeRadiusMm + rng.Range(-noiseMm, noiseMm);
                    var off = radial * (Math.Cos(a) * F(r)) + normal * (Math.Sin(a) * F(r));
                    pts.Add(centre + off);
                }
            }
            return pts;
        }

        /// <summary>Scan of a plane patch (wall face) on a regular grid.</summary>
        public static List<Vec3> ScanPlane(Vec3 origin, Vec3 du, Vec3 dv, double sizeUmm, double sizeVmm, double spacingMm,
            double noiseMm, Rng rng)
        {
            var pts = new List<Vec3>();
            var n = du.Cross(dv).Normalize();
            for (double a = 0; a <= sizeUmm; a += spacingMm)
            for (double b = 0; b <= sizeVmm; b += spacingMm)
                pts.Add(origin + du.Normalize() * F(a) + dv.Normalize() * F(b) + n * F(rng.Range(-noiseMm, noiseMm)));
            return pts;
        }

        public static List<Vec3> ScatterNoise(Aabb box, int count, Rng rng)
        {
            var pts = new List<Vec3>();
            for (int i = 0; i < count; i++)
                pts.Add(new Vec3(rng.Range(box.Min.X, box.Max.X), rng.Range(box.Min.Y, box.Max.Y), rng.Range(box.Min.Z, box.Max.Z)));
            return pts;
        }
    }
}
