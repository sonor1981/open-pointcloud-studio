using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ScanQa.Core.Geometry;
using Units = ScanQa.Core.Geometry.Units;
using ScanQa.Core.Qa;

namespace ScanQa.Revit
{
    internal sealed class CategoryOption
    {
        public BuiltInCategory Category { get; }
        public string Label { get; }
        public bool DefaultOn { get; }

        public CategoryOption(BuiltInCategory c, string label, bool on)
        {
            Category = c;
            Label = label;
            DefaultOn = on;
        }

        public override string ToString() => Label;

        public static readonly CategoryOption[] All =
        {
            new CategoryOption(BuiltInCategory.OST_PipeCurves, "Pipes", true),
            new CategoryOption(BuiltInCategory.OST_PipeFitting, "Pipe Fittings", true),
            new CategoryOption(BuiltInCategory.OST_DuctCurves, "Ducts", true),
            new CategoryOption(BuiltInCategory.OST_DuctFitting, "Duct Fittings", true),
            new CategoryOption(BuiltInCategory.OST_PipeAccessory, "Pipe Accessories", false),
            new CategoryOption(BuiltInCategory.OST_DuctAccessory, "Duct Accessories", false),
            new CategoryOption(BuiltInCategory.OST_FlexPipeCurves, "Flex Pipes", false),
            new CategoryOption(BuiltInCategory.OST_FlexDuctCurves, "Flex Ducts", false),
            new CategoryOption(BuiltInCategory.OST_Conduit, "Conduits", false),
            new CategoryOption(BuiltInCategory.OST_ConduitFitting, "Conduit Fittings", false),
            new CategoryOption(BuiltInCategory.OST_CableTray, "Cable Trays", false),
            new CategoryOption(BuiltInCategory.OST_CableTrayFitting, "Cable Tray Fittings", false),
            new CategoryOption(BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment", false),
        };
    }

    internal sealed class CloudOption
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public override string ToString() => Name;
    }

    internal sealed class QaRequest
    {
        /// <summary>Empty = every point cloud visible in the view.</summary>
        public List<long> CloudIds { get; } = new List<long>();
        public List<BuiltInCategory> Categories { get; } = new List<BuiltInCategory>();
        public bool SelectionOnly { get; set; }
        public bool InsulationAsSurface { get; set; } = true;
        public bool LinkedModelsAsContext { get; set; } = true;
        public double SpacingMm { get; set; } = 5;
        public int MaxPoints { get; set; } = 2_000_000;
        public QaSettings Settings { get; set; } = new QaSettings();
    }

    internal sealed class QaOutcome
    {
        public QaResult Result { get; set; }
        public long ViewId { get; set; }
        public string ViewName { get; set; } = "";
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>Collects the inputs from the active 3D view and runs the QA engine. Call only in a Revit API context.</summary>
    internal static class QaSession
    {
        public static View3D ActiveCheckView(UIApplication app, out string problem)
        {
            problem = null;
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) { problem = "Open a model first."; return null; }
            if (!(uidoc.ActiveView is View3D v) || v.IsTemplate) { problem = "Make a 3D view active."; return null; }
            if (!v.IsSectionBoxActive) { problem = "Turn on the Section Box in the 3D view and fit it around the area to check."; return null; }
            return v;
        }

        public static List<CloudOption> VisibleClouds(UIApplication app)
        {
            var v = ActiveCheckView(app, out _) ?? app.ActiveUIDocument?.ActiveView as View3D;
            if (v == null) return new List<CloudOption>();
            return new FilteredElementCollector(v.Document, v.Id)
                .OfClass(typeof(PointCloudInstance))
                .Cast<PointCloudInstance>()
                .Select(c => new CloudOption { Id = RevitConvert.ToLong(c.Id), Name = c.Name })
                .ToList();
        }

        public static QaOutcome Run(UIApplication app, QaRequest req)
        {
            var sw = Stopwatch.StartNew();
            var view = ActiveCheckView(app, out string problem);
            if (view == null) throw new InvalidOperationException(problem);
            var doc = view.Document;
            var uidoc = app.ActiveUIDocument;
            req.Settings.Validate();
            if (req.Categories.Count == 0) throw new InvalidOperationException("Choose at least one category to check.");

            var outcome = new QaOutcome { ViewId = RevitConvert.ToLong(view.Id), ViewName = view.Name };
            var box = RevitConvert.ToOrientedBox(view.GetSectionBox());
            double threshold = Units.MmToFeet(req.Settings.MissingThresholdMm);
            var region = box.ModelAabb().Inflate(threshold);
            var outline = new Outline(RevitConvert.ToXyz(region.Min), RevitConvert.ToXyz(region.Max));

            // 1. Points.
            var clouds = new FilteredElementCollector(doc, view.Id).OfClass(typeof(PointCloudInstance)).Cast<PointCloudInstance>()
                .Where(c => req.CloudIds.Count == 0 || req.CloudIds.Contains(RevitConvert.ToLong(c.Id)))
                .ToList();
            if (clouds.Count == 0) throw new InvalidOperationException("No linked point cloud is visible in this 3D view.");
            var points = new List<Vec3>();
            int budget = Math.Max(1000, req.MaxPoints / clouds.Count);
            foreach (var cloud in clouds)
            {
                var read = PointCloudReader.Read(cloud, box, req.SpacingMm, budget);
                points.AddRange(read.ModelPoints);
                outcome.Notes.AddRange(read.Notes);
            }
            long tPoints = sw.ElapsedMilliseconds;

            // 2. QA targets: chosen categories, visible in the view, bounding box touching the region.
            var bbFilter = new BoundingBoxIntersectsFilter(outline);
            var targets = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(req.Categories))
                .WherePasses(bbFilter)
                .ToElements()
                .ToList();
            if (req.SelectionOnly)
            {
                var sel = new HashSet<long>(uidoc.Selection.GetElementIds().Select(RevitConvert.ToLong));
                targets = targets.Where(e => sel.Contains(RevitConvert.ToLong(e.Id))).ToList();
                if (targets.Count == 0) throw new InvalidOperationException("None of the selected elements is in the chosen categories inside the section box.");
            }
            var targetIds = new HashSet<long>(targets.Select(e => RevitConvert.ToLong(e.Id)));
            var elements = new List<QaElement>();
            var insulationIds = new HashSet<long>();
            foreach (var e in targets)
            {
                var qa = ModelGeometry.ToQaElement(e, ElementRole.Target, null);
                if (req.InsulationAsSurface)
                {
                    foreach (var insId in ModelGeometry.InsulationOf(doc, e))
                    {
                        var ins = doc.GetElement(insId);
                        if (ins == null) continue;
                        insulationIds.Add(RevitConvert.ToLong(insId));
                        ModelGeometry.AddTriangles(ins, null, qa.Triangles);
                    }
                }
                elements.Add(qa);
            }

