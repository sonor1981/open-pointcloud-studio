using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using ScanQa.Core.Geometry;
using ScanQa.Core.Qa;

namespace ScanQa.Revit
{
    /// <summary>Reads element surfaces from the Revit model as triangles in model coordinates.</summary>
    internal static class ModelGeometry
    {
        /// <summary>Face.Triangulate level of detail: 1 is the finest Revit offers.</summary>
        private const double TriangulationDetail = 1.0;

        private static readonly Options GeometryOptions = new Options
        {
            ComputeReferences = false,
            DetailLevel = ViewDetailLevel.Fine,
            IncludeNonVisibleObjects = false,
        };

        public static void AddTriangles(Element element, Transform toModel, List<Triangle> into)
        {
            GeometryElement ge;
            try
            {
                ge = element.get_Geometry(GeometryOptions);
            }
            catch (Exception)
            {
                return;
            }
            if (ge != null) Collect(ge, toModel, into);
        }

        private static void Collect(GeometryElement ge, Transform toModel, List<Triangle> into)
        {
            foreach (GeometryObject go in ge)
            {
                switch (go)
                {
                    case Solid solid:
                        if (solid.Faces.Size == 0) break;
                        foreach (Face face in solid.Faces)
                        {
                            Mesh mesh;
                            try { mesh = face.Triangulate(TriangulationDetail); }
                            catch (Exception) { continue; }
                            AddMesh(mesh, toModel, into);
                        }
                        break;
                    case Mesh mesh:
                        AddMesh(mesh, toModel, into);
                        break;
                    case GeometryInstance gi:
                        // GetInstanceGeometry() is already in the coordinates of the element's document.
                        var inner = gi.GetInstanceGeometry();
                        if (inner != null) Collect(inner, toModel, into);
                        break;
                }
            }
        }

        private static void AddMesh(Mesh mesh, Transform toModel, List<Triangle> into)
        {
            if (mesh == null) return;
            for (int i = 0; i < mesh.NumTriangles; i++)
            {
                MeshTriangle t = mesh.get_Triangle(i);
                XYZ a = t.get_Vertex(0), b = t.get_Vertex(1), c = t.get_Vertex(2);
                if (toModel != null)
                {
                    a = toModel.OfPoint(a);
                    b = toModel.OfPoint(b);
                    c = toModel.OfPoint(c);
                }
                into.Add(new Triangle(RevitConvert.ToVec(a), RevitConvert.ToVec(b), RevitConvert.ToVec(c)));
            }
        }

        /// <summary>Straight centre line for pipes, ducts, conduits and cable trays.</summary>
        public static LinearAxis AxisOf(Element element)
        {
            if (!(element is MEPCurve)) return null;
            if (element.Location is LocationCurve lc && lc.Curve is Line line && line.IsBound)
                return new LinearAxis(RevitConvert.ToVec(line.GetEndPoint(0)), RevitConvert.ToVec(line.GetEndPoint(1)));
            return null;
        }

        /// <summary>Origins of physical end connectors that are not connected to anything.</summary>
        public static IEnumerable<Vec3> OpenEndsOf(Element element)
        {
            ConnectorManager cm = null;
            if (element is MEPCurve curve) cm = curve.ConnectorManager;
            else if (element is FamilyInstance fi) cm = fi.MEPModel?.ConnectorManager;
            if (cm == null) yield break;
            foreach (Connector c in cm.Connectors)
            {
                XYZ origin = null;
                try
                {
                    if (c.ConnectorType == ConnectorType.End && !c.IsConnected) origin = c.Origin;
                }
                catch (Exception)
                {
                    // Some logical connectors have no origin.
                }
                if (origin != null) yield return RevitConvert.ToVec(origin);
            }
        }

        /// <summary>Insulation hosted by a pipe or duct. Its outer face is what the scanner sees.</summary>
        public static ICollection<ElementId> InsulationOf(Document doc, Element element)
        {
            if (!(element is Pipe) && !(element is Duct) && !(element is FamilyInstance)) return new List<ElementId>();
            try
            {
                return InsulationLiningBase.GetInsulationIds(doc, element.Id);
            }
            catch (Exception)
            {
                return new List<ElementId>();
            }
        }

        public static QaElement ToQaElement(Element element, ElementRole role, Transform toModel)
        {
            var qa = new QaElement
            {
                Id = RevitConvert.ToLong(element.Id),
                Category = element.Category?.Name ?? "",
                Name = SafeName(element),
                Role = role,
            };
            AddTriangles(element, toModel, qa.Triangles);
            if (role == ElementRole.Target && toModel == null)
            {
                qa.Axis = AxisOf(element);
                qa.OpenEnds.AddRange(OpenEndsOf(element));
            }
            return qa;
        }

        private static string SafeName(Element e)
        {
            try
            {
                var typeId = e.GetTypeId();
                var type = typeId == ElementId.InvalidElementId ? null : e.Document.GetElement(typeId);
                return type != null ? $"{type.Name}" : e.Name;
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
