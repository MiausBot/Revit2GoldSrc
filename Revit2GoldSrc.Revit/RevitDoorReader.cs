using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit2GoldSrc.Core;
using CoreFrame = Revit2GoldSrc.Core.Frame;
namespace Revit2GoldSrc.Revit
{
    internal sealed class DoorExtraction
    { public List<DoorDefinition> Doors { get; } = new List<DoorDefinition>(); public List<Box> Unknown { get; } = new List<Box>(); }
    internal static class RevitDoorReader
    {
        private static Vec3 V(XYZ p) => new Vec3(p.X, p.Y, p.Z);
        private static T Choice<T>(Element door, string key, T fallback) where T : struct
        { string? text = DoorParameters.Get(door, key)?.AsString(); if (string.IsNullOrWhiteSpace(text)) return fallback; if (Enum.TryParse<T>(text, true, out var value) && Enum.IsDefined(typeof(T), value)) return value; throw new ArgumentException("GoldSrc_" + key + " inválido: " + text); }
        private static double Number(Element door, string key, double fallback) { var p = DoorParameters.Get(door, key); return p != null && p.HasValue ? p.AsDouble() : fallback; }
        private static bool Flag(Element door, string key) => DoorParameters.Get(door, key)?.AsInteger() == 1;
        private static double Dimension(FamilyInstance door, BuiltInParameter key)
        { var p = door.get_Parameter(key); if (p == null || !p.HasValue || p.AsDouble() <= 0) p = door.Symbol.get_Parameter(key); return p?.AsDouble() ?? 0; }
        public static DoorExtraction Read(UIDocument ui, bool selected, double tolerance, bool materials, ExportReport report, Action<string> progress, IReadOnlyList<BimElement> exported)
        {
            var doc = ui.Document;
            var doors = selected ? ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>().Where(e => e.Category?.Id.Value == (long)BuiltInCategory.OST_Doors).ToList() : new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();
            return ReadSource(doc, doors, tolerance, materials, report, progress, exported);
        }
        internal static DoorExtraction ReadSource(Document doc, IReadOnlyList<FamilyInstance> doors, double tolerance, bool materials, ExportReport report, Action<string> progress, IReadOnlyList<BimElement> exported, string sourceDocument = "", Func<FamilyInstance, DoorSettings>? sourceSettings = null)
        {
            var result = new DoorExtraction(); var reader = materials ? new RevitMaterialReader(doc, sourceDocument) : null;
            foreach (var door in doors)
            {
                progress("Leyendo puerta " + door.Id.Value);
                try
                {
                    if (!(door.Location is LocationPoint point)) throw new ArgumentException("Puerta sin ubicación puntual.");
                    var facing = V(door.FacingOrientation); facing = new Vec3(facing.X, facing.Y, 0).Unit(); var frame = new CoreFrame(V(point.Point), Vec3.ZAxis.Cross(facing), facing, Vec3.ZAxis);
                    var settings = sourceSettings?.Invoke(door) ?? new DoorSettings
                    {
                        Animated = Flag(door, "Animated"),
                        Kind = Choice(door, "DoorType", DoorKind.Rotating),
                        OpenAngleDegrees = Number(door, "OpenAngle", Math.PI / 2) * 180 / Math.PI,
                        Speed = Number(door, "Speed", 100),
                        Wait = Number(door, "Wait", 3),
                        Activation = Choice(door, "Activation", DoorActivation.Use),
                        ReverseDirection = Flag(door, "ReverseDirection"),
                        Hinge = Choice(door, "HingeSide", HingeSide.Auto),
                        HingeOffsetXFeet = Number(door, "HingeOffsetX", 0),
                        HingeOffsetYFeet = Number(door, "HingeOffsetY", 0),
                        SlideDirection = Choice(door, "SlideDirection", SlideDirection.Auto),
                        SlideDistanceFeet = Number(door, "SlideDistance", 0),
                        LeafThicknessFeet = Number(door, "LeafThickness", .04 / .3048),
                        LeafCount = Choice(door, "LeafCount", DoorLeafCount.Auto),
                        Group = DoorParameters.Get(door, "DoorGroup")?.AsString() ?? "",
                        ActivatorKind = Choice(door, "ActivatorType", DoorActivatorKind.Trigger),
                        ActivatorOffsetFeet = new Vec3(Number(door, "ActivatorOffsetX", 0), Number(door, "ActivatorOffsetY", 0), Number(door, "ActivatorOffsetZ", 0))
                    };
                    if (sourceSettings == null) { string placement = DoorParameters.Get(door, "ActivatorPlacement")?.AsString() ?? "Auto"; if (!placement.Equals("Auto", StringComparison.OrdinalIgnoreCase) && !placement.Equals("Manual", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("GoldSrc_ActivatorPlacement debe ser Auto o Manual."); settings.ManualActivator = placement.Equals("Manual", StringComparison.OrdinalIgnoreCase); }
                    settings.Validate();
                    double width = Dimension(door, BuiltInParameter.DOOR_WIDTH), height = Dimension(door, BuiltInParameter.DOOR_HEIGHT);
                    if (!Vec3.Finite(width) || !Vec3.Finite(height) || width <= tolerance || height <= tolerance) throw new ArgumentException("Ancho o altura no verificables; no se sustituyen por el bounding box.");
                    var surfaces = new List<MaterialSurface>(); IReadOnlyList<Box> geometry = Array.Empty<Box>(); var unknown = new List<Box>();
                    try { geometry = RevitGeometryExtractor.CollisionBoxes(door, frame, tolerance, reader, report, surfaces); }
                    catch (Exception ex) when (IsGeometryError(ex)) { var bounds = Bounds(door); if (bounds != null) unknown.Add(bounds); }
                    int count = settings.LeafCount == DoorLeafCount.Double ? 2 : 1;
                    var real = geometry.Where(b => Math.Abs((b.Max.Z - b.Min.Z) - height) < Math.Max(.02, height * .02) && (b.Max.Y - b.Min.Y) <= width * .15 && (b.Max.X - b.Min.X) > width * .35 && Math.Abs((b.Max.X + b.Min.X) / 2) <= width / 2 + tolerance).ToList();
                    if (settings.LeafCount == DoorLeafCount.Auto && real.Count == 2) count = 2;
                    bool useReal = real.Count == count && real.All(b => Math.Abs((b.Max.X - b.Min.X) - width / count) < width * .04);
                    var frames = useReal ? geometry.Except(real).ToList() : geometry.Where(b => (b.Max.X - b.Min.X) < width * .25 || (b.Min.Z >= height * .95)).ToList();
                    var leaves = new List<DoorLeaf>();
                    for (int i = 0; i < count; i++)
                    {
                        var box = useReal ? real.OrderBy(b => b.Min.X).ElementAt(i) : new Box(frame, new Vec3(-width / 2 + i * width / count, -settings.LeafThicknessFeet / 2, 0), new Vec3(-width / 2 + (i + 1) * width / count, settings.LeafThicknessFeet / 2, height));
                        HingeSide inferred = count == 2 ? (i == 0 ? HingeSide.Left : HingeSide.Right) : InferHinge(door, frame, width, tolerance);
                        leaves.Add(new DoorLeaf(box, inferred, surfaces));
                    }
                    if (!useReal)
                    {
                        report.Diagnostics.Add(new Diagnostic(door.Id.Value, "Hoja rectangular simplificada con dimensiones de familia; espesor configurado o 40 mm por defecto.") { Code = "door_simplified" });
                        // Discarded solids that cannot be classified as jambs remain uncertainty, not invented obstacles.
                        unknown.AddRange(geometry.Except(frames));
                    }
                    result.Doors.Add(new DoorDefinition(door.Id.Value, frame, settings, leaves, frames, unknown));
                    report.Diagnostics.Add(new Diagnostic(door.Id.Value, "Orientación nativa usada sin invertir de nuevo: HandFlipped=" + door.HandFlipped + ", FacingFlipped=" + door.FacingFlipped + ", transform reflejado=" + door.GetTransform().HasReflection) { Code = "door_orientation", Severity = "info" });
                }
                catch (Exception ex) when (IsGeometryError(ex)) { report.ElementsFound++; report.Doors.Add(new DoorExportInfo { ElementId = door.Id.Value, Status = "Omitted" }); report.Diagnostics.Add(new Diagnostic(door.Id.Value, ex.Message) { Code = "door_read_failed" }); }
            }
            // Geometry omitted from the architecture, and unselected architecture, still constrains safe movement.
            var exportedIds = new HashSet<long>(exported.Select(e => e.Id));
            foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (exportedIds.Contains(e.Id.Value) || doors.Any(d => d.Id == e.Id) || e.Category == null) continue;
                long category = e.Category.Id.Value;
                if (e is FamilyInstance || category == (long)BuiltInCategory.OST_Ceilings || category == (long)BuiltInCategory.OST_Roofs || category == (long)BuiltInCategory.OST_Railings || category == (long)BuiltInCategory.OST_Doors || category == (long)BuiltInCategory.OST_Walls || category == (long)BuiltInCategory.OST_Floors || category == (long)BuiltInCategory.OST_Columns || category == (long)BuiltInCategory.OST_StructuralColumns || category == (long)BuiltInCategory.OST_GenericModel || category == (long)BuiltInCategory.OST_Stairs || category == (long)BuiltInCategory.OST_Ramps || e is RevitLinkInstance)
                { var b = Bounds(e); if (b != null) result.Unknown.Add(b); }
            }
            return result;
        }
        private static bool IsGeometryError(Exception ex) => ex is ArgumentException || ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException;
        private static HingeSide InferHinge(FamilyInstance door, CoreFrame frame, double width, double tolerance)
        {
            var sides = new HashSet<HingeSide>(); foreach (var id in door.GetSubComponentIds())
            { var child = door.Document.GetElement(id); if (child == null || !(child.Name.IndexOf("hinge", StringComparison.OrdinalIgnoreCase) >= 0 || child.Name.IndexOf("bisagra", StringComparison.OrdinalIgnoreCase) >= 0)) continue; var bounds = child.get_BoundingBox(null); if (bounds == null) continue; var center = frame.ToLocal(V(bounds.Transform.OfPoint((bounds.Min + bounds.Max) / 2))); if (Math.Abs(Math.Abs(center.X) - width / 2) < Math.Max(.05, tolerance)) sides.Add(center.X < 0 ? HingeSide.Left : HingeSide.Right); }
            return sides.Count == 1 ? sides.Single() : HingeSide.Auto;
        }
        private static Box? Bounds(Element element)
        { var b = element.get_BoundingBox(null); if (b == null) return null; var transform = b.Transform; return new Box(new CoreFrame(V(transform.Origin), V(transform.BasisX), V(transform.BasisY), V(transform.BasisZ)), V(b.Min), V(b.Max)); }
    }
}
