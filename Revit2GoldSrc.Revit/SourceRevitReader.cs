using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Revit2GoldSrc.Core;
using Revit2Source;

namespace Revit2GoldSrc.Revit
{
    internal static class SourceRevitReader
    {
        internal static readonly BuiltInCategory[] Categories =
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Columns,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_StairsRuns,
            BuiltInCategory.OST_StairsLandings
        };
        public static string DocumentKey(Document doc) => string.IsNullOrEmpty(doc.PathName) ? "unsaved:" + doc.ProjectInformation.UniqueId : doc.PathName;
        private static bool HasSolids(Element e)
        {
            var geometry = e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
            if (geometry == null)
                return false;
            bool Contains(GeometryElement g)
            {
                foreach (GeometryObject obj in g)
                {
                    if (obj is Solid s && s.Faces.Size > 0 && s.Volume > 1e-12)
                        return true;
                    if (obj is GeometryInstance instance && Contains(instance.GetSymbolGeometry()))
                        return true;
                }

                return false;
            }

            return Contains(geometry);
        }

        public static ExtractionResult Read(UIDocument ui, bool selected, IReadOnlyCollection<BuiltInCategory> categories, GeometryQuality quality, double tolerance, Action<string> progress)
        {
            var doc = ui.Document;
            var input = (selected ? ui.Selection.GetElementIds().Select(doc.GetElement) : new FilteredElementCollector(doc).WhereElementIsNotElementType().AsEnumerable()).Where(e => e != null).ToList();
            var targets = new Dictionary<long, Element>();
            foreach (var element in input)
            {
                if (element is Stairs stairs)
                {
                    var children = stairs.GetStairsRuns().Concat(stairs.GetStairsLandings()).Select(doc.GetElement).ToList();
                    if (children.Count > 0 && children.All(HasSolids))
                        foreach (var child in children) targets[child.Id.Value] = child;
                    else if (HasSolids(stairs)) targets[stairs.Id.Value] = stairs;
                    else foreach (var child in children) targets[child.Id.Value] = child;
                }
                else
                    targets[element.Id.Value] = element;
            }

            foreach (var stairs in targets.Values.OfType<Stairs>().ToList())
                if (HasSolids(stairs))
                    foreach (var child in stairs.GetStairsRuns().Concat(stairs.GetStairsLandings()))
                        targets.Remove(child.Value);
            var rules = categories.Select(c => new CategorySelection { Key = ((long)c).ToString(System.Globalization.CultureInfo.InvariantCulture), Name = c.ToString() }).ToList();
            if (categories.Contains(BuiltInCategory.OST_Stairs))
            {
                foreach (var c in new[]
                {
                    BuiltInCategory.OST_StairsRuns,
                    BuiltInCategory.OST_StairsLandings
                }

                )
                    if (!rules.Any(r => r.Key == ((long)c).ToString(System.Globalization.CultureInfo.InvariantCulture)))
                        rules.Add(new CategorySelection { Key = ((long)c).ToString(System.Globalization.CultureInfo.InvariantCulture), Name = c.ToString() });
            }

            return RevitModelReader.Read(ui, true, false, rules, quality, tolerance, true, progress, GeometryProfile.Source, targets.Values.Select(e => e.Id).ToArray());
        }

        public static DoorExtraction Doors(UIDocument ui, SourceConfiguration configuration, ExtractionResult architecture, double tolerance, Action<string> progress)
        {
            string key = DocumentKey(ui.Document);
            var configs = configuration.Doors.Where(d => d.Document == key).ToDictionary(d => d.ElementId);
            var native = configs.Keys.Select(id => ui.Document.GetElement(new ElementId(id))).OfType<FamilyInstance>().Where(d => d.Category?.Id.Value == (long)BuiltInCategory.OST_Doors).ToList();
            var result = RevitDoorReader.ReadSource(ui.Document, native, tolerance, true, architecture.Report, progress, architecture.Elements, key, door =>
            {
                var c = configs[door.Id.Value];
                if (!Enum.TryParse(c.Kind, true, out DoorKind kind) || !Enum.TryParse(c.Hinge, true, out HingeSide hinge) || !Enum.TryParse(c.SlideDirection, true, out SlideDirection direction))
                    throw new ArgumentException("Configuración Source de puerta inválida.");
                return new DoorSettings
                {
                    Animated = kind != DoorKind.Static,
                    Kind = kind,
                    Hinge = hinge,
                    OpenAngleDegrees = c.Angle,
                    Speed = c.Speed,
                    Wait = c.Wait,
                    ReverseDirection = c.Reverse,
                    SlideDirection = direction,
                    SlideDistanceFeet = c.SlideDistanceFeet
                };
            });
            foreach (var door in result.Doors)
                door.Origin.Document = key;
            return result;
        }
    }
}
