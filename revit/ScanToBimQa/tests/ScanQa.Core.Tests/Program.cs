using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using ScanQa.Core.Geometry;
using ScanQa.Core.Qa;
using ScanQa.Core.Spatial;
using static ScanQa.Core.Tests.Synthetic;

namespace ScanQa.Core.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class TestAttribute : Attribute { }

    internal sealed class AssertException : Exception
    {
        public AssertException(string m) : base(m) { }
    }

    internal static class Assert
    {
        public static void True(bool c, string m) { if (!c) throw new AssertException(m); }
        public static void Equal<T>(T expected, T actual, string m)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertException($"{m}: expected {expected}, got {actual}");
        }
        public static void Near(double expected, double actual, double tol, string m)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tol)
                throw new AssertException($"{m}: expected {expected}±{tol}, got {actual}");
        }
        public static void Throws<T>(Action a, string m) where T : Exception
        {
            try { a(); }
            catch (T) { return; }
            throw new AssertException(m + ": expected " + typeof(T).Name);
        }
    }

    public static class Program
    {
        public static int Main(string[] args)
        {
            var tests = typeof(Program).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .Where(m => args.Length == 0 || args.Any(a => m.Name.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name)
                .ToList();
            int failed = 0;
            foreach (var t in tests)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    t.Invoke(null, null);
                    Console.WriteLine($"PASS  {t.DeclaringType.Name}.{t.Name} ({sw.ElapsedMilliseconds} ms)");
                }
                catch (TargetInvocationException ex)
                {
                    failed++;
                    Console.WriteLine($"FAIL  {t.DeclaringType.Name}.{t.Name}: {ex.InnerException?.Message}");
                    if (!(ex.InnerException is AssertException)) Console.WriteLine(ex.InnerException);
                }
            }
            Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed");
            return failed == 0 ? 0 : 1;
        }
    }

    internal static class Scenario
    {
        public static readonly Vec3 Up = Vec3.UnitZ;

        public static QaSettings Settings() => new QaSettings { ToleranceMm = 20, MissingThresholdMm = 100 };

        public static ElementResult ResultOf(QaResult r, long id) => r.Elements.Single(e => e.ElementId == id);

        /// <summary>
        /// Two pipes that should meet at a 90° elbow. The elbow is in the scan but not in the model.
        /// Pipe 1 runs along X and ends at (2000, 0, 3000); pipe 2 starts at (2150, 150, 3000) and runs along Y.
        /// </summary>
        public static (List<QaElement> elements, List<Vec3> points, int elbowPoints) MissingElbow(bool withWall, ulong seed = 7)
        {
            var rng = new Rng(seed);
            var a0 = Mm(0, 0, 3000); var a1 = Mm(2000, 0, 3000);
            var b0 = Mm(2150, 150, 3000); var b1 = Mm(2150, 2000, 3000);
            var p1 = Pipe(1, a0, a1, 50);
            p1.OpenEnds.Add(a1);
            var p2 = Pipe(2, b0, b1, 50);
            p2.OpenEnds.Add(b0);
            var elements = new List<QaElement> { p1, p2 };

            var pts = new List<Vec3>();
            pts.AddRange(ScanCylinder(a0, a1, 50, Up, 10, 2, rng));
            pts.AddRange(ScanCylinder(b0, b1, 50, Up, 10, 2, rng));
            var elbow = ScanElbow(Mm(2000, 150, 3000), new Vec3(0, -1, 0), new Vec3(1, 0, 0), 150, 50, 10, 2, rng);
            pts.AddRange(elbow);

            if (withWall)
            {
                // Wall face at x = 2230 mm, 30 mm beyond the outside of the elbow (x = 2200 mm).
                elements.Add(Box(100, Mm(2230, -500, 2500), Mm(2430, 1000, 3600)));
                pts.AddRange(ScanPlane(Mm(2230, -500, 2500), Vec3.UnitY, Vec3.UnitZ, 1500, 1100, 25, 2, rng));
            }
            return (elements, pts, elbow.Count);
        }
    }

    internal static class GeometryTests
    {
        [Test]
        public static void Units_FeetAndMillimetres()
        {
            Assert.Near(304.8, Units.FeetToMm(1), 1e-12, "1 ft");
            Assert.Near(1, Units.MmToFeet(304.8), 1e-12, "304.8 mm");
            Assert.Near(20, Units.FeetToMm(Units.MmToFeet(20)), 1e-12, "round trip");
        }

        [Test]
        public static void Transform_InverseRoundTrip()
        {
            var t = RigidTransform.Rotation(new Vec3(0.3, -0.2, 1), 0.7, new Vec3(1000, -250, 13));
            var inv = t.Inverse();
            var rng = new Rng(1);
            for (int i = 0; i < 100; i++)
            {
                var p = new Vec3(rng.Range(-50, 50), rng.Range(-50, 50), rng.Range(-50, 50));
                Assert.Near(0, inv.OfPoint(t.OfPoint(p)).DistanceTo(p), 1e-9, "inverse(transform(p))");
                Assert.Near(0, t.Multiply(inv).OfPoint(p).DistanceTo(p), 1e-9, "t * inv = identity");
            }
        }

        [Test]
        public static void RotatedSectionBox_ContainmentUsesLocalFrame()
        {
            // 2 m x 1 m x 1 m box rotated 45° about Z.
            var box = new OrientedBox(RigidTransform.RotationZ(Math.PI / 4, Mm(5000, 5000, 0)), Mm(-1000, -500, 0), Mm(1000, 500, 1000));
            var inside = box.Transform.OfPoint(Mm(900, 0, 500));
            var cornerOfAabbOnly = Mm(5000 + 1000, 5000 - 1000, 500); // inside the world AABB, outside the rotated box
            Assert.True(box.Contains(inside), "point along rotated long axis is inside");
            Assert.True(box.ModelAabb().Contains(cornerOfAabbOnly), "test point lies in the world AABB");
            Assert.True(!box.Contains(cornerOfAabbOnly), "but outside the rotated box");
        }

        [Test]
        public static void CloudFilterPlanes_AgreeWithBoxInCloudCoordinates()
        {
            var cloudToModel = RigidTransform.Rotation(new Vec3(0.2, 0.1, 1), 35 * Math.PI / 180, new Vec3(1234.5, -987.6, 12.3));
            var box = new OrientedBox(RigidTransform.RotationZ(15 * Math.PI / 180, new Vec3(1240, -980, 20)), new Vec3(-5, -3, -2), new Vec3(5, 3, 2));
            var planes = PointPreparation.CloudLocalFilterPlanes(box, cloudToModel);
            Assert.Equal(6, planes.Count, "plane count");
            var rng = new Rng(3);
            var modelToCloud = cloudToModel.Inverse();
            int inside = 0;
            for (int i = 0; i < 2000; i++)
            {
                var model = new Vec3(rng.Range(1230, 1250), rng.Range(-990, -970), rng.Range(15, 25));
                var cloud = modelToCloud.OfPoint(model);
                bool byPlanes = planes.All(pl => pl.SignedDistance(cloud) >= 0);
                Assert.Equal(box.Contains(model), byPlanes, "plane test vs box test");
                if (byPlanes) inside++;
            }
            Assert.True(inside > 100 && inside < 1900, "sample hits both sides");
        }

        [Test]
        public static void Triangle_ClosestPointMatchesBruteForce()
        {
            var rng = new Rng(11);
            for (int i = 0; i < 200; i++)
            {
                var t = new Triangle(new Vec3(rng.Next(), rng.Next(), rng.Next()), new Vec3(rng.Next(), rng.Next(), rng.Next()), new Vec3(rng.Next(), rng.Next(), rng.Next()));
                var p = new Vec3(rng.Range(-1, 2), rng.Range(-1, 2), rng.Range(-1, 2));
                double best = double.MaxValue;
                for (int a = 0; a <= 100; a++)
                for (int b = 0; b <= 100 - a; b++)
                {
                    double u = a / 100.0, v = b / 100.0;
                    best = Math.Min(best, (t.A + (t.B - t.A) * u + (t.C - t.A) * v).DistanceTo(p));
                }
                double d = t.ClosestPoint(p).DistanceTo(p);
                Assert.True(d <= best + 1e-9, "closest point is not farther than any sample");
                Assert.True(best - d < 0.03, "closest point agrees with dense sampling");
            }
        }

        [Test]
        public static void Bvh_NearestMatchesBruteForce()
        {
            var rng = new Rng(5);
            var tris = new List<Triangle>();
            var owners = new List<int>();
            for (int i = 0; i < 500; i++)
            {
                var c = new Vec3(rng.Range(0, 20), rng.Range(0, 20), rng.Range(0, 20));
                tris.Add(new Triangle(c, c + new Vec3(rng.Next(), 0, 0), c + new Vec3(0, rng.Next(), rng.Next())));
                owners.Add(i % 7);
            }
            var bvh = new TriangleBvh(tris, owners);
            for (int q = 0; q < 300; q++)
            {
                var p = new Vec3(rng.Range(-2, 22), rng.Range(-2, 22), rng.Range(-2, 22));
                double brute = double.MaxValue;
                int bruteOwner = -1;
                for (int i = 0; i < tris.Count; i++)
                {
                    if (owners[i] == 3) continue;
                    double d = tris[i].ClosestPoint(p).DistanceTo(p);
                    if (d < brute) { brute = d; bruteOwner = owners[i]; }
                }
                var hit = bvh.Nearest(p, 100, o => o != 3);
                Assert.True(hit.Found, "found");
                Assert.Near(brute, hit.Distance, 1e-9, "BVH distance");
                Assert.Equal(bruteOwner, hit.Owner, "BVH owner");
                var limited = bvh.Nearest(p, brute * 0.5, o => o != 3);
                Assert.True(!limited.Found, "max distance respected");
            }
        }
    }

    internal static class ClassificationTests
    {
        [Test]
        public static void PipeWithinTolerance_Passes()
        {
            var rng = new Rng(21);
            var a = Mm(0, 0, 3000); var b = Mm(3000, 0, 3000);
            var pts = ScanCylinder(a, b, 50, Scenario.Up, 10, 3, rng);
            var r = QaEngine.Run(pts, new[] { Pipe(10, a, b, 50) }, Scenario.Settings());
            var e = Scenario.ResultOf(r, 10);
            Assert.Equal(QaStatus.Pass, e.Status, "status");
            Assert.True(e.P95Mm < 5, $"P95 {e.P95Mm:0.0} mm");
            Assert.True(e.PercentWithinTolerance > 99.9, "% within tolerance");
            Assert.True(e.Coverage > 0.4 && e.Coverage < 0.65, $"half-visible pipe coverage {e.Coverage:P0}");
            Assert.Equal(0, r.Clusters.Count, "no missing clusters");
        }

        [Test]
        public static void PipeOffset40mm_Fails()
        {
            var rng = new Rng(22);
            // The pipe was built at y = 0; the scan shows it at y = 40 mm and z + 0.
            var pts = ScanCylinder(Mm(0, 40, 3000), Mm(3000, 40, 3000), 50, Scenario.Up, 10, 2, rng);
            var r = QaEngine.Run(pts, new[] { Pipe(11, Mm(0, 0, 3000), Mm(3000, 0, 3000), 50) }, Scenario.Settings());
            var e = Scenario.ResultOf(r, 11);
            Assert.Equal(QaStatus.Fail, e.Status, "status");
            Assert.Near(40, e.MaxMm, 4, "max deviation");
            Assert.True(e.P95Mm > 30 && e.P95Mm <= 42, $"P95 {e.P95Mm:0.0} mm");
            Assert.True(e.PercentWithinTolerance < 60, $"% within {e.PercentWithinTolerance:0}");
            Assert.True(e.Coverage >= 0.25, "an offset pipe still counts as scanned (Fail, not Insufficient)");
        }

        [Test]
        public static void OccludedPipe_IsInsufficientNotPassOrFail()
        {
            var rng = new Rng(23);
            var visibleA = Mm(0, 0, 3000); var visibleB = Mm(3000, 0, 3000);
            var hiddenA = Mm(0, 600, 3000); var hiddenB = Mm(3000, 600, 3000);
            var patchA = Mm(0, 1200, 3000); var patchB = Mm(3000, 1200, 3000);
            var pts = new List<Vec3>();
            pts.AddRange(ScanCylinder(visibleA, visibleB, 50, Scenario.Up, 10, 2, rng));
            // Fully hidden behind another service: no points.
            // Patch pipe: only a 200 mm long, 60° strip seen through a gap, and offset 30 mm.
            pts.AddRange(ScanCylinder(Mm(0, 1230, 3000), Mm(3000, 1230, 3000), 50, Scenario.Up, 10, 2, rng, 1400, 1600, 60));
            var elements = new[] { Pipe(20, visibleA, visibleB, 50), Pipe(21, hiddenA, hiddenB, 50), Pipe(22, patchA, patchB, 50) };
            var r = QaEngine.Run(pts, elements, Scenario.Settings());
            Assert.Equal(QaStatus.Pass, Scenario.ResultOf(r, 20).Status, "visible pipe");
            var hidden = Scenario.ResultOf(r, 21);
            Assert.Equal(QaStatus.InsufficientScanData, hidden.Status, "hidden pipe");
            Assert.Equal(0, hidden.PointCount, "hidden pipe points");
            var patch = Scenario.ResultOf(r, 22);
            Assert.True(patch.PointCount >= Scenario.Settings().MinPoints, $"patch pipe has {patch.PointCount} points, enough by count alone");
            Assert.True(patch.P95Mm > 20, "patch points deviate (would be Fail on count alone)");
            Assert.Equal(QaStatus.InsufficientScanData, patch.Status, "patch pipe is Insufficient because coverage is too low");
        }

        [Test]
        public static void PipeExtendingOutsideSectionBox_CoverageCountsOnlyInside()
        {
            var rng = new Rng(24);
            var a = Mm(0, 0, 3000); var b = Mm(10000, 0, 3000);
            // Section box spans x = 0..2000 mm; the scan is clipped to it like Revit's point read.
            var box = new OrientedBox(RigidTransform.Identity, Mm(0, -500, 2500), Mm(2000, 500, 3500));
            var pts = ScanCylinder(a, b, 50, Scenario.Up, 10, 2, rng).Where(p => box.Contains(p)).ToList();
            var r = QaEngine.Run(pts, new[] { Pipe(30, a, b, 50) }, Scenario.Settings(), box);
            var e = Scenario.ResultOf(r, 30);
            Assert.Equal(QaStatus.Pass, e.Status, "status");
            Assert.True(e.Coverage > 0.4, $"coverage {e.Coverage:P0} measured inside the box only");
        }

        [Test]
        public static void ElementOutsideRegion_IsNotReported()
        {
            var rng = new Rng(25);
            var box = new OrientedBox(RigidTransform.Identity, Mm(0, -500, 2500), Mm(2000, 500, 3500));
            var inside = Pipe(40, Mm(0, 0, 3000), Mm(2000, 0, 3000), 50);
            var outside = Pipe(41, Mm(5000, 0, 3000), Mm(7000, 0, 3000), 50);
            var pts = ScanCylinder(Mm(0, 0, 3000), Mm(2000, 0, 3000), 50, Scenario.Up, 10, 2, rng);
            var r = QaEngine.Run(pts, new[] { inside, outside }, Scenario.Settings(), box);
            Assert.Equal(1, r.Elements.Count, "only the element in the region is listed");
            Assert.Equal(40L, r.Elements[0].ElementId, "listed element");
        }

        [Test]
        public static void ScatteredNoise_GivesNoMissingCluster()
        {
            var rng = new Rng(26);
            var a = Mm(0, 0, 3000); var b = Mm(3000, 0, 3000);
            var pts = ScanCylinder(a, b, 50, Scenario.Up, 10, 2, rng);
            pts.AddRange(ScatterNoise(new Aabb(Mm(0, -1500, 2000), Mm(3000, 1500, 4000)), 400, rng));
            var r = QaEngine.Run(pts, new[] { Pipe(50, a, b, 50) }, Scenario.Settings());
            Assert.Equal(0, r.Clusters.Count, "400 scattered points in 18 m³ form no cluster");
            Assert.True(r.PointClasses.Count(c => c == PointClass.Unexplained) > 300, "noise points are unexplained");
            Assert.Equal(QaStatus.Pass, Scenario.ResultOf(r, 50).Status, "pipe still passes");
        }

        [Test]
        public static void Settings_AreValidated()
        {
            Assert.Throws<ArgumentException>(() => new QaSettings { ToleranceMm = 0 }.Validate(), "zero tolerance");
            Assert.Throws<ArgumentException>(() => new QaSettings { ToleranceMm = 50, MissingThresholdMm = 40 }.Validate(), "threshold below tolerance");
            new QaSettings().Validate();
        }
    }

    internal static class MissingFittingTests
    {
        [Test]
        public static void MissingElbow_IsReportedAsClusterAtOpenEnds_AndPipesStillPass()
        {
            var (elements, pts, elbowPoints) = Scenario.MissingElbow(withWall: false);
            var r = QaEngine.Run(pts, elements, Scenario.Settings());
            Assert.Equal(QaStatus.Pass, Scenario.ResultOf(r, 1).Status, "pipe 1");
            Assert.Equal(QaStatus.Pass, Scenario.ResultOf(r, 2).Status, "pipe 2");
            Assert.True(Scenario.ResultOf(r, 1).P95Mm < 10, "pipe 1 P95 not inflated by elbow points");
            Assert.True(r.Clusters.Count >= 1, "a Suspected Missing cluster exists");
            var c = r.Clusters[0];
            var elbowCentre = Mm(2000 + 150 * Math.Cos(Math.PI / 4), 150 - 150 * Math.Sin(Math.PI / 4), 3000);
            Assert.True(c.Bounds.Inflate(F(10)).Contains(elbowCentre), "cluster box covers the elbow mid-point");
            Assert.True(c.PointCount >= 0.7 * elbowPoints, $"cluster has {c.PointCount} of {elbowPoints} elbow points");
            Assert.True(c.NearOpenEndOfElementId == 1 || c.NearOpenEndOfElementId == 2, "cluster is tied to an open pipe end");
            Assert.True(c.Note.Contains("possible missing fitting"), "note");
        }

        [Test]
        public static void MissingElbow_NaiveNearestSurfaceLosesPointsToAdjacentPipes()
        {
            // Documents why the axial end rule exists: without it the elbow's points within the missing
            // threshold of the pipe ends are attributed to the pipes.
            var (elements, pts, _) = Scenario.MissingElbow(withWall: false);
            var withRule = QaEngine.Run(pts, elements, Scenario.Settings());
            var s = Scenario.Settings();
            s.UseAxialEndRule = false;
            var naive = QaEngine.Run(pts, elements, s);
            int ruleCluster = withRule.Clusters.Sum(c => c.PointCount);
            int naiveCluster = naive.Clusters.Sum(c => c.PointCount);
            Assert.True(naiveCluster < ruleCluster * 0.85, $"naive cluster {naiveCluster} vs axial rule {ruleCluster}");
            Assert.True(Scenario.ResultOf(naive, 1).MaxMm > Scenario.ResultOf(withRule, 1).MaxMm + 20,
                "naive attribution inflates the pipe's max deviation");
        }

        [Test]
        public static void MissingElbowNextToWall_KnownLimitation_WallWithinThresholdAbsorbsPoints()
        {
            // Limitation: context geometry (wall) within the missing threshold explains part of the missing
            // fitting. The cluster is smaller but still reported here; with a wall closer than ~half the
            // fitting size the cluster can disappear. The README states this.
            var (noWallEls, noWallPts, _) = Scenario.MissingElbow(withWall: false);
            var (wallEls, wallPts, _) = Scenario.MissingElbow(withWall: true);
            var a = QaEngine.Run(noWallPts, noWallEls, Scenario.Settings());
            var b = QaEngine.Run(wallPts, wallEls, Scenario.Settings());
            int clusterNoWall = a.Clusters.Sum(c => c.PointCount);
            int clusterWall = b.Clusters.Sum(c => c.PointCount);
            Assert.True(clusterWall < clusterNoWall, $"wall absorbs elbow points ({clusterWall} < {clusterNoWall})");
            Assert.True(b.Clusters.Count >= 1, "cluster still reported with the wall 30 mm away");
            // The wall's own scan points are explained by the wall and never flagged.
            Assert.True(b.Clusters.All(c => c.Bounds.Max.X < F(2230)), "no cluster on the wall face");
        }

        [Test]
        public static void MissingElbow_SmallerThresholdKeepsMorePointsAwayFromWall()
        {
            var (els, pts, _) = Scenario.MissingElbow(withWall: true);
            var s100 = Scenario.Settings();
            var s50 = Scenario.Settings();
            s50.MissingThresholdMm = 50;
            int c100 = QaEngine.Run(pts, els, s100).Clusters.Sum(c => c.PointCount);
            int c50 = QaEngine.Run(pts, els, s50).Clusters.Sum(c => c.PointCount);
            Assert.True(c50 > c100, $"threshold 50 mm keeps {c50} points vs {c100} at 100 mm");
        }
    }

    internal static class TransformTests
    {
        [Test]
        public static void RotatedTranslatedCloud_AndRotatedSectionBox_GiveSameResultAsModelFrame()
        {
            var rng = new Rng(31);
            var a = Mm(0, 0, 3000); var b = Mm(3000, 0, 3000);
            var c0 = Mm(0, 400, 3000); var c1 = Mm(3000, 400, 3000);
            var model = new List<Vec3>();
            model.AddRange(ScanCylinder(a, b, 50, Scenario.Up, 10, 2, rng));
            model.AddRange(ScanCylinder(Mm(0, 435, 3000), Mm(3000, 435, 3000), 50, Scenario.Up, 10, 2, rng)); // pipe 61 offset 35 mm
            // Points inside the world AABB of the rotated box but outside the box itself.
            model.AddRange(ScanCylinder(Mm(2900, -900, 3000), Mm(2900, -700, 3000), 50, Scenario.Up, 10, 2, rng));

            var cloudToModel = RigidTransform.Rotation(new Vec3(0.2, 0.1, 1), 35 * Math.PI / 180, new Vec3(1234.5, -987.6, 12.3));
            var cloudLocal = model.Select(cloudToModel.Inverse().OfPoint).ToList();

            // Section box rotated 15° about Z around the middle of the pipes.
            var box = new OrientedBox(RigidTransform.RotationZ(15 * Math.PI / 180, Mm(1500, 200, 3000)), Mm(-1700, -700, -300), Mm(1700, 700, 300));
            var elements = new[] { Pipe(60, a, b, 50), Pipe(61, c0, c1, 50) };

            var viaCloud = PointPreparation.ToModelInsideBox(cloudLocal, cloudToModel, box);
            var direct = model.Where(p => box.Contains(p)).ToList();
            Assert.Equal(direct.Count, viaCloud.Count, "same points inside the rotated box");
            Assert.True(direct.Count < model.Count, "the rotated box clips points that its world AABB would keep");
            for (int i = 0; i < direct.Count; i++)
                Assert.Near(0, direct[i].DistanceTo(viaCloud[i]), 1e-9, "point position after transform");

            var r1 = QaEngine.Run(direct, elements, Scenario.Settings(), box);
            var r2 = QaEngine.Run(viaCloud, elements, Scenario.Settings(), box);
            foreach (var id in new long[] { 60, 61 })
            {
                var e1 = Scenario.ResultOf(r1, id);
                var e2 = Scenario.ResultOf(r2, id);
                Assert.Equal(e1.Status, e2.Status, "status " + id);
                Assert.Near(e1.P95Mm, e2.P95Mm, 1e-6, "P95 " + id);
                Assert.Equal(e1.PointCount, e2.PointCount, "points " + id);
            }
            Assert.Equal(QaStatus.Pass, Scenario.ResultOf(r2, 60).Status, "pipe 60");
            Assert.Equal(QaStatus.Fail, Scenario.ResultOf(r2, 61).Status, "pipe 61");
            Assert.Near(35, Scenario.ResultOf(r2, 61).P95Mm, 4, "pipe 61 deviation in mm");
        }

        [Test]
        public static void WrongTransform_WouldBeDetected()
        {
            // Guard: if the instance transform is not applied, the same scan no longer matches the model.
            var rng = new Rng(32);
            var a = Mm(0, 0, 3000); var b = Mm(3000, 0, 3000);
            var model = ScanCylinder(a, b, 50, Scenario.Up, 10, 2, rng);
            var cloudToModel = RigidTransform.Translation(Mm(0, 0, 500));
            var cloudLocal = model.Select(cloudToModel.Inverse().OfPoint).ToList();
            var r = QaEngine.Run(cloudLocal, new[] { Pipe(70, a, b, 50) }, Scenario.Settings());
            Assert.Equal(QaStatus.InsufficientScanData, Scenario.ResultOf(r, 70).Status, "untransformed points miss the pipe");
        }
    }

    internal static class PresentationTests
    {
        [Test]
        public static void Heatmap_ColoursFollowTolerance()
        {
            Assert.Equal(Heatmap.Green.ToString(), Heatmap.ForDeviation(0, 20).ToString(), "0 mm");
            Assert.Equal(Heatmap.Yellow.ToString(), Heatmap.ForDeviation(20, 20).ToString(), "at tolerance");
            Assert.Equal(Heatmap.Red.ToString(), Heatmap.ForDeviation(40, 20).ToString(), "2x tolerance");
            Assert.Equal(Heatmap.Red.ToString(), Heatmap.ForDeviation(400, 20).ToString(), "beyond");
            Assert.Equal(Heatmap.Missing.ToString(), Heatmap.ForDeviation(double.NaN, 20).ToString(), "unexplained");
            var legend = Heatmap.Legend(25);
            Assert.True(legend.Any(l => l.Label.StartsWith("25 mm")), "legend labels follow tolerance");
        }

        [Test]
        public static void ZoomTargets_FrameElementAndCluster()
        {
            var (elements, pts, _) = Scenario.MissingElbow(withWall: false);
            var r = QaEngine.Run(pts, elements, Scenario.Settings());
            var cluster = r.Clusters[0];
            var frame = ZoomTargets.ForCluster(cluster);
            Assert.True(frame.Contains(cluster.Centroid), "frame contains cluster centroid");
            Assert.True(frame.Contains(cluster.Bounds.Min) && frame.Contains(cluster.Bounds.Max), "frame contains cluster box");
            Assert.True(frame.Size.X >= cluster.Bounds.Size.X + 2 * F(300) - 1e-9, "at least 300 mm padding");
            Assert.True(frame.Size.X < F(2000), "frame stays local to the cluster (no zoom to the whole model)");

            var pipe = Scenario.ResultOf(r, 1);
            var ef = ZoomTargets.ForElement(pipe);
            Assert.True(ef.Contains(pipe.Bounds.Min) && ef.Contains(pipe.Bounds.Max), "element frame contains element");
        }

        [Test]
        public static void ResultFilter_AndCsv()
        {
            var rows = new[]
            {
                new ElementResult { ElementId = 1, Status = QaStatus.Pass, P95Mm = 3 },
                new ElementResult { ElementId = 2, Status = QaStatus.Fail, P95Mm = 30 },
                new ElementResult { ElementId = 3, Status = QaStatus.InsufficientScanData, P95Mm = double.NaN, Name = "a \"quoted\" name" },
                new ElementResult { ElementId = 4, Status = QaStatus.Fail, P95Mm = 60 },
            };
            Assert.Equal(3, ResultFilter.Apply(rows, StatusFilter.Problems).Count(), "problems");
            Assert.Equal(2, ResultFilter.Apply(rows, StatusFilter.Fail).Count(), "fail");
            Assert.Equal(1, ResultFilter.Apply(rows, StatusFilter.Insufficient).Count(), "insufficient");
            Assert.Equal("4,2,3,1", string.Join(",", ResultFilter.DefaultOrder(rows).Select(r => r.ElementId)), "order");
            var csv = CsvExport.Elements(rows);
            Assert.True(csv.Contains("\"a \"\"quoted\"\" name\""), "csv quoting");
            Assert.True(csv.Contains("\"Insufficient Scan Data\""), "status text");
        }
    }
}
