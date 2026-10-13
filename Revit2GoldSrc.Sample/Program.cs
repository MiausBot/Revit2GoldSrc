using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Runtime.Serialization.Json;
using Revit2GoldSrc.Core;
using Revit2GoldSrc.GoldSrc;
namespace Revit2GoldSrc.Sample
{
    public static class Program
    {
        public static List<BimElement> Room()
        {
            double Feet(double m) => m / .3048;
            Box B(double x, double y, double z, double dx, double dy, double dz) => new Box(Frame.Identity, new Vec3(Feet(x), Feet(y), Feet(z)), new Vec3(Feet(x + dx), Feet(y + dy), Feet(z + dz)));
            // Interior 4 x 5 m, 3 m high, with a 0.90 x 2.10 m doorway.
            return new List<BimElement>{
    new BimElement(1,BimCategory.Floor,new[]{B(-.15,-.15,-.2,4.3,5.3,.2)}),
    new BimElement(2,BimCategory.Wall,new[]{B(-.15,-.15,0,.15,5.3,3)}),
    new BimElement(3,BimCategory.Wall,new[]{B(4,-.15,0,.15,5.3,3)}),
    new BimElement(4,BimCategory.Wall,new[]{B(0,5,0,4,.15,3)}),
    new BimElement(5,BimCategory.Wall,new[]{B(0,-.15,0,1.55,.15,3),B(2.45,-.15,0,1.55,.15,3),B(1.55,-.15,2.1,.9,.15,.9)})
   };
        }
        private static List<BimElement> MaterialRoom()
        {
            var floor = new BimMaterial { Id = 101, Name = "Baldosas de prueba", WidthFeet = 1.2 / .3048, HeightFeet = 1.2 / .3048 };
            var rgba = new byte[64 * 64 * 4];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                int p = (y * 64 + x) * 4; bool grout = x % 16 == 0 || y % 16 == 0; byte value = (byte)(grout ? 90 : 220);
                rgba[p] = value; rgba[p + 1] = value; rgba[p + 2] = (byte)(grout ? 90 : 235); rgba[p + 3] = 255;
            }
            floor.Image = new PixelImage(64, 64, rgba);
            var wall = new BimMaterial { Id = 102, Name = "Pintura beige de prueba", Color = new RgbColor(210, 190, 160) };
            return Room().Select(element =>
            {
                var surfaces = new List<MaterialSurface>();
                foreach (var box in element.Solids)
                {
                    var brush = BrushBuilder.FromBox(box, "TEST", 1e-5); var vertices = ConvexGeometryValidator.Validate(brush, 1e-5).Vertices;
                    foreach (var face in brush.Faces)
                    {
                        var points = vertices.Where(v => Math.Abs(face.Normal.Dot(v) - face.Distance) < 1e-5).ToList(); var center = points.Aggregate(Vec3.Zero, (sum, v) => sum + v) / points.Count;
                        var u = (points[0] - center).Unit(); var v = face.Normal.Cross(u);
                        var loop = points.OrderBy(p => Math.Atan2((p - center).Dot(v), (p - center).Dot(u))).ToList();
                        surfaces.Add(new MaterialSurface(face.Normal, face.Distance, new[] { (IReadOnlyList<Vec3>)loop }, element.Category == BimCategory.Floor ? floor : wall));
                    }
                }
                return new BimElement(element.Id, element.Category, element.Solids, materialSurfaces: surfaces, materialFrame: Frame.Identity);
            }).ToList();
        }
        public static ExportResult DoorScene(string outputPath)
        {
            Box B(double x,double y,double z,double dx,double dy,double dz)=>new Box(Frame.Identity,new Vec3(x,y,z),new Vec3(x+dx,y+dy,z+dz));
            var wall=new BimElement(2,BimCategory.Wall,new[]{B(-272,-208,0,16,416,128),B(256,-208,0,16,416,128),B(-256,-208,0,512,16,128),B(-256,192,0,512,16,128),B(-256,-192,128,512,384,16),B(-256,-4,0,172,8,128),B(-44,-4,0,104,8,128),B(100,-4,0,156,8,128),B(-84,-4,80,40,8,48),B(60,-4,80,40,8,48)});
            var floor=new BimElement(1,BimCategory.Floor,new[]{B(-272,-208,-16,544,416,16)});
            var rotatingFrame=new Frame(new Vec3(-64,0,0),Vec3.XAxis,Vec3.YAxis,Vec3.ZAxis);
            var slidingFrame=new Frame(new Vec3(80,8,0),Vec3.XAxis,Vec3.YAxis,Vec3.ZAxis);
            var doors=new[]{new DoorDefinition(100,rotatingFrame,new DoorSettings{Animated=true,Hinge=HingeSide.Left},new[]{new DoorLeaf(new Box(rotatingFrame,new Vec3(-18,-1,0),new Vec3(18,1,80)))}),
                new DoorDefinition(101,slidingFrame,new DoorSettings{Animated=true,Kind=DoorKind.Sliding,SlideDirection=SlideDirection.Right,SlideDistanceFeet=40,Activation=DoorActivation.Touch},new[]{new DoorLeaf(new Box(slidingFrame,new Vec3(-18,-1,0),new Vec3(18,1,80)))})};
            string wadPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath))!,"doors_demo.wad");
            File.WriteAllBytes(wadPath,WadWriter.Write(new[]{TextureImageConverter.Convert(PixelImage.Solid(new RgbColor(180,150,110)),RgbColor.Neutral)}));
            string texture=WadCatalog.Read(wadPath).Textures.First();var options=new ExportOptions{UnitsPerFoot=1,CenterOrigin=false,WadPath=wadPath};foreach(BimCategory category in Enum.GetValues(typeof(BimCategory)))options.Textures[category]=texture;options.Doors.Enabled=true;options.Doors.Texture=texture;
            return Exporter.Prepare(new[]{floor,wall},options,new ExportReport{RevitVersion="independent C# door scene"},Console.WriteLine,doors);
        }
        public static ExportResult FamilyScene()
        {
            var red=new BimMaterial{Id=52,SourceDocument="sample-host",Name="Pipe red",Color=new RgbColor(180,40,30)};
            var blue=new BimMaterial{Id=52,SourceDocument="sample-link",Name="Linked pipe blue",Color=new RgbColor(30,80,190)};
            var data=new List<BimElement>{new BimElement(1,BimCategory.Floor,new[]{new Box(Frame.Identity,new Vec3(-32,-32,-.5),new Vec3(32,32,0))})};
            void Mesh(long id,TriangleMesh mesh,Func<Vec3,Vec3> transform,string category,string doc,bool collision=true)
            {
                var moved=new TriangleMesh(mesh.Triangles.Select(f=>new MeshFace(f.Points.Select(transform).ToList(),f.Material,f.Painted)).ToList());
                var parts=ConvexMeshDecomposer.Decompose(moved,1e-5);
                data.Add(new BimElement(id,BimCategory.Wall,Array.Empty<Box>(),convexSolids:parts){CategoryKey=category,CategoryName=category,FamilyName="C# sample",TypeName="verified closed mesh",Origin=new ElementOrigin{Document=doc,DocumentName=doc,LinkInstances=doc=="sample-link"?new List<long>{700}:new List<long>(),ElementId=id},Collision=collision});
            }
            Mesh(5000000000,MeshSamples.Tube(16,1,red),p=>new Vec3(p.Z-10,p.Y,p.X+5),"Pipes","sample-host");
            Mesh(5000000000,MeshSamples.Tube(32,0,blue),p=>DoorMotion.RotateVector(p,45)+new Vec3(12,10,0),"Pipes","sample-link");
            Mesh(3,MeshSamples.Elbow(8,4,red),p=>p+new Vec3(-10,8,8),"Pipe fittings","sample-host");
            var furniture=new BimElement(4,BimCategory.Wall,new[]{new Box(Frame.Identity,new Vec3(-8,-8,0),new Vec3(-4,-4,3))}){Collision=false,CategoryKey="Furniture",CategoryName="Furniture",Origin=new ElementOrigin{Document="sample-host",ElementId=4}};data.Add(furniture);
            var equipment=new BimElement(5,BimCategory.Wall,new[]{new Box(new Frame(new Vec3(6,-10,1),DoorMotion.RotateVector(Vec3.XAxis,45),DoorMotion.RotateVector(Vec3.YAxis,45),Vec3.ZAxis),new Vec3(-2,-2,-1),new Vec3(2,2,4))}){CategoryKey="Equipment",CategoryName="Equipment",FamilyName="C# equipment"};data.Add(equipment);
            return Exporter.Prepare(data,new ExportOptions{TextureSource=TextureSourceMode.RevitMaterials,TestEnclosure=true},new ExportReport{RevitVersion="independent C# mixed scene; link transforms simulated"},Console.WriteLine);
        }
        private static int Recover(string reportPath,string baselinePath,string outputPath)
        {
            if(File.Exists(outputPath))throw new ArgumentException("El destino de recuperación ya existe: elija otro nombre.");
            ExportReport source;using(var stream=File.OpenRead(reportPath))source=(ExportReport)new DataContractJsonSerializer(typeof(ExportReport)).ReadObject(stream)!;
            var baseline=MapValidator.Parse(File.ReadAllText(baselinePath),source.ToleranceFeet*source.UnitsPerFoot);
            var elements=source.Diagnostics.Where(d=>d.Code=="geometry_rejected"&&d.Geometry!=null).Select(d=>new BimElement(d.ElementId!.Value,(BimCategory)Enum.Parse(typeof(BimCategory),d.Category!),Array.Empty<Box>(),prisms:d.Geometry!.SelectMany(PlanarSectionDecomposer.Decompose).ToList())).ToList();
            var origin=source.OriginFeet.Split(' ').Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();
            var options=new ExportOptions{CenterOrigin=false,OriginFeet=new Vec3(origin[0],origin[1],origin[2]),UnitsPerFoot=source.UnitsPerFoot,ToleranceFeet=source.ToleranceFeet,WadPath=baseline.Entities[0].Properties["wad"].Split(';')[0],GenerateEntities=false};
            string neutral=WadCatalog.Read(options.WadPath).Textures.First();foreach(BimCategory category in Enum.GetValues(typeof(BimCategory)))options.Textures[category]=neutral;
            var report=new ExportReport{RevitVersion=source.RevitVersion+" (captured boundary replay; not a live Revit export)"};
            report.Diagnostics.Add(new Diagnostic(null,"Reproducción de contornos capturados: se recuperan los omitidos y se conservan los brushes previos. Los materiales de los elementos recuperados usan textura neutra porque el reporte no contiene sus superficies de material."){Code="captured_replay",Severity="info"});
            var result=Exporter.Prepare(elements,options,report,Console.WriteLine);
            result.Map.Entities[0].Brushes.AddRange(baseline.Entities[0].Brushes);
            result.Map.Entities.AddRange(baseline.Entities.Skip(1).Where(e=>e.Properties["classname"]!="info_player_start"&&e.Properties["classname"]!="light"));
            var floors=elements.Where(e=>e.Category==BimCategory.Floor).SelectMany(e=>e.Prisms).Select(p=>BrushBuilder.FromPrism(p.Convert(options.OriginFeet,options.UnitsPerFoot),neutral,options.ToleranceFeet*options.UnitsPerFoot)).ToList();
            var spawn=EntityBuilder.SearchSpawn(floors,result.Map.Entities.SelectMany(e=>e.Brushes).Where(b=>b.Role==BrushRole.Solid).ToList(),options.ToleranceFeet*options.UnitsPerFoot);
            if(spawn.Position.HasValue)EntityBuilder.AddBasicEntities(result.Map,spawn.Position.Value);else report.Diagnostics.Add(new Diagnostic(null,spawn.Message){Code=spawn.Code});
            report.ElementsFound=source.ElementsFound;report.ElementsConverted=source.ElementsConverted+elements.Count;report.ElementsSkipped=report.ElementsFound-report.ElementsConverted;
            void Counts(){report.Brushes=result.Map.Entities.Sum(e=>e.Brushes.Count);report.Faces=result.Map.Entities.SelectMany(e=>e.Brushes).Sum(b=>b.Faces.Count);report.Entities=result.Map.Entities.Count;}
            Counts();Exporter.Publish(result,outputPath);Console.WriteLine("Captura recuperada: "+report.ElementsConverted+"/"+report.ElementsFound+"; "+report.Brushes+" brushes.");
            // Compile a separate enclosed variant, preserving the recovered architecture MAP.
            string enclosed=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath))!,Path.GetFileNameWithoutExtension(outputPath)+"_recinto.map");if(File.Exists(enclosed))throw new ArgumentException("El recinto de prueba ya existe.");
            var points=result.Map.Entities.SelectMany(e=>e.Brushes).Where(b=>b.Role==BrushRole.Solid).SelectMany(b=>ConvexGeometryValidator.Validate(b,options.ToleranceFeet*options.UnitsPerFoot).Vertices).ToList();result.Map.Entities[0].Brushes.AddRange(EntityBuilder.Enclosure(points,neutral,options.ToleranceFeet*options.UnitsPerFoot));Counts();Exporter.Publish(result,enclosed);
            var compiled=Revit2GoldSrc.Compilation.BspCompiler.CompileAsync(enclosed,Revit2GoldSrc.Compilation.CompilerOptions.Detect(),new Progress<string>(Console.WriteLine)).GetAwaiter().GetResult();Console.WriteLine("Recinto BSP: "+compiled.BspPath);return 0;
        }
        public static int Main(string[] args)
        {
            if(args.Length==4&&args[0]=="--recover-report")
            {try{return Recover(args[1],args[2],args[3]);}catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}}
            if (args.Length != 2) { Console.Error.WriteLine("Uso: dotnet run --project Revit2GoldSrc.Sample -- <archivo.wad o --materials> <salida.map>"); return 2; }
            try
            {
                if(args[0]=="--families"||args[0]=="--compile-families")
                {
                    var mixedResult=FamilyScene();Exporter.Publish(mixedResult,args[1]);
                    if(args[0]=="--compile-families")
                    {
                        var built=Revit2GoldSrc.Compilation.BspCompiler.CompileAsync(args[1],Revit2GoldSrc.Compilation.CompilerOptions.Detect(),new Progress<string>(Console.WriteLine)).GetAwaiter().GetResult();
                        var inspection=Revit2GoldSrc.Compilation.BspInspector.Read(built.BspPath);if(inspection.Models!=2||!inspection.Entities.Contains("func_illusionary"))throw new InvalidOperationException("BSP no contiene el modelo decorativo.");Console.WriteLine("BSP30 mixto comprobado: "+inspection.Models+" modelos.");
                    }
                    return 0;
                }
                if(args[0]=="--doors"||args[0]=="--compile-doors")
                {
                    var doorResult=DoorScene(args[1]);if(doorResult.Report.Doors.Count(d=>d.Status=="Animated")!=2)throw new InvalidOperationException(string.Join(";",doorResult.Report.Diagnostics.Select(d=>d.Message)));
                    Exporter.Publish(doorResult,args[1]);
                    if(args[0]=="--compile-doors")
                    {
                        var built=Revit2GoldSrc.Compilation.BspCompiler.CompileAsync(args[1],Revit2GoldSrc.Compilation.CompilerOptions.Detect(),new Progress<string>(Console.WriteLine)).GetAwaiter().GetResult();
                        var bsp=Revit2GoldSrc.Compilation.BspInspector.Read(built.BspPath);if(bsp.Models!=3||!bsp.Entities.Contains("func_door_rotating")||!bsp.Entities.Contains("func_door"))throw new InvalidOperationException("BSP sin los modelos de ambas puertas.");Console.WriteLine("BSP30 comprobado: "+bsp.Models+" modelos; logs "+built.Workspace);
                    }
                    return 0;
                }
                if (args[0] == "--materials")
                {
                    var materials = MaterialRoom(); var materialReport = new ExportReport { RevitVersion = "independent material sample", ElementsFound = materials.Count };
                    var materialResult = Exporter.Prepare(materials, new ExportOptions { TextureSource = TextureSourceMode.RevitMaterials, TestEnclosure = true }, materialReport, Console.WriteLine);
                    Exporter.Publish(materialResult, args[1]); Console.WriteLine("MAP + WAD de materiales validados: " + Path.GetFullPath(args[1])); return 0;
                }
                var catalog = WadCatalog.Read(args[0]); string? texture = null;
                foreach (string n in catalog.Textures) { try { TextureMapper.ValidateSolidTexture(n); texture = n; break; } catch (ArgumentException) { } }
                if (texture == null) throw new ArgumentException("Sin textura sólida.");
                var options = new ExportOptions { WadPath = Path.GetFullPath(args[0]), TestEnclosure = true, EnclosureTexture = texture };
                foreach (BimCategory category in Enum.GetValues(typeof(BimCategory))) options.Textures.Add(category, texture);
                var data = Room(); var report = new ExportReport { RevitVersion = "independent sample", ElementsFound = data.Count };
                var result = Exporter.Prepare(data, options, report, Console.WriteLine); Exporter.Publish(result, args[1]);
                Console.WriteLine("MAP validado: " + Path.GetFullPath(args[1]) + " — " + report.Brushes + " brushes"); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }
    }
}
