using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit2GoldSrc.Core;
using CoreFrame = Revit2GoldSrc.Core.Frame;
namespace Revit2GoldSrc.Revit
{
    public sealed class ExtractionResult
    {
        public List<BimElement> Elements { get; } = new List<BimElement>();
        public ExportReport Report { get; } = new ExportReport();
    }
    public static class RevitGeometryExtractor
    {
        public static ExtractionResult Read(UIDocument ui, bool selected, IReadOnlyCollection<BimCategory> categories, double tolerance, Action<string>? progress = null, bool readMaterials = false)
        {
            IEnumerable<Element> source;
            if (selected) source = ui.Selection.GetElementIds().Select(id => ui.Document.GetElement(id)).Where(e => e != null);
            else
            {
                var filter = new ElementMulticategoryFilter(new[] { BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns });
                source = new FilteredElementCollector(ui.Document).WhereElementIsNotElementType().WherePasses(filter).ToElements();
            }
            return ReadSource(ui.Document,source,categories,tolerance,progress,readMaterials,selected);
        }
        internal static ExtractionResult ReadSource(Document document,IEnumerable<Element> source,IReadOnlyCollection<BimCategory> categories,double tolerance,Action<string>? progress,bool readMaterials,bool selected=true,RevitMaterialReader? sharedMaterialReader=null)
        {
            var timer = Stopwatch.StartNew(); var result = new ExtractionResult(); result.Report.RevitVersion = document.Application.VersionNumber;
            var materialReader = readMaterials ? sharedMaterialReader??new RevitMaterialReader(document) : null;
            foreach (var element in source)
            {
                var category = Category(element);
                if (category.HasValue && !categories.Contains(category.Value)) continue;
                if (!category.HasValue && !selected) continue;
                result.Report.ElementsFound++; long id = element.Id.Value;
                progress?.Invoke("Leyendo " + result.Report.ElementsFound + ": elemento " + id);
                var snapshots = new List<SolidGeometryDiagnostic>();
                var materialSurfaces = new List<MaterialSurface>();
                try
                {
                    if (!category.HasValue) throw new ArgumentException("Categoría no soportada (incluidos modelos vinculados).");
                    var options = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false, ComputeReferences = false };
                    var geometry = element.get_Geometry(options);
                    if (geometry == null) throw new ArgumentException("Elemento sin geometría.");
                    var faceOrigins = readMaterials ? SourceFaces(geometry, Transform.Identity).ToList() : new List<SourceFace>();
                    var solids = Solids(geometry, Transform.Identity).Where(solid => solid.Faces.Size > 0).ToList();
                    CoreFrame? frame = null;
                    Exception? frameError = null;
                    try { frame = GetFrame(element, solids); }
                    catch (Exception ex) when (IsGeometryError(ex)) { frameError = ex; }
                    for (int index = 0; index < solids.Count; index++)
                    {
                        progress?.Invoke("Capturando elemento " + id + ", sólido " + index);
                        // Add before reading faces so a failed capture still preserves its partial evidence.
                        var snapshot = new SolidGeometryDiagnostic
                        {
                            SolidIndex = index,
                            ToleranceFeet = tolerance,
                            OriginalVolumeFeet3 = solids[index].Volume,
                            Frame = frame == null ? null : new DiagnosticFrame(frame)
                        };
                        snapshots.Add(snapshot);
                        try { CaptureBoundary(solids[index], frame, snapshot, materialReader, faceOrigins, document, element.Id, materialSurfaces, result.Report); }
                        catch (Exception ex) when (IsGeometryError(ex))
                        { snapshot.Issues.Add(new BoundaryIssue { Code = "capture_failed", Message = ex.Message }); }
                    }
                    if (frameError != null) throw frameError;
                    if (frame == null) throw new ArgumentException("Elemento sin marco local.");
                    var boxes = new List<Box>();var prisms=new List<ConvexPrism>();
                    foreach (var snapshot in snapshots)
                    {
                        progress?.Invoke("Comprobando elemento " + id + ", sólido " + snapshot.SolidIndex);
                        try
                        {
                            try
                            {
                                if(snapshot.Issues.Count>0)throw new ArgumentException("Se requiere conversión poligonal.");
                                var parts=GeometryDecomposer.Decompose(frame,snapshot.ToOrthogonalFaces(),snapshot.OriginalVolumeFeet3,tolerance);
                                if(category!=BimCategory.Wall&&parts.Count!=1)throw new ArgumentException("Contorno no rectangular.");
                                boxes.AddRange(parts);
                            }
                            catch(ArgumentException)
                            {
                                if(snapshot.Issues.Any(issue=>issue.Code=="capture_failed"))throw new ArgumentException("Captura geométrica incompleta.");
                                var parts=PlanarSectionDecomposer.Decompose(snapshot);prisms.AddRange(parts);
                                snapshot.Issues.Clear();
                            }
                        }
                        catch (GeometryValidationException ex) { snapshot.Issues.AddRange(ex.Issues); }
                        catch (Exception ex) when (IsGeometryError(ex))
                        { snapshot.Issues.Add(new BoundaryIssue { Code = "solid_conversion_failed", Message = ex.Message }); }
                    }
                    var failures = snapshots.SelectMany(snapshot => snapshot.Issues.Select(issue => "Sólido " + snapshot.SolidIndex + ": " + issue)).ToList();
                    if (failures.Count > 0)
                    {
                        result.Report.Diagnostics.Add(new Diagnostic(id, element.Name + ": " + string.Join("; ", failures))
                        { Code = "geometry_rejected", Category = category.Value.ToString(), Geometry = snapshots });
                        continue;
                    }
                    if (boxes.Count + prisms.Count == 0) throw new ArgumentException("Sin sólidos exportables.");
                    result.Elements.Add(new BimElement(id, category.Value, boxes, snapshots, materialSurfaces, frame, prisms));
                    if(prisms.Count>0)result.Report.Diagnostics.Add(new Diagnostic(id,"Contorno poligonal recuperado: "+prisms.Count+" prismas; volumen original comprobado."){Code="polygonal_conversion",Category=category.Value.ToString(),Severity="info"});
                }
                catch (Exception ex) when (IsGeometryError(ex))
                {
                    result.Report.Diagnostics.Add(new Diagnostic(id, element.Name + ": " + ex.Message)
                    { Code = "extraction_failed", Category = category?.ToString() ?? element.Category?.Name, Geometry = snapshots });
                }
            }
            result.Report.ElementsConverted = result.Elements.Count; result.Report.ElementsSkipped = result.Report.ElementsFound - result.Elements.Count;
            result.Report.ElapsedSeconds = timer.Elapsed.TotalSeconds; return result;
        }
        internal static IReadOnlyList<Box> CollisionBoxes(Element element, CoreFrame frame, double tolerance, RevitMaterialReader? reader, ExportReport report, List<MaterialSurface> surfaces)
        {
            var geometry=element.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine,IncludeNonVisibleObjects=false});
            if(geometry==null)throw new ArgumentException("Sin geometría verificable.");
            var boxes=new List<Box>();var origins=reader==null?new List<SourceFace>():SourceFaces(geometry,Transform.Identity).ToList();
            foreach(var solid in Solids(geometry,Transform.Identity).Where(s=>s.Faces.Size>0&&s.Volume>tolerance*tolerance*tolerance))
            {
                var snapshot=new SolidGeometryDiagnostic{ToleranceFeet=tolerance,OriginalVolumeFeet3=solid.Volume,Frame=new DiagnosticFrame(frame)};
                CaptureBoundary(solid,frame,snapshot,reader,origins,element.Document,element.Id,surfaces,report);
                if(snapshot.Issues.Count>0)throw new ArgumentException("Geometría no ortogonal o no evaluable.");
                boxes.AddRange(GeometryDecomposer.Decompose(frame,snapshot.ToOrthogonalFaces(),solid.Volume,tolerance));
            }
            if(boxes.Count==0)throw new ArgumentException("Sin sólidos verificables.");return boxes;
        }
        private static bool IsGeometryError(Exception ex) => ex is ArgumentException || ex is Autodesk.Revit.Exceptions.ApplicationException || ex is InvalidOperationException;
        private static void CaptureBoundary(Solid solid, CoreFrame? frame, SolidGeometryDiagnostic snapshot, RevitMaterialReader? materialReader, IReadOnlyList<SourceFace> sourceFaces, Document document, ElementId elementId, List<MaterialSurface> materialSurfaces, ExportReport report)
        {
            int faceIndex = 0;
            foreach (Face face in solid.Faces)
            {
                var capturedFace = new DiagnosticFace { IsPlanar = face is PlanarFace, SurfaceType = face.GetType().Name };
                snapshot.Faces.Add(capturedFace);
                if (!capturedFace.IsPlanar) snapshot.Issues.Add(new BoundaryIssue { Code = "curved_surface", Message = "Superficie curva no soportada.", FaceIndex = faceIndex });
                int loopIndex = 0;
                foreach (var loop in face.GetEdgesAsCurveLoops())
                {
                    var capturedLoop = new DiagnosticLoop(); capturedFace.Loops.Add(capturedLoop);
                    int edgeIndex = 0;
                    foreach (Curve edge in loop)
                    {
                        var start = ToVec(edge.GetEndPoint(0)); var end = ToVec(edge.GetEndPoint(1));
                        var capturedEdge = new DiagnosticEdge
                        {
                            CurveType = edge.GetType().Name,
                            IsLine = edge is Line,
                            WorldStartFeet = new DiagnosticPoint(start),
                            WorldEndFeet = new DiagnosticPoint(end),
                            LocalStartFeet = frame == null ? null : new DiagnosticPoint(frame.ToLocal(start)),
                            LocalEndFeet = frame == null ? null : new DiagnosticPoint(frame.ToLocal(end))
                        };
                        capturedLoop.Edges.Add(capturedEdge);
                        if (!capturedEdge.IsLine)
                        {
                            capturedEdge.WorldSamplesFeet = edge.Tessellate().Select(p => new DiagnosticPoint(ToVec(p))).ToList();
                            snapshot.Issues.Add(new BoundaryIssue
                            {
                                Code = "curved_edge",
                                Message = "Arista curva no soportada.",
                                FaceIndex = faceIndex,
                                LoopIndex = loopIndex,
                                EdgeIndex = edgeIndex,
                                WorldStartFeet = capturedEdge.WorldStartFeet,
                                WorldEndFeet = capturedEdge.WorldEndFeet,
                                LocalStartFeet = capturedEdge.LocalStartFeet,
                                LocalEndFeet = capturedEdge.LocalEndFeet
                            });
                        }
                        edgeIndex++;
                    }
                    for (int e = 0; e < capturedLoop.Edges.Count; e++)
                    {
                        var end = capturedLoop.Edges[e].WorldEndFeet.ToVector();
                        var next = capturedLoop.Edges[(e + 1) % capturedLoop.Edges.Count].WorldStartFeet.ToVector();
                        if ((end - next).Length > snapshot.ToleranceFeet)
                            snapshot.Issues.Add(new BoundaryIssue
                            {
                                Code = "invalid_contour",
                                Message = "Los extremos consecutivos del contorno no coinciden.",
                                FaceIndex = faceIndex,
                                LoopIndex = loopIndex,
                                EdgeIndex = e,
                                WorldStartFeet = new DiagnosticPoint(end),
                                WorldEndFeet = new DiagnosticPoint(next),
                                LocalStartFeet = frame == null ? null : new DiagnosticPoint(frame.ToLocal(end)),
                                LocalEndFeet = frame == null ? null : new DiagnosticPoint(frame.ToLocal(next))
                            });
                    }
                    loopIndex++;
                }
                if (materialReader != null && face is PlanarFace planar && capturedFace.Loops.Count > 0 && capturedFace.Loops.All(l => l.Edges.Count >= 3 && l.Edges.All(e => e.IsLine)))
                {
                    var id = face.MaterialElementId;
                    bool painted = false;
                    try
                    {
                        var origin = FindSourceFace(planar, sourceFaces, snapshot.ToleranceFeet);
                        if (origin != null)
                        {
                            var paint = document.GetPaintedMaterial(elementId, origin);
                            if (paint != ElementId.InvalidElementId) { id = paint; painted = true; }
                        }
                        else if (!report.Diagnostics.Any(d => d.ElementId == elementId.Value && d.Code == "paint_lookup_unavailable"))
                            report.Diagnostics.Add(new Diagnostic(elementId.Value, "No se pudo asociar una cara original para comprobar pintura; se utilizó su material geométrico.") { Code = "paint_lookup_unavailable" });
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException)
                    {
                        if (!report.Diagnostics.Any(d => d.ElementId == elementId.Value && d.Code == "paint_lookup_unavailable"))
                            report.Diagnostics.Add(new Diagnostic(elementId.Value, "Revit no permitió consultar pintura en una copia de instancia; se utilizó su material geométrico.") { Code = "paint_lookup_unavailable" });
                    }
                    var normal = ToVec(planar.FaceNormal).Unit();
                    var loops = capturedFace.Loops.Select(l => (IReadOnlyList<Vec3>)l.Edges.Select(e => e.WorldStartFeet.ToVector()).ToList()).ToList();
                    materialSurfaces.Add(new MaterialSurface(normal, normal.Dot(ToVec(planar.Origin)), loops, materialReader.Read(id), painted));
                }
                faceIndex++;
            }
        }
        private sealed class SourceFace
        {
            public Face Face { get; }
            public Transform Transform { get; }
            public SourceFace(Face face, Transform transform) { Face = face; Transform = transform; }
        }
        private static IEnumerable<SourceFace> SourceFaces(GeometryElement geometry, Transform transform)
        {
            foreach (GeometryObject item in geometry)
            {
                if (item is Solid solid) foreach (Face face in solid.Faces) yield return new SourceFace(face, transform);
                else if (item is GeometryInstance instance)
                    foreach (var face in SourceFaces(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform))) yield return face;
            }
        }
        private static Face? FindSourceFace(PlanarFace current, IReadOnlyList<SourceFace> origins, double tolerance)
        {
            var normal = current.FaceNormal;
            var candidates = origins.Where(o => o.Face is PlanarFace face && Math.Abs(o.Transform.OfVector(face.FaceNormal).Normalize().DotProduct(normal)) > 1 - 1e-8 &&
                Math.Abs(normal.DotProduct(o.Transform.OfPoint(face.Origin) - current.Origin)) <= tolerance).ToList();
            if (candidates.Count == 1) return candidates[0].Face;
            var point = current.GetEdgesAsCurveLoops().SelectMany(l => l).First().Evaluate(.5, true);
            var containing = candidates.Where(o =>
            {
                var projected = o.Face.Project(o.Transform.Inverse.OfPoint(point));
                return projected != null && projected.Distance <= tolerance && o.Face.IsInside(projected.UVPoint);
            }).ToList();
            return containing.Count == 1 ? containing[0].Face : null;
        }
        private static BimCategory? Category(Element e)
        {
            if (e.Category == null) return null; long id = e.Category.Id.Value;
            if (id == (long)BuiltInCategory.OST_Walls) return BimCategory.Wall;
            if (id == (long)BuiltInCategory.OST_Floors) return BimCategory.Floor;
            if (id == (long)BuiltInCategory.OST_Columns || id == (long)BuiltInCategory.OST_StructuralColumns) return BimCategory.Column;
            return null;
        }
        private static CoreFrame GetFrame(Element e, IReadOnlyList<Solid> solids)
        {
            if (e is Wall wall)
            {
                if (wall.WallType.Kind != WallKind.Basic || wall.IsStackedWall) throw new ArgumentException("Solo muros básicos rectos.");
                if (!(wall.Location is LocationCurve lc) || !(lc.Curve is Line line)) throw new ArgumentException("Muro curvo o sin eje recto.");
                var u = ToVec(line.Direction); if (Math.Abs(u.Z) > 1e-8) throw new ArgumentException("Muro con eje inclinado.");
                return new CoreFrame(ToVec(line.GetEndPoint(0)), u, Vec3.ZAxis.Cross(u), Vec3.ZAxis);
            }
            if (e is FamilyInstance instance)
            {
                var transform = instance.GetTransform(); var u = ToVec(transform.BasisX); var v = ToVec(transform.BasisY); var w = ToVec(transform.BasisZ);
                if (Math.Abs(w.Z) < 1 - 1e-8) throw new ArgumentException("Columna inclinada.");
                return new CoreFrame(ToVec(transform.Origin), u, v, w);
            }
            if (e is Floor)
            {
                var top = solids.SelectMany(s => s.Faces.Cast<Face>()).OfType<PlanarFace>().FirstOrDefault(f => f.FaceNormal.Z > 1 - 1e-8);
                if (top == null) throw new ArgumentException("Solo losas horizontales.");
                var edge = top.GetEdgesAsCurveLoops().SelectMany(l => l).OfType<Line>().FirstOrDefault();
                if (edge == null) throw new ArgumentException("Losa sin contorno rectilíneo.");
                var u = ToVec(edge.Direction); return new CoreFrame(ToVec(edge.GetEndPoint(0)), u, Vec3.ZAxis.Cross(u), Vec3.ZAxis);
            }
            throw new ArgumentException("Tipo de elemento no soportado.");
        }
        private static IEnumerable<Solid> Solids(GeometryElement geometry, Transform accumulated)
        {
            foreach (GeometryObject item in geometry)
            {
                if (item is Solid solid)
                {
                    if (solid.Faces.Size > 0) yield return accumulated.IsIdentity ? solid : SolidUtils.CreateTransformed(solid, accumulated);
                }
                else if (item is GeometryInstance instance)
                {
                    // Symbol geometry is local. Compose nested transforms exactly once.
                    foreach (var child in Solids(instance.GetSymbolGeometry(), accumulated.Multiply(instance.Transform))) yield return child;
                }
            }
        }
        private static Vec3 ToVec(XYZ p) => new Vec3(p.X, p.Y, p.Z);
    }
}
