using System;
using System.IO;
using System.Linq;
using Revit2Source;
using Revit2Source.Compilation;

namespace Revit2Source.Sample
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length < 2)
                    throw new ArgumentException("Uso: --sample salida.vmf [--compile] | --compile archivo.vmf");
                var tools = SourceToolchain.Detect();
                if (args[0] == "--checkpoint-bounds")
                {
                    var bounds = new GameAssets(tools.GameRoot).DoorBounds(args[1]) ?? throw new ArgumentException("Modelo no encontrado.");
                    Console.WriteLine("Min: " + bounds.Min + "; Max: " + bounds.Max);
                    Console.WriteLine("Ancho/alto a 12 unidades por pie: " + Math.Max(bounds.Max.X - bounds.Min.X, bounds.Max.Y - bounds.Min.Y) / 12 + " / " + (bounds.Max.Z - bounds.Min.Z) / 12 + " pies");
                    return 0;
                }
                if (args[0] == "--sample")
                {
                    var fgd = File.Exists(tools.Fgd) ? FgdCatalog.Read(tools.Fgd) : null;
                    var scene = SourceSample.Room(fgd);
                    if (!scene.Map.Entities.Any(e => e.Properties["classname"] == "func_door_rotating"))
                        throw new InvalidOperationException(string.Join("; ", scene.Report.Diagnostics.Select(d => d.Message)));
                    SourcePublication.Publish(scene, args[1]);
                    Console.WriteLine("VMF validado: " + Path.GetFullPath(args[1]) + "; " + scene.Report.Solids + " sólidos, " + scene.Report.Entities + " entidades, FGD=" + scene.Report.FgdValidated);
                }

                if (args[0] == "--compile" || args.Contains("--compile"))
                {
                    var result = SourceCompiler.CompileAsync(args[1], tools, new Progress<string>(Console.WriteLine)).GetAwaiter().GetResult();
                    var bsp = SourceBsp.Inspect(File.ReadAllBytes(result.BspPath));
                    Console.WriteLine("BSP L4D2: " + result.BspPath + "; " + bsp.Models + " modelos; navegación y prueba en juego pendientes.");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }
    }
}