            // 3. Context: every other model element in the region, so walls, slabs and other services do
            //    not show up as Suspected Missing.
            var context = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .WherePasses(bbFilter)
                .ToElements();
            int contextCount = 0;
            foreach (var e in context)
            {
                long id = RevitConvert.ToLong(e.Id);
                if (targetIds.Contains(id) || insulationIds.Contains(id)) continue;
                if (!IsContextCandidate(e)) continue;
                var qa = ModelGeometry.ToQaElement(e, ElementRole.Context, null);
                if (qa.Triangles.Count == 0) continue;
                elements.Add(qa);
                contextCount++;
            }

            int linkedCount = 0;
            if (req.LinkedModelsAsContext)
            {
                foreach (var link in new FilteredElementCollector(doc, view.Id).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                {
                    var linkDoc = link.GetLinkDocument();
                    if (linkDoc == null) continue;
                    var linkToModel = link.GetTotalTransform();
                    var modelToLink = RevitConvert.ToRigid(linkToModel.Inverse);
                    var linkBox = Aabb.Empty;
                    foreach (var c in box.ModelCorners()) linkBox = linkBox.Include(modelToLink.OfPoint(c));
                    linkBox = linkBox.Inflate(threshold);
                    var linkFilter = new BoundingBoxIntersectsFilter(new Outline(RevitConvert.ToXyz(linkBox.Min), RevitConvert.ToXyz(linkBox.Max)));
                    foreach (var e in new FilteredElementCollector(linkDoc).WhereElementIsNotElementType().WherePasses(linkFilter))
                    {
                        if (!IsContextCandidate(e)) continue;
                        var qa = ModelGeometry.ToQaElement(e, ElementRole.Context, linkToModel);
                        if (qa.Triangles.Count == 0) continue;
                        qa.Id = -1; // not selectable in the host model
                        elements.Add(qa);
                        linkedCount++;
                    }
                }
            }
            long tGeometry = sw.ElapsedMilliseconds;

            // 4. QA.
            outcome.Result = QaEngine.Run(points, elements, req.Settings, box);
            long tQa = sw.ElapsedMilliseconds;

            outcome.Notes.Add($"{targets.Count} QA elements, {contextCount} context elements" +
                              (req.LinkedModelsAsContext ? $", {linkedCount} linked context elements" : "") +
                              $", {points.Count:N0} scan points.");
            outcome.Notes.Add($"Time: points {tPoints / 1000.0:0.0} s, geometry {(tGeometry - tPoints) / 1000.0:0.0} s, QA {(tQa - tGeometry) / 1000.0:0.0} s.");
            return outcome;
        }

        private static readonly HashSet<BuiltInCategory> NonPhysical = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_Rooms, BuiltInCategory.OST_MEPSpaces, BuiltInCategory.OST_Areas,
            BuiltInCategory.OST_HVAC_Zones, BuiltInCategory.OST_Mass, BuiltInCategory.OST_PipeInsulations,
            BuiltInCategory.OST_DuctInsulations, BuiltInCategory.OST_DuctLinings,
        };

        /// <summary>
        /// Physical model geometry only. Rooms, spaces and zones have volume solids that would "explain" every
        /// scan point inside them; insulation is handled with its host.
        /// </summary>
        private static bool IsContextCandidate(Element e)
        {
            if (e is PointCloudInstance || e is RevitLinkInstance || e is SpatialElement) return false;
            var cat = e.Category;
            if (cat == null || cat.CategoryType != CategoryType.Model) return false;
            return !NonPhysical.Contains((BuiltInCategory)RevitConvert.ToLong(cat.Id));
        }

        /// <summary>Selects an element and frames it in the checked view.</summary>
        public static void ZoomTo(UIApplication app, long viewId, Aabb frame, long? selectId)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            if (selectId.HasValue && selectId.Value > 0)
                uidoc.Selection.SetElementIds(new List<ElementId> { RevitConvert.ToId(selectId.Value) });
            var uiView = uidoc.GetOpenUIViews().FirstOrDefault(v => RevitConvert.ToLong(v.ViewId) == viewId);
            if (uiView == null) throw new InvalidOperationException("The checked 3D view is no longer open.");
            if (RevitConvert.ToLong(uidoc.ActiveView.Id) != viewId)
                uidoc.ActiveView = uidoc.Document.GetElement(RevitConvert.ToId(viewId)) as View;
            uiView.ZoomAndCenterRectangle(RevitConvert.ToXyz(frame.Min), RevitConvert.ToXyz(frame.Max));
        }
    }
}
