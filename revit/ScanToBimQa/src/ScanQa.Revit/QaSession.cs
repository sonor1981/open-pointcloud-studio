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
    internal static class Discipline
    {
        public const string Architecture = "Architecture";
        public const string Structure = "Structure";
        public const string Mep = "MEP";
        public static readonly string[] All = { Architecture, Structure, Mep };
    }

    internal sealed class CategoryOption
    {
        public BuiltInCategory Category { get; }
        public string Label { get; }
        public string Discipline { get; }
        public bool DefaultOn { get; }

        public CategoryOption(BuiltInCategory c, string label, string discipline, bool on)
        {
            Category = c;
            Label = label;
            Discipline = discipline;
            DefaultOn = on;
        }

        public override string ToString() => $"{Label}  [{(Discipline == ScanQa.Revit.Discipline.Mep ? "MEP" : Discipline.Substring(0, 4))}]";

        public static readonly CategoryOption[] All =
        {
            new CategoryOption(BuiltInCategory.OST_Walls, "Walls", ScanQa.Revit.Discipline.Architecture, true),
            new CategoryOption(BuiltInCategory.OST_Floors, "Floors", ScanQa.Revit.Discipline.Architecture, true),
            new CategoryOption(BuiltInCategory.OST_Ceilings, "Ceilings", ScanQa.Revit.Discipline.Architecture, true),
            new CategoryOption(BuiltInCategory.OST_Roofs, "Roofs", ScanQa.Revit.Discipline.Architecture, false),
            new CategoryOption(BuiltInCategory.OST_Doors, "Doors", ScanQa.Revit.Discipline.Architecture, true),
            new CategoryOption(BuiltInCategory.OST_Windows, "Windows", ScanQa.Revit.Discipline.Architecture, true),
            new CategoryOption(BuiltInCategory.OST_Stairs, "Stairs", ScanQa.Revit.Discipline.Architecture, true),
            new CategoryOption(BuiltInCategory.OST_StairsRailing, "Railings", ScanQa.Revit.Discipline.Architecture, false),
            new CategoryOption(BuiltInCategory.OST_CurtainWallPanels, "Curtain Panels", ScanQa.Revit.Discipline.Architecture, false),
            new CategoryOption(BuiltInCategory.OST_CurtainWallMullions, "Curtain Wall Mullions", ScanQa.Revit.Discipline.Architecture, false),
            new CategoryOption(BuiltInCategory.OST_Columns, "Columns (architectural)", ScanQa.Revit.Discipline.Architecture, false),
            new CategoryOption(BuiltInCategory.OST_GenericModel, "Generic Models", ScanQa.Revit.Discipline.Architecture, false),
            new CategoryOption(BuiltInCategory.OST_StructuralColumns, "Structural Columns", ScanQa.Revit.Discipline.Structure, true),
            new CategoryOption(BuiltInCategory.OST_StructuralFraming, "Structural Framing", ScanQa.Revit.Discipline.Structure, true),
            new CategoryOption(BuiltInCategory.OST_StructuralFoundation, "Structural Foundations", ScanQa.Revit.Discipline.Structure, false),
            new CategoryOption(BuiltInCategory.OST_PipeCurves, "Pipes", ScanQa.Revit.Discipline.Mep, true),
            new CategoryOption(BuiltInCategory.OST_PipeFitting, "Pipe Fittings", ScanQa.Revit.Discipline.Mep, true),
            new CategoryOption(BuiltInCategory.OST_DuctCurves, "Ducts", ScanQa.Revit.Discipline.Mep, true),
            new CategoryOption(BuiltInCategory.OST_DuctFitting, "Duct Fittings", ScanQa.Revit.Discipline.Mep, true),
            new CategoryOption(BuiltInCategory.OST_PipeAccessory, "Pipe Accessories", ScanQa.Revit.Discipline.Mep, false),
            new CategoryOption(BuiltInCategory.OST_DuctAccessory, "Duct Accessories", ScanQa.Revit.Discipline.Mep, false),
            new CategoryOption(BuiltInCategory.OST_FlexPipeCurves, "Flex Pipes", ScanQa.Revit.Discipline.Mep, false),
            new CategoryOption(BuiltInCategory.OST_FlexDuctCurves, "Flex Ducts", ScanQa.Revit.Discipline.Mep, false),
            new CategoryOption(BuiltInCategory.OST_Conduit, "Conduits", ScanQa.Revit.Discipline.Mep, false),
            new CategoryOption(BuiltInCategory.OST_ConduitFitting, "Conduit Fittings", ScanQa.Revit.Discipline.Mep, false),
            new CategoryOption(BuiltInCategory.OST_CableTray, "Cable Trays", ScanQa.Revit.Discipline.Mep, true),
            new CategoryOption(BuiltInCategory.OST_CableTrayFitting, "Cable Tray Fittings", ScanQa.Revit.Discipline.Mep, true),
            new CategoryOption(BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment", ScanQa.Revit.Discipline.Mep, false),
            new CategoryOption(BuiltInCategory.OST_PlumbingFixtures, "Plumbing Fixtures", ScanQa.Revit.Discipline.Mep, false),
            new CategoryOption(BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment", ScanQa.Revit.Discipline.Mep, false),
        };

        /// <summary>Discipline of an element: the category's, except walls and floors flagged structural.</summary>
        public static string DisciplineOf(Element e)
        {
            var cat = e.Category == null ? BuiltInCategory.INVALID : (BuiltInCategory)RevitConvert.ToLong(e.Category.Id);
            var option = All.FirstOrDefault(o => o.Category == cat);
            if (cat == BuiltInCategory.OST_Walls || cat == BuiltInCategory.OST_Floors)
            {
                var p = e.get_Parameter(cat == BuiltInCategory.OST_Walls
                    ? BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT
                    : BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
                if (p != null && p.HasValue && p.AsInteger() == 1) return ScanQa.Revit.Discipline.Structure;
            }
            return option?.Discipline ?? "";
        }
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
        /// <summary>Elements of the chosen categories in linked models are checked too (else they are context only).</summary>
        public bool CheckLinkedElements { get; set; } = true;
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
            return CloudsIn(v)
                .Select(c => new CloudOption { Id = RevitConvert.ToLong(c.Id), Name = c.Name })
                .ToList();
        }

        /// <summary>
        /// Point clouds shown in the view. A cloud hidden only with Temporary Hide/Isolate still counts, so the
        /// user can hide the scan colours to read the heatmap and still run the check.
        /// </summary>
        public static List<PointCloudInstance> CloudsIn(View3D v)
        {
            var visible = new FilteredElementCollector(v.Document, v.Id).OfClass(typeof(PointCloudInstance))
                .Cast<PointCloudInstance>().ToList();
            if (visible.Count > 0 || !v.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate)) return visible;
            return new FilteredElementCollector(v.Document).OfClass(typeof(PointCloudInstance))
                .Cast<PointCloudInstance>()
                .Where(c => !c.IsHidden(v))
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
            var clouds = CloudsIn(view)
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
                if (targets.Count == 0 && !req.CheckLinkedElements) throw new InvalidOperationException("None of the selected elements is in the chosen categories inside the section box.");
            }
            var targetIds = new HashSet<long>(targets.Select(e => RevitConvert.ToLong(e.Id)));
            var elements = new List<QaElement>();
            var insulationIds = new HashSet<long>();
            foreach (var e in targets)
            {
                var qa = ModelGeometry.ToQaElement(e, ElementRole.Target, null);
                qa.Discipline = CategoryOption.DisciplineOf(e);
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

            int linkedCount = 0, linkedTargets = 0;
            if (req.LinkedModelsAsContext || req.CheckLinkedElements)
            {
                var categorySet = new HashSet<long>(req.Categories.Select(c => (long)c));
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
                    string source = linkDoc.Title;
                    long linkId = RevitConvert.ToLong(link.Id);
                    foreach (var e in new FilteredElementCollector(linkDoc).WhereElementIsNotElementType().WherePasses(linkFilter))
                    {
                        if (!IsContextCandidate(e)) continue;
                        bool isTarget = req.CheckLinkedElements && categorySet.Contains(RevitConvert.ToLong(e.Category.Id));
                        if (!isTarget && !req.LinkedModelsAsContext) continue;
                        var qa = ModelGeometry.ToQaElement(e, isTarget ? ElementRole.Target : ElementRole.Context, linkToModel);
                        if (isTarget)
                        {
                            qa.Discipline = CategoryOption.DisciplineOf(e);
                            qa.Source = source;
                            qa.LinkInstanceId = linkId;
                            if (e is MEPCurve)
                            {
                                var ax = ModelGeometry.AxisOf(e);
                                if (ax != null)
                                {
                                    var t = RevitConvert.ToRigid(linkToModel);
                                    qa.Axis = new LinearAxis(t.OfPoint(ax.Start), t.OfPoint(ax.End));
                                }
                            }
                            var linkRigid = RevitConvert.ToRigid(linkToModel);
                            qa.OpenEnds.AddRange(ModelGeometry.OpenEndsOf(e).Select(linkRigid.OfPoint));
                            if (req.InsulationAsSurface)
                                foreach (var insId in ModelGeometry.InsulationOf(linkDoc, e))
                                {
                                    var ins = linkDoc.GetElement(insId);
                                    if (ins != null) ModelGeometry.AddTriangles(ins, linkToModel, qa.Triangles);
                                }
                        }
                        else
                        {
                            qa.Id = -1; // context from a link is never listed
                        }
                        if (qa.Triangles.Count == 0) continue;
                        elements.Add(qa);
                        if (isTarget) linkedTargets++; else linkedCount++;
                    }
                }
            }
            long tGeometry = sw.ElapsedMilliseconds;

            // 4. QA.
            outcome.Result = QaEngine.Run(points, elements, req.Settings, box);
            long tQa = sw.ElapsedMilliseconds;

            outcome.Notes.Add($"{targets.Count} QA elements in the host, {linkedTargets} in linked models, {contextCount} context elements" +
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

        /// <summary>Selects an element (host or linked) and frames the given box in the checked view.</summary>
        public static void ZoomTo(UIApplication app, long viewId, Aabb frame, long? selectId, long? linkInstanceId = null)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            Select(uidoc, selectId, linkInstanceId);
            var uiView = uidoc.GetOpenUIViews().FirstOrDefault(v => RevitConvert.ToLong(v.ViewId) == viewId);
            if (uiView == null) throw new InvalidOperationException("The checked 3D view is no longer open.");
            if (RevitConvert.ToLong(uidoc.ActiveView.Id) != viewId)
                uidoc.ActiveView = uidoc.Document.GetElement(RevitConvert.ToId(viewId)) as View;
            uiView.ZoomAndCenterRectangle(RevitConvert.ToXyz(frame.Min), RevitConvert.ToXyz(frame.Max));
        }

        public static void Select(UIDocument uidoc, long? selectId, long? linkInstanceId)
        {
            if (!selectId.HasValue || selectId.Value <= 0) return;
            if (!linkInstanceId.HasValue)
            {
                uidoc.Selection.SetElementIds(new List<ElementId> { RevitConvert.ToId(selectId.Value) });
                return;
            }
            var link = uidoc.Document.GetElement(RevitConvert.ToId(linkInstanceId.Value)) as RevitLinkInstance;
            var linked = link?.GetLinkDocument()?.GetElement(RevitConvert.ToId(selectId.Value));
            if (linked == null) return;
            uidoc.Selection.SetReferences(new List<Reference> { new Reference(linked).CreateLinkReference(link) });
        }
    }
}
