using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using Revit2GoldSrc.Core;
namespace Revit2GoldSrc.GoldSrc
{
    public sealed class ExportResult
    {
        public MapDocument Map { get; }
        public ExportReport Report { get; }
        public string Text { get; internal set; }
        public byte[]? GeneratedWad { get; }
        public byte[]? GeneratedToolsWad { get; internal set; }
        internal ExportResult(MapDocument map, ExportReport report, string text, byte[]? generatedWad = null) { Map = map; Report = report; Text = text; GeneratedWad = generatedWad; }
    }
    public static class Exporter
    {
        public static ExportResult Prepare(IReadOnlyList<BimElement> elements, ExportOptions options, ExportReport report, Action<string>? progress = null, IReadOnlyList<DoorDefinition>? doors = null, IReadOnlyList<Box>? uncertainGeometryFeet = null)
        {
            var timer = Stopwatch.StartNew(); options.Validate();
            doors = options.Doors.Enabled ? doors ?? Array.Empty<DoorDefinition>() : Array.Empty<DoorDefinition>();
            bool fromRevit = options.TextureSource == TextureSourceMode.RevitMaterials;
            report.TextureSource = options.TextureSource.ToString();
            var materials = fromRevit ? new MaterialTextureSet(report) : null;
            var wad = fromRevit ? null : WadCatalog.Read(options.WadPath);
            report.ElementsFound = Math.Max(report.ElementsFound, elements.Count);
            var candidates = new List<BimElement>();
            foreach (var element in elements)
            {
                progress?.Invoke("Comprobando elemento " + element.Id);
                try
                {
                    if (element.Solids.Count + element.Prisms.Count + element.ConvexSolids.Count == 0) throw new ArgumentException("Elemento sin sólidos.");
                    foreach (var solid in element.Solids) solid.Validate(options.ToleranceFeet);
                    foreach(var prism in element.Prisms)prism.Validate(options.ToleranceFeet);
                    foreach(var solid in element.ConvexSolids)solid.Validate(options.ToleranceFeet);
                    if (element.Vertices.Any(p => !p.IsFinite)) throw new ArgumentException("Vértices no finitos.");
                    candidates.Add(element);
                }
                catch (ArgumentException ex) { report.Diagnostics.Add(new Diagnostic(element.Id, ex.Message) { Code = "brush_conversion_failed", Category = element.Category.ToString(), Geometry = element.SourceGeometry?.ToList() }); }
            }
            string Texture(BimElement e)
            {
                if(e.CategoryKey.Length>0&&options.CategoryTextures.TryGetValue(e.CategoryKey,out var specific))return specific;
                if(options.Textures.TryGetValue(e.Category,out var existing))return existing;
                throw new ArgumentException("Falta textura para "+(e.CategoryName.Length>0?e.CategoryName:e.Category.ToString()));
            }
            if(!fromRevit)foreach(var element in candidates)wad!.Require(Texture(element));
            if (options.TestEnclosure && !fromRevit) wad!.Require(options.EnclosureTexture);
            if (candidates.Count == 0 && doors.Count == 0) throw new ArgumentException("No hay elementos compatibles para exportar. Revise el análisis.");
            var origin = options.CenterOrigin ? Coordinates.NormalizeCoordinates(candidates.SelectMany(e=>e.Vertices).Concat(doors.SelectMany(d=>d.Leaves.SelectMany(l=>l.GeometryFeet.Vertices)))) : options.OriginFeet;
            double t = options.ToleranceFeet * options.UnitsPerFoot;
            var map = new MapDocument(); var world = new MapEntity("worldspawn"); world.Properties.Add("mapversion", "220");
            // WAD paths in MAP use forward slashes. MAP strings are ASCII in the supported toolchain.
            world.Properties.Add("wad", (fromRevit ? "generated_materials.wad" : Path.GetFullPath(options.WadPath)).Replace('\\', '/')); map.Entities.Add(world);
            var floors = new List<GoldSrcBrush>(); int converted = 0;var decorative=new Dictionary<string,MapEntity>();
            foreach (var element in candidates)
            {
                progress?.Invoke("Convirtiendo elemento " + element.Id);
                try
                {
                    if (element.Solids.Count + element.Prisms.Count + element.ConvexSolids.Count == 0) throw new ArgumentException("Elemento sin sólidos.");
                    if (fromRevit && element.MaterialSurfaces.Count == 0 && element.ConvexSolids.All(s=>s.Faces.All(f=>f.Material==null))) report.Diagnostics.Add(new Diagnostic(element.Id, "Sin superficies de material disponibles: se usó gris neutro.") { Code = "missing_face_material" });
                    var brushes = element.Solids.Select(b =>
                    {
                        var brush = BrushBuilder.FromBox(Coordinates.Convert(b, origin, options.UnitsPerFoot), materials?.NeutralName ?? Texture(element), t);
                        return materials == null ? brush : materials.Apply(brush, element, origin, options.UnitsPerFoot, options.ToleranceFeet);
                    }).ToList();
                    brushes.AddRange(element.Prisms.Select(p=>{
                        var brush=BrushBuilder.FromPrism(p.Convert(origin,options.UnitsPerFoot),materials?.NeutralName??Texture(element),t);
                        return materials==null?brush:materials.Apply(brush,element,origin,options.UnitsPerFoot,options.ToleranceFeet);
                    }));
                    brushes.AddRange(element.ConvexSolids.Select(s=>{
                        var brush=BrushBuilder.FromConvex(s.Convert(origin,options.UnitsPerFoot),materials?.NeutralName??Texture(element),t);
                        return materials==null?brush:materials.Apply(brush,element,origin,options.UnitsPerFoot,options.ToleranceFeet);
                    }));
                    if(element.Collision){world.Brushes.AddRange(brushes);if(element.Category==BimCategory.Floor)floors.AddRange(brushes);}
                    else{
                        string key=element.Origin.Document+"|"+element.CategoryKey;
                        if(!decorative.TryGetValue(key,out var entity)){entity=new MapEntity("func_illusionary");entity.Properties["skin"]="-1";decorative.Add(key,entity);map.Entities.Add(entity);}
                        entity.Brushes.AddRange(brushes);
                    }
                    converted++;
                    report.Elements.Add(new ElementExportInfo{Origin=element.Origin,Category=element.CategoryName.Length>0?element.CategoryName:element.Category.ToString(),Family=element.FamilyName,Type=element.TypeName,Status="Converted",Collision=element.Collision,Quality=element.Quality?.ToString()??"Exact",VolumeError=element.VolumeError,SurfaceDeviationFeet=element.SurfaceDeviationFeet,Brushes=brushes.Count});
                }
                catch (ArgumentException ex) { report.Diagnostics.Add(new Diagnostic(element.Id, ex.Message) { Code = "brush_conversion_failed", Category = element.Category.ToString(), Geometry = element.SourceGeometry?.ToList() }); }
            }
            if (doors.Count > 0)
            {
                if (!fromRevit) wad!.Require(options.Doors.Texture);
                report.ElementsFound += doors.Count;
                converted += DoorEntityBuilder.Add(map, doors, candidates.Where(e=>e.Collision).SelectMany(e => e.Solids).ToList(), (uncertainGeometryFeet ?? Array.Empty<Box>()).Concat(candidates.Where(e=>e.Collision).SelectMany(e=>e.Prisms.Select(p=>DoorMotion.Bounds(p.Vertices)).Concat(e.ConvexSolids.Select(s=>DoorMotion.Bounds(s.Vertices))))).ToList(), options, origin, report,
                    (door,leaf) => {
                        if(materials!=null&&leaf.Materials.Count==0&&!report.Diagnostics.Any(d=>d.ElementId==door.Id&&d.Code=="missing_door_material"))
                            report.Diagnostics.Add(new Diagnostic(door.Id,"Sin superficies de material identificables en la hoja: se usó gris neutro."){Code="missing_door_material"});
                        var brush = BrushBuilder.FromBox(Coordinates.Convert(leaf.GeometryFeet,origin,options.UnitsPerFoot),materials?.NeutralName ?? options.Doors.Texture,t);
                        return materials == null ? brush : materials.Apply(brush,new BimElement(door.Id,BimCategory.Wall,new[]{leaf.GeometryFeet},materialSurfaces:leaf.Materials,materialFrame:door.Frame),origin,options.UnitsPerFoot,options.ToleranceFeet);
                    });
            }
            if (world.Brushes.Count == 0 && !options.TestEnclosure) throw new ArgumentException(decorative.Count>0?"La geometría sin colisión requiere arquitectura sólida o un recinto de prueba para formar worldspawn.":"Todos los elementos fueron omitidos.");
            if (options.TestEnclosure)
            {
                var points = map.Entities.SelectMany(e=>e.Brushes).Where(b=>b.Role==BrushRole.Solid).SelectMany(b => ConvexGeometryValidator.Validate(b, t).Vertices).Concat(map.DoorMotionBoundsHU.SelectMany(b=>b.Vertices)).ToList();
                var enclosure = EntityBuilder.Enclosure(points, materials?.NeutralName ?? options.EnclosureTexture, t); world.Brushes.AddRange(enclosure); floors.Add(enclosure[0]);
            }
            if (options.GenerateEntities)
            {
                progress?.Invoke("Buscando un punto transitable..."); var spawn = EntityBuilder.SearchSpawn(floors, map.Entities.Where(e=>e.Properties["classname"]!="func_illusionary").SelectMany(e=>e.Brushes).Where(b=>b.Role==BrushRole.Solid).ToList(), t);
                if (spawn.Position.HasValue) EntityBuilder.AddBasicEntities(map, spawn.Position.Value);
                else report.Diagnostics.Add(new Diagnostic(null, spawn.Message + " No se generaron spawn ni luz.") { Code = spawn.Code });
            }
            report.ElementsConverted = converted; report.ElementsSkipped = report.ElementsFound - converted;
            report.Brushes = map.Entities.Sum(e=>e.Brushes.Count); report.Faces = map.Entities.SelectMany(e=>e.Brushes).Sum(b => b.Faces.Count); report.Entities = map.Entities.Count;
            report.UnitsPerFoot = options.UnitsPerFoot; report.ToleranceFeet = options.ToleranceFeet; report.OriginFeet = origin.ToString();
            progress?.Invoke("Validando Valve 220..."); string text = MapSerializer.Serialize(map, t); var parsed = MapValidator.Parse(text, t);
            if (parsed.Entities.Count != report.Entities || parsed.Entities.Sum(e => e.Brushes.Count) != report.Brushes) throw new InvalidDataException("El archivo releído no coincide con el resultado.");
            byte[]? generatedWad = null;
            if (materials != null)
            {
                progress?.Invoke("Generando y validando WAD de materiales...");
                var usedNames = map.Entities.SelectMany(e=>e.Brushes).Where(b=>b.Role==BrushRole.Solid).SelectMany(b => b.Faces).Select(f => f.Texture).ToList();
                generatedWad = WadWriter.Write(materials.Textures.Where(texture => usedNames.Contains(texture.Name)).ToList());
                using (var stream = new MemoryStream(generatedWad))
                {
                    var catalog = WadCatalog.Read(stream);
                    foreach (var texture in map.Entities.SelectMany(e=>e.Brushes).Where(b=>b.Role==BrushRole.Solid).SelectMany(b => b.Faces).Select(f => f.Texture).Distinct()) catalog.Require(texture);
                }
                materials.CompleteReport(map);
            }
            report.ElapsedSeconds += timer.Elapsed.TotalSeconds; return new ExportResult(map, report, text, generatedWad) { GeneratedToolsWad = map.Entities.SelectMany(e=>e.Brushes).Any(b=>b.Role!=BrushRole.Solid) ? ToolBrushes.CreateWad() : null };
        }
        public static void Publish(ExportResult result, string mapPath)
        {
            if (!string.Equals(Path.GetExtension(mapPath), ".map", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("La salida debe terminar en .map.");
            string target = Path.GetFullPath(mapPath), dir = Path.GetDirectoryName(target)!;
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException(dir);
            var targets = new List<string>(); var temporary = new List<string>(); var backups = new List<string>();
            bool success = false, previousPublished = result.Report.MapPublished;
            string previousWad=result.Map.Entities[0].Properties["wad"], previousText=result.Text;
            string? previousToolsPath=result.Report.ToolsWadPath, previousMaterialsPath=result.Report.GeneratedWadPath;
            string token = Guid.NewGuid().ToString("N"); int installed = 0;
            result.Report.MapPath = target;
            void Stage(string path) { targets.Add(path); temporary.Add(path + "." + token + ".tmp"); backups.Add(path + "." + token + ".bak"); }
            try
            {
                if (result.GeneratedWad != null)
                {
                    string wadPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(target) + "_materiales.wad");
                    result.Map.Entities[0].Properties["wad"] = wadPath.Replace('\\', '/');
                    result.Report.GeneratedWadPath = wadPath;
                    result.Text = MapSerializer.Serialize(result.Map, result.Report.ToleranceFeet * result.Report.UnitsPerFoot);
                    Stage(wadPath); File.WriteAllBytes(temporary.Last(), result.GeneratedWad);
                    var catalog = WadCatalog.Read(temporary.Last());
                    foreach (var face in result.Map.Entities.SelectMany(e => e.Brushes).Where(b=>b.Role==BrushRole.Solid).SelectMany(b => b.Faces)) catalog.Require(face.Texture);
                }
                if(result.GeneratedToolsWad != null)
                {
                    string toolsPath=Path.Combine(dir,Path.GetFileNameWithoutExtension(target)+"_tools.wad");
                    result.Report.ToolsWadPath=toolsPath;
                    result.Map.Entities[0].Properties["wad"] = string.Join(";",result.Map.Entities[0].Properties["wad"].Split(';').Where(p=>!string.Equals(p,previousToolsPath?.Replace('\\','/'),StringComparison.OrdinalIgnoreCase)).Concat(new[]{toolsPath.Replace('\\','/')}));
                    Stage(toolsPath);File.WriteAllBytes(temporary.Last(),result.GeneratedToolsWad);
                    var tools=WadCatalog.Read(temporary.Last());
                    if(!tools.Textures.Contains("ORIGIN")||!tools.Textures.Contains("AAATRIGGER"))throw new InvalidDataException("WAD técnico incompleto.");
                    result.Text=MapSerializer.Serialize(result.Map,result.Report.ToleranceFeet*result.Report.UnitsPerFoot);
                }
                result.Text=MapSerializer.Serialize(result.Map,result.Report.ToleranceFeet*result.Report.UnitsPerFoot);
                Stage(target); File.WriteAllText(temporary.Last(), result.Text, new UTF8Encoding(false));
                MapValidator.Parse(File.ReadAllText(temporary.Last()), result.Report.ToleranceFeet * result.Report.UnitsPerFoot);
                Stage(Path.Combine(dir, Path.GetFileNameWithoutExtension(target) + "_export_report.json"));
                result.Report.MapPublished = true;
                using (var stream = File.Create(temporary.Last())) new DataContractJsonSerializer(typeof(ExportReport)).WriteObject(stream, result.Report);
                for (int i = 0; i < targets.Count; i++) { Install(temporary[i], targets[i], backups[i]); installed++; }
                success = true;
            }
            catch (Exception failure)
            {
                result.Report.MapPublished = previousPublished;
                result.Map.Entities[0].Properties["wad"]=previousWad;result.Text=previousText;result.Report.ToolsWadPath=previousToolsPath;result.Report.GeneratedWadPath=previousMaterialsPath;
                var errors = new List<Exception> { failure };
                for (int i = installed - 1; i >= 0; i--) try { Restore(targets[i], backups[i]); } catch (Exception rollback) { errors.Add(rollback); }
                if (errors.Count > 1) throw new AggregateException("Falló la publicación y parte de la restauración. Se conservaron los backups disponibles.", errors);
                throw;
            }
            finally
            {
                foreach (string path in temporary) if (File.Exists(path)) File.Delete(path);
                if (success) foreach (string path in backups) if (File.Exists(path)) File.Delete(path);
            }
        }
        public static void PublishDiagnostics(ExportReport report, string intendedMapPath)
        {
            if (!string.Equals(Path.GetExtension(intendedMapPath), ".map", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("La salida debe terminar en .map.");
            report.MapPath = Path.GetFullPath(intendedMapPath);
            report.MapPublished = false;
            string target = Path.Combine(Path.GetDirectoryName(report.MapPath)!, Path.GetFileNameWithoutExtension(report.MapPath) + "_export_report.json");
            string token = Guid.NewGuid().ToString("N"), temp = target + "." + token + ".tmp", backup = target + "." + token + ".bak";
            bool installed = false;
            try
            {
                using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(ExportReport)).WriteObject(stream, report);
                Install(temp, target, backup); installed = true;
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
                if (installed && File.Exists(backup)) File.Delete(backup);
            }
        }
        private static void Install(string temp, string target, string backup)
        { if (File.Exists(target)) File.Replace(temp, target, backup); else File.Move(temp, target); }
        private static void Restore(string target, string backup)
        { if (File.Exists(backup)) File.Replace(backup, target, null); else File.Delete(target); }
    }
}
