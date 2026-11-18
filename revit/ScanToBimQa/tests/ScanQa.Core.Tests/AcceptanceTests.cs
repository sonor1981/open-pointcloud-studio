using System;
using System.Collections.Generic;
using System.Linq;
using ScanQa.Core.Geometry;
using ScanQa.Core.Qa;
using static ScanQa.Core.Tests.Synthetic;

namespace ScanQa.Core.Tests
{
    /// <summary>
    /// The minimum acceptance set in one model and one scan: a pipe within tolerance, a pipe offset on purpose,
    /// a missing fitting, an occluded pipe, and a point cloud with a rotated and translated instance transform,
    /// read through a rotated section box. Checks classification and the zoom frame of every finding.
    /// </summary>
    internal static class AcceptanceTests
    {
        [Test]
        public static void AcceptanceSuite_AllRequiredCasesInOneRotatedCloud()
        {
            var rng = new Rng(99);
            var up = Vec3.UnitZ;
            var elements = new List<QaElement>();
            var model = new List<Vec3>();

            // 101: within tolerance.
            elements.Add(Pipe(101, Mm(0, 0, 3000), Mm(3000, 0, 3000), 50));
            model.AddRange(ScanCylinder(Mm(0, 0, 3000), Mm(3000, 0, 3000), 50, up, 10, 3, rng));

            // 102: modelled 45 mm too low.
            elements.Add(Pipe(102, Mm(0, 400, 2955), Mm(3000, 400, 2955), 50));
            model.AddRange(ScanCylinder(Mm(0, 400, 3000), Mm(3000, 400, 3000), 50, up, 10, 2, rng));

            // 103 + 104 with a missing elbow between them.
            var p103 = Pipe(103, Mm(0, 900, 3000), Mm(2000, 900, 3000), 50);
            p103.OpenEnds.Add(Mm(2000, 900, 3000));
            var p104 = Pipe(104, Mm(2150, 1050, 3000), Mm(2150, 2500, 3000), 50);
            p104.OpenEnds.Add(Mm(2150, 1050, 3000));
            elements.Add(p103);
            elements.Add(p104);
            model.AddRange(ScanCylinder(Mm(0, 900, 3000), Mm(2000, 900, 3000), 50, up, 10, 2, rng));
            model.AddRange(ScanCylinder(Mm(2150, 1050, 3000), Mm(2150, 2500, 3000), 50, up, 10, 2, rng));
            var elbow = ScanElbow(Mm(2000, 1050, 3000), new Vec3(0, -1, 0), new Vec3(1, 0, 0), 150, 50, 10, 2, rng);
            model.AddRange(elbow);

            // 105: occluded by a duct above it — only a 150 mm sliver is visible.
            elements.Add(Pipe(105, Mm(400, 2000, 2700), Mm(1900, 2000, 2700), 40));
            model.AddRange(ScanCylinder(Mm(400, 2000, 2700), Mm(1900, 2000, 2700), 40, up, 10, 2, rng, 700, 850, 50));

            // Slab below as context; its scan must not be flagged.
            elements.Add(Box(900, Mm(-500, -500, 2300), Mm(3500, 3000, 2400), "Floors"));
            model.AddRange(ScanPlane(Mm(-500, -500, 2400), Vec3.UnitX, Vec3.UnitY, 4000, 3500, 50, 2, rng));

            // Cloud stored in its own frame: rotated 20° about a tilted axis and moved far from the origin.
            var cloudToModel = RigidTransform.Rotation(new Vec3(0.1, -0.2, 1), 20 * Math.PI / 180, new Vec3(-3456.7, 2345.6, 98.7));
            var cloudLocal = model.Select(cloudToModel.Inverse().OfPoint).ToList();

            // Section box rotated 10° about Z.
            var box = new OrientedBox(RigidTransform.RotationZ(10 * Math.PI / 180, Mm(1500, 1300, 2800)), Mm(-2200, -2000, -600), Mm(2200, 2000, 600));

            var points = PointPreparation.ToModelInsideBox(cloudLocal, cloudToModel, box);
            var r = QaEngine.Run(points, elements, new QaSettings { ToleranceMm = 20, MissingThresholdMm = 100 }, box);

            var byId = r.Elements.ToDictionary(e => e.ElementId);
            Assert.Equal(QaStatus.Pass, byId[101].Status, "101 within tolerance");
            Assert.Equal(QaStatus.Fail, byId[102].Status, "102 offset");
            Assert.Near(45, byId[102].P95Mm, 5, "102 P95");
            Assert.Equal(QaStatus.Pass, byId[103].Status, "103 next to missing elbow");
            Assert.Equal(QaStatus.Pass, byId[104].Status, "104 next to missing elbow");
            Assert.Equal(QaStatus.InsufficientScanData, byId[105].Status, "105 occluded");
            Assert.True(!byId.ContainsKey(900), "context slab gets no status");

            Assert.Equal(1, r.Clusters.Count, "exactly one Suspected Missing cluster (the elbow; slab and pipes explained)");
            var c = r.Clusters[0];
            Assert.True(c.NearOpenEndOfElementId == 103 || c.NearOpenEndOfElementId == 104, "cluster tied to open end");
            Assert.True(c.PointCount >= 0.7 * elbow.Count, $"cluster has {c.PointCount} of {elbow.Count} elbow points");

            // Zoom frames: each finding is framed locally, not the whole region.
            var elbowMid = Mm(2000 + 150 * Math.Cos(Math.PI / 4), 1050 - 150 * Math.Sin(Math.PI / 4), 3000);
            var cf = ZoomTargets.ForCluster(c);
            Assert.True(cf.Contains(elbowMid), "cluster zoom frame contains the elbow");
            Assert.True(cf.Size.X < F(1500) && cf.Size.Y < F(1500), "cluster zoom frame is local");
            var ff = ZoomTargets.ForElement(byId[102]);
            Assert.True(ff.Contains(Mm(1500, 400, 2955)), "element zoom frame contains the failed pipe");
            var problems = ResultFilter.Apply(r.Elements, StatusFilter.Problems).Select(e => e.ElementId).OrderBy(x => x).ToArray();
            Assert.Equal("102,105", string.Join(",", problems), "problem filter lists the Fail and the Insufficient element");
        }
    }
}
