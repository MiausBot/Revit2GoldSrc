using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Revit2Source
{
    public static class SourcePublication
    {
        public static byte[] Json<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return stream.ToArray();
            }
        }

        public static T ReadJson<T>(string file)
        {
            using (var stream = File.OpenRead(file))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream)!;
        }

        public static void Publish(SourceExportResult result, string vmfPath)
        {
            vmfPath = Path.GetFullPath(vmfPath);
            if (Path.GetExtension(vmfPath).ToLowerInvariant() != ".vmf")
                throw new ArgumentException("Seleccione VMF.");
            string name = Path.GetFileNameWithoutExtension(vmfPath);
            string folder = Path.GetDirectoryName(vmfPath)!;
            foreach (var material in result.Map.World.Solids.Concat(result.Map.Entities.SelectMany(e => e.Solids)).SelectMany(s => s.Sides).Select(s => s.Material).Distinct())
            {
                if (!material.StartsWith("revit2source/", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var extension in new[]
                {
                    ".vmt",
                    ".vtf"
                }

                )
                    if (!result.Assets.ContainsKey("materials/" + material + extension))
                        throw new ArgumentException("Falta recurso de material: " + material + extension);
            }

            var files = result.Assets.ToDictionary(p => p.Key, p => p.Value);
            files[name + ".vmf"] = Encoding.UTF8.GetBytes(Vmf.Write(result.Map));
            Vmf.Read(Encoding.UTF8.GetString(files[name + ".vmf"]));
            foreach (var asset in files.Where(p => p.Key.EndsWith(".vtf")))
                VtfWriter.Validate(asset.Value);
            result.Report.Published = true;
            files[name + "_source_report.json"] = Json(result.Report);
            files[name + "_source_config.json"] = Json(result.Configuration);
            foreach (var file in Navigation.Files(name))
                files.Add(file.Key, Encoding.UTF8.GetBytes(file.Value));
            try
            {
                AtomicFiles(folder, files);
            }
            catch
            {
                result.Report.Published = false;
                throw;
            }
        }

        public static void AtomicFiles(string folder, IReadOnlyDictionary<string, byte[]> files, Action<int>? checkpoint = null)
        {
            string root = Path.GetFullPath(folder);
            Directory.CreateDirectory(root);
            string staging = Path.Combine(root, ".source-publish-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var previous = new Dictionary<string, string?>();
            var published = new List<string>();
            try
            {
                foreach (var file in files)
                {
                    string destination = Path.GetFullPath(Path.Combine(root, file.Key.Replace('/', Path.DirectorySeparatorChar)));
                    if (!destination.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("Ruta fuera del destino.");
                    string stage = Path.Combine(staging, Guid.NewGuid().ToString("N"));
                    File.WriteAllBytes(stage, file.Value);
                    string? backup = null;
                    if (File.Exists(destination))
                    {
                        backup = Path.Combine(staging, Guid.NewGuid().ToString("N"));
                        File.Copy(destination, backup);
                    }

                    previous.Add(destination, backup);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (File.Exists(destination))
                        File.Replace(stage, destination, null);
                    else
                        File.Move(stage, destination);
                    published.Add(destination);
                    checkpoint?.Invoke(published.Count);
                }
            }
            catch
            {
                foreach (var path in published.AsEnumerable().Reverse())
                {
                    string? old = previous[path];
                    if (old != null)
                        File.Copy(old, path, true);
                    else
                        File.Delete(path);
                }

                throw;
            }
            finally
            {
                Directory.Delete(staging, true);
            }
        }
    }

    public static class Navigation
    {
        public static Dictionary<string, string> Files(string name)
        {
            return new Dictionary<string, string>
            {
                [name + "_nav_readme.txt"] = "L4D2: prueba local, no campaña completa.\r\nCopie manualmente el BSP a left4dead2/maps y los materiales a left4dead2/materials, o use un directorio de contenido montado.\r\nEn consola: map " + name + " coop\r\nsv_cheats 1\r\nnav_edit 1\r\nnav_mark_walkable sobre superficies caminables; después nav_generate. Puede recargar el nivel.\r\nRevise conexiones, escaleras, paso de puertas y áreas huérfanas.\r\nnav_analyze\r\nnav_trouble_report\r\nnav_save\r\nLa habitación no define flujo completo de campaña. Las anotaciones PLAYER_START/CHECKPOINT, zonas de aparición y flujo requieren un recorrido inicial/final diseñado y revisión manual.\r\nRepita navegación tras modificar arquitectura. Pruebe humanos, bots, apertura, colisiones y Director.\r\nNo se declara funcionamiento de infectados ni Director antes de esta validación. No redistribuya modelos/texturas de Valve.\r\n",
                ["cfg/" + name + "_nav_review.cfg"] = "// Ejecutar manualmente dentro del juego después de cargar el mapa.\r\nsv_cheats 1\r\nnav_edit 1\r\nnav_trouble_report\r\n// nav_generate, nav_analyze y nav_save se ejecutan por separado, después de revisar.\r\n"
            };
        }
    }
}
