using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit2GoldSrc.Core;
using CFrame = Revit2GoldSrc.Core.Frame;
namespace Revit2GoldSrc.Revit
{
    internal sealed class CategorySelection
    {
        public string Key { get; set; } = ""; public string Name { get; set; } = ""; public int Count { get; set; }
        public bool Enabled { get; set; } = true; public bool Collision { get; set; } = true; public string Texture { get; set; } = "";
    }
    internal sealed class ModelItem
    {
        public Element Element = null!; public Document Document = null!; public Transform Transform = null!; public ElementOrigin Origin = null!;
    }
    internal static class RevitModelReader
    {
        private static Vec3 V(XYZ p) => new Vec3(p.X, p.Y, p.Z);
        private static string DocumentKey(Document doc) => string.IsNullOrEmpty(doc.PathName) ? "unsaved:" + (doc.ProjectInformation?.UniqueId ?? doc.Title) : doc.PathName;
        private static string CategoryKey(Element e) => e.Category!.Id.Value < 0 ? e.Category.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : DocumentKey(e.Document) + "|" + e.Category.Id.Value;
        private static bool Physical(Element e) => e.Category?.CategoryType == CategoryType.Model && !(e is ElementType) && !(e is RevitLinkInstance) && !(e is Group) && !(e is SpatialElement) && !(e is AssemblyInstance) && !(e is Level) && !(e is Grid) && !(e is ReferencePlane);
        private static IEnumerable<ModelItem> Items(UIDocument ui, bool selected, bool links, ExportReport report, Action<string>? progress, bool doorsOnly = false, IReadOnlyCollection<ElementId>? selectedIds = null)
        {
            var documents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<ModelItem> Visit(Document doc, Transform transform, List<long> chain, IEnumerable<Element> source)
            {
                string key = DocumentKey(doc); if (!documents.Add(key)) { report.Diagnostics.Add(new Diagnostic(null, "Ciclo de vínculos omitido: " + doc.Title) { Code = "link_cycle" }); yield break; }
                try
                {
                    var elements = source.ToList(); var ids = new HashSet<long>(elements.Select(e => e.Id.Value));
                    foreach (var element in elements)
                    {
                        progress?.Invoke("Descubriendo " + doc.Title + ": " + element.Id.Value);
                        if (element is RevitLinkInstance link)
                        {
                            if (!links) continue; var linked = link.GetLinkDocument(); if (linked == null) { report.Diagnostics.Add(new Diagnostic(link.Id.Value, "Vínculo descargado: " + link.Name) { Code = "link_unloaded" }); continue; }
                            foreach (var item in Visit(linked, transform.Multiply(link.GetTotalTransform()), chain.Concat(new[] { link.Id.Value }).ToList(), new FilteredElementCollector(linked).WhereElementIsNotElementType())) yield return item;
                            continue;
                        }
                        if (!Physical(element) || (doorsOnly ? element.Category!.Id.Value != (long)BuiltInCategory.OST_Doors : element.Category!.Id.Value == (long)BuiltInCategory.OST_Doors)) continue;
                        // Shared subcomponents are already represented by GetSymbolGeometry of a selected parent.
                        if (element is FamilyInstance family && family.SuperComponent != null && ids.Contains(family.SuperComponent.Id.Value) && IncludesShared(family.SuperComponent, family)) continue;
                        yield return new ModelItem { Element = element, Document = doc, Transform = transform, Origin = new ElementOrigin { Document = key, DocumentName = doc.Title, LinkInstances = chain.ToList(), ElementId = element.Id.Value } };
                    }
                }
                finally { documents.Remove(key); }
            }
            return Visit(ui.Document, Transform.Identity, new List<long>(), selected ? (selectedIds ?? ui.Selection.GetElementIds().ToArray()).Select(ui.Document.GetElement).Where(e => e != null) : new FilteredElementCollector(ui.Document).WhereElementIsNotElementType());
        }
        private static bool HasPhysicalGeometry(Element e)
        {
            try { var geometry = e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }); if (geometry == null) return false; return Solids(geometry, Transform.Identity).Any(s => s.Item1.Faces.Size > 0 && s.Item1.Volume > 1e-12) || Meshes(geometry, Transform.Identity).Any(m => m.Item1.NumTriangles > 0); }
            catch (Exception ex) when (GeometryError(ex)) { return e.get_BoundingBox(null) != null; }
        }
        public static IReadOnlyList<CategorySelection> Discover(UIDocument ui, bool links, Action<string>? progress = null)
        {
            var report = new ExportReport(); return Items(ui, false, links, report, progress).Where(item => HasPhysicalGeometry(item.Element)).GroupBy(item => CategoryKey(item.Element)).Select(g => new CategorySelection { Key = g.Key, Name = g.First().Element.Category!.Name, Count = g.Count() }).OrderBy(c => c.Name).ToList();
        }
        public static ExtractionResult Read(UIDocument ui, bool selected, bool links, IReadOnlyList<CategorySelection> categories, GeometryQuality quality, double tolerance, bool materials, Action<string> progress, GeometryProfile? profile = null, IReadOnlyCollection<ElementId>? selectedIds = null)
        {
            var result = new ExtractionResult(); result.Report.RevitVersion = ui.Document.Application.VersionNumber; var timer = Stopwatch.StartNew(); var rules = categories.Where(c => c.Enabled).ToDictionary(c => c.Key); var readers = new Dictionary<string, RevitMaterialReader>();
            foreach (var item in Items(ui, selected, links, result.Report, progress, selectedIds: selectedIds))
            {
                var element = item.Element; string categoryKey = CategoryKey(element); if (!rules.TryGetValue(categoryKey, out var rule)) continue;
                progress("Leyendo familia/MEP " + item.Document.Title + ": " + element.Id.Value);
                bool counted = false; var sourceInfo = new ElementExportInfo { Origin = item.Origin, Category = element.Category.Name, Family = (element as FamilyInstance)?.Symbol.FamilyName ?? element.Category.Name, Type = item.Document.GetElement(element.GetTypeId())?.Name ?? element.Name, Collision = rule.Collision, Quality = quality.ToString() };
                try
                {
                    var geometry = element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false }); if (geometry == null) continue;
                    var solids = Solids(geometry, item.Transform).Where(s => s.Item1.Faces.Size > 0 && s.Item1.Volume > tolerance * tolerance * tolerance).ToList(); var nativeMeshes = Meshes(geometry, item.Transform).ToList(); if (solids.Count == 0 && nativeMeshes.Count == 0) continue;
                    result.Report.ElementsFound++; counted = true;
                    if (!readers.TryGetValue(item.Origin.Document, out var reader)) { reader = new RevitMaterialReader(item.Document, item.Origin.Document); readers.Add(item.Origin.Document, reader); }
                    var legacy = RevitGeometryExtractor.ReadSource(item.Document, new[] { element }, new[] { BimCategory.Wall, BimCategory.Floor, BimCategory.Column }, tolerance, progress, materials, true, reader);
                    if (legacy.Elements.Count == 1 && Math.Abs(item.Transform.BasisZ.Normalize().Z) > 1 - 1e-8)
                    {
                        var existing = TransformElement(legacy.Elements[0], item.Transform, item.Origin.Document); Decorate(existing, item, rule, sourceInfo); result.Elements.Add(existing); result.Report.Diagnostics.AddRange(legacy.Report.Diagnostics); continue;
                    }
                    var parts = new List<ConvexSolid>();
                    foreach (var nativeMesh in nativeMeshes)
                    {
                        var triangles = new List<MeshFace>(); if (nativeMesh.Item1.NumTriangles > 24000) throw new ArgumentException("Malla importada supera 24000 triángulos.");
                        var material = materials ? reader.Read(nativeMesh.Item1.MaterialElementId) : null;
                        for (int i = 0; i < nativeMesh.Item1.NumTriangles; i++) { if (i % 32 == 0) progress("Leyendo malla " + element.Id.Value); var triangle = nativeMesh.Item1.get_Triangle(i); triangles.Add(new MeshFace(Enumerable.Range(0, 3).Select(j => V(nativeMesh.Item2.OfPoint(triangle.get_Vertex(j)))).ToList(), material)); }
                        parts.AddRange(ConvexMeshDecomposer.Decompose(new TriangleMesh(triangles), tolerance, () => progress("Descomponiendo malla " + element.Id.Value), profile: profile));
                    }
                    double maxVolumeError = 0, maxDeviation = 0; var settings = new QualitySettings(quality);
                    foreach (var solid in solids)
                    {
                        Exception? last = null; bool converted = false;
                        for (int attempt = 0; attempt < 3; attempt++)
                        {
                            progress("Teselando " + element.Id.Value + ", intento " + (attempt + 1));
                            try
                            {
                                var mesh = Tessellate(solid.Item1, solid.Item2, element, settings, attempt, tolerance, materials ? reader : null, result.Report, () => progress("Asociando superficies " + element.Id.Value), out double deviation);
                                double scale = solid.Item2.BasisX.GetLength(); double expected = solid.Item1.Volume * Math.Abs(solid.Item2.Determinant); double actual = Math.Abs(ConvexMeshDecomposer.Volume(mesh.Triangles)); double error = Math.Abs(actual - expected) / expected;
                                if (error > settings.VolumeError || deviation > settings.AccuracyFeet * scale + tolerance) { last = new ArgumentException("Aproximación supera calidad: error de volumen " + error.ToString("P2") + ", desviación " + deviation + " pies."); continue; }
                                var convertedParts = ConvexMeshDecomposer.Decompose(mesh, tolerance, () => progress("Descomponiendo " + element.Id.Value + " (" + mesh.Triangles.Count + " triángulos; límite 30 s)"), profile: profile); parts.AddRange(convertedParts); maxVolumeError = Math.Max(maxVolumeError, error); maxDeviation = Math.Max(maxDeviation, deviation); converted = true; break;
                            }
                            catch (Exception ex) when (GeometryError(ex)) { last = ex; break; }
                        }
                        if (!converted) throw new ArgumentException("Sólido no verificable: " + last?.Message);
                    }
                    var bim = new BimElement(element.Id.Value, LegacyCategory(element), Array.Empty<Box>(), convexSolids: parts) { Quality = quality, VolumeError = maxVolumeError, SurfaceDeviationFeet = maxDeviation }; Decorate(bim, item, rule, sourceInfo); result.Elements.Add(bim);
                }
                catch (Exception ex) when (GeometryError(ex)) { if (!counted) result.Report.ElementsFound++; sourceInfo.Status = "Omitted"; result.Report.Elements.Add(sourceInfo); result.Report.Diagnostics.Add(new Diagnostic(element.Id.Value, item.Document.Title + " / " + element.Name + ": " + ex.Message) { Code = "family_geometry_rejected", Category = element.Category.Name }); }
            }
            result.Report.ElementsConverted = result.Elements.Count; result.Report.ElementsSkipped = result.Report.ElementsFound - result.Elements.Count; result.Report.ElapsedSeconds = timer.Elapsed.TotalSeconds; return result;
        }
        public static DoorExtraction ReadDoors(UIDocument ui, bool selected, bool links, double tolerance, bool materials, ExportReport report, Action<string> progress, IReadOnlyList<BimElement> exported)
        {
            var result = new DoorExtraction();
            foreach (var group in Items(ui, selected, links, report, progress, true).GroupBy(item => item.Origin.Document + "|" + string.Join("/", item.Origin.LinkInstances)))
            {
                var first = group.First(); var nativeDoors = group.Select(item => item.Element).OfType<FamilyInstance>().ToList();
                var read = RevitDoorReader.ReadSource(first.Document, nativeDoors, tolerance, materials, report, progress, exported.Where(e => e.Origin.Document == first.Origin.Document && e.Origin.LinkInstances.SequenceEqual(first.Origin.LinkInstances)).ToList(), first.Origin.Document);
                Vec3 Point(Vec3 p) => V(first.Transform.OfPoint(new XYZ(p.X, p.Y, p.Z))); Vec3 Vector(Vec3 p) => V(first.Transform.OfVector(new XYZ(p.X, p.Y, p.Z)));
                CFrame Frame(CFrame f) => new CFrame(Point(f.Origin), Vector(f.U), Vector(f.V), Vector(f.W)); double scale = first.Transform.BasisX.GetLength();
                Box Box(Box b) => new Box(Frame(b.Frame), b.Min * scale, b.Max * scale);
                MaterialSurface Surface(MaterialSurface s) { var n = Vector(s.Normal).Unit(); return new MaterialSurface(n, n.Dot(Point(s.Normal * s.DistanceFeet)), s.WorldLoopsFeet.Select(l => (IReadOnlyList<Vec3>)l.Select(Point).ToList()).ToList(), s.Material, s.IsPainted); }
                foreach (var door in read.Doors)
                {
                    var origin = group.First(item => item.Element.Id.Value == door.Id).Origin;
                    if (door.Settings.IsDynamic && (first.Transform.HasReflection || first.Transform.BasisZ.Normalize().Z < 1 - 1e-8))
                    { report.ElementsFound++; report.Doors.Add(new DoorExportInfo { ElementId = door.Id, Origin = origin, Status = "Omitted" }); report.Diagnostics.Add(new Diagnostic(door.Id, "Puerta animada de vínculo reflejado o inclinado: orientación de apertura no verificable; vano libre.") { Code = "linked_door_motion_uncertain" }); continue; }
                    if (door.Settings.Group.Length > 0) door.Settings.Group = group.Key + "|" + door.Settings.Group;
                    result.Doors.Add(new DoorDefinition(door.Id, Frame(door.Frame), door.Settings, door.Leaves.Select(l => new DoorLeaf(Box(l.GeometryFeet), l.InferredHinge, l.Materials.Select(Surface).ToList())).ToList(), door.FramesFeet.Select(Box).ToList(), door.UncertainGeometryFeet.Select(Box).ToList()) { Origin = origin });
                }
                result.Unknown.AddRange(read.Unknown.Select(Box));
            }
            return result;
        }
        private static bool IncludesShared(Element parent, FamilyInstance child)
        {
            var geometry = parent.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }); if (geometry == null) return false; var expected = child.GetTransform();
            bool Contains(GeometryElement source, Transform accumulated)
            {
                foreach (GeometryObject obj in source) if (obj is GeometryInstance instance)
                {
                    var transform = accumulated.Multiply(instance.Transform);
                    if (instance.GetSymbolGeometryId().SymbolId == child.Symbol.Id && transform.Origin.DistanceTo(expected.Origin) < 1e-8 && (transform.BasisX - expected.BasisX).GetLength() < 1e-8 && (transform.BasisY - expected.BasisY).GetLength() < 1e-8 && (transform.BasisZ - expected.BasisZ).GetLength() < 1e-8) return true;
                    if (Contains(instance.GetSymbolGeometry(), transform)) return true;
                }
                return false;
            }
            return Contains(geometry, Transform.Identity);
        }
        private static bool GeometryError(Exception ex) => ex is ArgumentException || ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException;
        private static BimCategory LegacyCategory(Element e) => e is Floor ? BimCategory.Floor : e.Category!.Id.Value == (long)BuiltInCategory.OST_Columns || e.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns ? BimCategory.Column : BimCategory.Wall;
        private static void Decorate(BimElement bim, ModelItem item, CategorySelection rule, ElementExportInfo info)
        { bim.Origin = item.Origin; bim.CategoryKey = rule.Key; bim.CategoryName = rule.Name; bim.Collision = rule.Collision; bim.FamilyName = info.Family; bim.TypeName = info.Type; }
        private static BimElement TransformElement(BimElement source, Transform transform, string documentKey)
        {
            CFrame Frame(CFrame f) => new CFrame(V(transform.OfPoint(new XYZ(f.Origin.X, f.Origin.Y, f.Origin.Z))), V(transform.OfVector(new XYZ(f.U.X, f.U.Y, f.U.Z))), V(transform.OfVector(new XYZ(f.V.X, f.V.Y, f.V.Z))), V(transform.OfVector(new XYZ(f.W.X, f.W.Y, f.W.Z))));
            Vec3 Point(Vec3 p) => V(transform.OfPoint(new XYZ(p.X, p.Y, p.Z))); double scale = transform.BasisX.GetLength();
            var boxes = source.Solids.Select(b => new Box(Frame(b.Frame), b.Min * scale, b.Max * scale)).ToList();
            var prisms = source.Prisms.Select(p => { var vertices = p.Vertices.Select(Point).ToList(); return new ConvexPrism(p.Polygon.Select(v => Point(new Vec3(v.X, v.Y, p.Bottom))).Select(v => new Vec3(v.X, v.Y, 0)).ToList(), vertices.Min(v => v.Z), vertices.Max(v => v.Z)); }).ToList();
            var surfaces = source.MaterialSurfaces.Select(s => { s.Material.SourceDocument = documentKey; var n = V(transform.OfVector(new XYZ(s.Normal.X, s.Normal.Y, s.Normal.Z))).Unit(); var point = Point(s.Normal * s.DistanceFeet); return new MaterialSurface(n, n.Dot(point), s.WorldLoopsFeet.Select(l => (IReadOnlyList<Vec3>)l.Select(Point).ToList()).ToList(), s.Material, s.IsPainted); }).ToList();
            return new BimElement(source.Id, source.Category, boxes, source.SourceGeometry, surfaces, source.MaterialFrame == null ? null : Frame(source.MaterialFrame), prisms);
        }
        private static IEnumerable<Tuple<Solid, Transform>> Solids(GeometryElement geometry, Transform transform)
        { foreach (GeometryObject item in geometry) { if (item is Solid solid) yield return Tuple.Create(solid, transform); else if (item is GeometryInstance instance) foreach (var child in Solids(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform))) yield return child; } }
        private static IEnumerable<Tuple<Mesh, Transform>> Meshes(GeometryElement geometry, Transform transform)
        { foreach (GeometryObject item in geometry) { if (item is Mesh mesh) yield return Tuple.Create(mesh, transform); else if (item is GeometryInstance instance) foreach (var child in Meshes(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform))) yield return child; } }
        private static TriangleMesh Tessellate(Solid solid, Transform transform, Element element, QualitySettings settings, int attempt, double tolerance, RevitMaterialReader? reader, ExportReport report, Action checkpoint, out double maxDeviation)
        {
            maxDeviation = 0; var timer = Stopwatch.StartNew(); var controls = new SolidOrShellTessellationControls { Accuracy = settings.AccuracyFeet / Math.Pow(2, attempt), LevelOfDetail = Math.Min(1, settings.LevelOfDetail + attempt * .25) };
            var native = SolidUtils.TessellateSolidOrShell(solid, controls); var faces = solid.Faces.Cast<Face>().ToList(); var output = new List<MeshFace>(); var tags = new Dictionary<Face, Tuple<BimMaterial?, bool>>();
            foreach (var face in faces)
            {
                var id = face.MaterialElementId; bool painted = false;
                if (reader != null) try { var paint = element.Document.GetPaintedMaterial(element.Id, face); if (paint != ElementId.InvalidElementId) { id = paint; painted = true; } } catch (Autodesk.Revit.Exceptions.ApplicationException) { if (!report.Diagnostics.Any(d => d.ElementId == element.Id.Value && d.Code == "paint_lookup_unavailable")) report.Diagnostics.Add(new Diagnostic(element.Id.Value, "Pintura no accesible en cara de familia; se utilizó material geométrico.") { Code = "paint_lookup_unavailable" }); }
                tags[face] = Tuple.Create(reader?.Read(id), painted);
            }
            for (int shell = 0; shell < native.ShellComponentCount; shell++)
            {
                var component = native.GetShellComponent(shell); if (output.Count + component.TriangleCount > 24000) throw new ArgumentException("Teselación supera 24000 triángulos por sólido.");
                for (int i = 0; i < component.TriangleCount; i++)
                {
                    if (i % 16 == 0) { checkpoint(); if (timer.Elapsed.TotalSeconds > 30) throw new ArgumentException("Asignación de superficies supera presupuesto de 30 segundos."); }
                    var triangle = component.GetTriangle(i); var points = new[] { component.GetVertex(triangle.VertexIndex0), component.GetVertex(triangle.VertexIndex1), component.GetVertex(triangle.VertexIndex2) }; var center = (points[0] + points[1] + points[2]) / 3;
                    var candidates = new List<Tuple<Face, double>>();
                    foreach (var face in faces) { try { var projected = face.Project(center); if (projected != null && face.IsInside(projected.UVPoint)) candidates.Add(Tuple.Create(face, projected.Distance)); } catch (Exception ex) when (GeometryError(ex)) { } }
                    if (candidates.Count == 0) throw new ArgumentException("No se pudo asociar triángulo con superficie original."); double min = candidates.Min(c => c.Item2); var nearest = candidates.Where(c => c.Item2 <= min + tolerance).ToList();
                    var chosen = nearest[0].Item1; var tag = tags[chosen]; bool ambiguous = nearest.Any(c => (tags[c.Item1].Item1?.SourceKey ?? "") != (tag.Item1?.SourceKey ?? ""));
                    foreach (var sample in new[] { center, (points[0] + points[1]) / 2, (points[1] + points[2]) / 2, (points[2] + points[0]) / 2 }) { var projection = chosen.Project(sample); if (projection == null) throw new ArgumentException("Desviación superficial no evaluable."); maxDeviation = Math.Max(maxDeviation, projection.Distance * transform.BasisX.GetLength()); }
                    if (ambiguous && reader != null) report.Diagnostics.Add(new Diagnostic(element.Id.Value, "Triángulo con material ambiguo: gris neutro.") { Code = "ambiguous_face_material" });
                    output.Add(new MeshFace(points.Select(p => V(transform.OfPoint(p))).ToList(), ambiguous ? null : tag.Item1, !ambiguous && tag.Item2));
                }
            }
            var mesh = new TriangleMesh(output);
            foreach (var face in faces)
            {
                checkpoint(); var bounds = face.GetBoundingBox();
                for (int u = 1; u <= 3; u++) for (int v = 1; v <= 3; v++)
                {
                    if (timer.Elapsed.TotalSeconds > 30) throw new ArgumentException("Validación superficial supera presupuesto de 30 segundos.");
                    var uv = new UV(bounds.Min.U + (bounds.Max.U - bounds.Min.U) * u / 4, bounds.Min.V + (bounds.Max.V - bounds.Min.V) * v / 4); if (!face.IsInside(uv)) continue;
                    var sample = V(transform.OfPoint(face.Evaluate(uv))); maxDeviation = Math.Max(maxDeviation, MeshQuality.Distance(sample, mesh));
                }
            }
            return mesh;
        }
    }
}
