using System.Collections.Generic;
using System.Linq;
using ScanQa.Core.Geometry;
using ScanQa.Core.Qa;
using static ScanQa.Core.Tests.Synthetic;

namespace ScanQa.Core.Tests
{
    /// <summary>Architecture and structure elements: walls, slabs, columns and beams, some from a linked model.</summary>
    internal static class MultiDisciplineTests
    {
        private static QaElement Target(QaElement e, string discipline, string source = "Host")
        {
            e.Role = ElementRole.Target;
            e.Discipline = discipline;
            e.Source = source;
            if (source != "Host") e.LinkInstanceId = 777;
            return e;
        }

        [Test]
        public static void WallsSlabColumnsBeams_ClassifiedPerDiscipline()
        {
            var rng = new Rng(41);
            var els = new List<QaElement>();
            var pts = new List<Vec3>();

            // Wall 200 mm thick, face at y = 0, scanned from the room side (y < 0).
            els.Add(Target(Box(201, Mm(0, 0, 0), Mm(4000, 200, 3000), "Walls"), "Architecture"));
            pts.AddRange(ScanPlane(Mm(0, 0, 0), Vec3.UnitX, Vec3.UnitZ, 4000, 3000, 30, 2, rng)
);

            // Second wall modelled 35 mm off its real face (y = -2000 real, -2035 model).
            els.Add(Target(Box(202, Mm(0, -2235, 0), Mm(4000, -2035, 3000), "Walls"), "Architecture"));
            pts.AddRange(ScanPlane(Mm(0, -2000, 0), Vec3.UnitX, Vec3.UnitZ, 4000, 3000, 30, 2, rng));

            // Slab top at z = 0 from the structural link, scanned from above.
            els.Add(Target(Box(301, Mm(0, -2000, -250), Mm(4000, 0, 0), "Floors"), "Structure", "WP3004_STR.rvt"));
            pts.AddRange(ScanPlane(Mm(0, -2000, 0), Vec3.UnitX, Vec3.UnitY, 4000, 2000, 30, 2, rng));

            // Column 400x400 in the link, correct.
            els.Add(Target(Box(302, Mm(1800, -1200, 0), Mm(2200, -800, 3000), "Structural Columns"), "Structure", "WP3004_STR.rvt"));
            pts.AddRange(ScanPlane(Mm(1800, -1200, 0), Vec3.UnitX, Vec3.UnitZ, 400, 3000, 30, 2, rng));
            pts.AddRange(ScanPlane(Mm(1800, -1200, 0), Vec3.UnitY, Vec3.UnitZ, 400, 3000, 30, 2, rng));

            // A second column exists on site but not in the model.
            pts.AddRange(ScanPlane(Mm(3200, -1200, 0), Vec3.UnitX, Vec3.UnitZ, 400, 3000, 30, 2, rng));
            pts.AddRange(ScanPlane(Mm(3200, -1200, 0), Vec3.UnitY, Vec3.UnitZ, 400, 3000, 30, 2, rng));

            var r = QaEngine.Run(pts, els, new QaSettings { ToleranceMm = 20, MissingThresholdMm = 100 });
            var byId = r.Elements.ToDictionary(e => e.ElementId);
            Assert.Equal(QaStatus.Pass, byId[201].Status, "wall on the scan");
            Assert.Equal(QaStatus.Fail, byId[202].Status, "wall 35 mm off");
            Assert.Near(35, byId[202].P95Mm, 4, "wall deviation");
            Assert.Equal(QaStatus.Pass, byId[301].Status, "linked slab");
            Assert.Equal(QaStatus.Pass, byId[302].Status, "linked column");
            Assert.Equal("WP3004_STR.rvt", byId[302].Source, "source of linked element");
            Assert.Equal(777L, byId[302].LinkInstanceId ?? 0, "link instance id carried to the result");
            Assert.Equal(1, r.Clusters.Count, "the unmodelled column is one Suspected Missing cluster");
            Assert.True(r.Clusters[0].Bounds.Inflate(F(50)).Contains(Mm(3400, -1200, 1500)), "cluster at the unmodelled column");
            Assert.Equal(2, ResultFilter.ByDiscipline(r.Elements, "Structure").Count(), "discipline filter");
            Assert.True(CsvExport.Elements(r.Elements).Contains("\"WP3004_STR.rvt\",\"Structure\",302"), "csv carries model and discipline");
        }
    }
}
