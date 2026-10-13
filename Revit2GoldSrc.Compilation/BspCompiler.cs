using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Revit2GoldSrc.GoldSrc;
namespace Revit2GoldSrc.Compilation
{
    public sealed class CompilerOptions
    {
        public string Csg { get; set; } = ""; public string Bsp { get; set; } = ""; public string Vis { get; set; } = ""; public string Rad { get; set; } = "";
        public static CompilerOptions Detect(string? folder = null)
        { folder = folder ?? @"C:\Program Files\J.A.C.K\halflife"; return new CompilerOptions { Csg = Path.Combine(folder, "hlcsg.exe"), Bsp = Path.Combine(folder, "hlbsp.exe"), Vis = Path.Combine(folder, "hlvis.exe"), Rad = Path.Combine(folder, "hlrad.exe") }; }
        public void Validate() { foreach (var path in new[] { Csg, Bsp, Vis, Rad }) if (!File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Compilador ausente o inválido: " + path); }
    }
    [DataContract]
    public sealed class StageResult
    { [DataMember] public string Stage { get; set; } = ""; [DataMember] public string Executable { get; set; } = ""; [DataMember] public int ExitCode { get; set; } [DataMember] public double Seconds { get; set; } [DataMember] public string LogPath { get; set; } = ""; }
    [DataContract]
    public sealed class CompilationResult
    { [DataMember] public string Workspace { get; set; } = ""; [DataMember] public string BspPath { get; set; } = ""; [DataMember] public bool Published { get; set; } [DataMember] public string Error { get; set; } = ""; [DataMember] public List<StageResult> Stages { get; set; } = new List<StageResult>(); }
    public static class BspCompiler
    {
        // ProcessStartInfo.ArgumentList does not exist in .NET Framework 4.8: quote each Windows argv separately.
        public static string QuoteArgument(string value)
        {
            if (value.IndexOf('\0') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0) throw new ArgumentException("Argumento inválido.");
            return "\"" + Regex.Replace(value, @"(\\*)\""", "$1$1\\\"").TrimEnd('\\') + new string('\\', value.Reverse().TakeWhile(c => c == '\\').Count() * 2) + "\"";
        }
        public static Task<CompilationResult> CompileAsync(string mapPath, CompilerOptions options, IProgress<string>? progress = null, CancellationToken cancellation = default)
            => Task.Run(() => Compile(mapPath, options, progress, cancellation), cancellation);
        private static CompilationResult Compile(string mapPath, CompilerOptions options, IProgress<string>? progress, CancellationToken cancellation)
        {
            options.Validate(); mapPath = Path.GetFullPath(mapPath); if (!File.Exists(mapPath) || !mapPath.EndsWith(".map", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Seleccione un MAP existente.");
            var parsed = MapValidator.Parse(File.ReadAllText(mapPath), .0001);
            string folder = Path.GetDirectoryName(mapPath)!; string workspace = Path.Combine(folder, ".Revit2GoldSrc-build", DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(workspace);
            var result = new CompilationResult { Workspace = workspace }; string workingMap = Path.Combine(workspace, "map.map"), basename = Path.Combine(workspace, "map");
            try
            {
                // Copy WADs into the isolated build so compiler inputs are stable for all four stages.
                var wadPaths = new List<string>(); int number = 0;
                foreach (string raw in parsed.Entities[0].Properties["wad"].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string source = Path.IsPathRooted(raw) ? raw : Path.Combine(folder, raw); if (!File.Exists(source)) throw new FileNotFoundException("Falta WAD de compilación.", source);
                    string copy = Path.Combine(workspace, "textures" + (++number) + ".wad"); File.Copy(source, copy); wadPaths.Add(copy.Replace('\\', '/'));
                }
                parsed.Entities[0].Properties["wad"] = string.Join(";", wadPaths); File.WriteAllText(workingMap, MapSerializer.Serialize(parsed, .0001), new UTF8Encoding(false));
                string lights = Path.Combine(Path.GetDirectoryName(options.Rad)!, "lights.rad"); if (File.Exists(lights)) File.Copy(lights, Path.Combine(workspace, "lights.rad"));
                var names = new[] { "CSG", "BSP", "VIS", "RAD" }; var executables = new[] { options.Csg, options.Bsp, options.Vis, options.Rad };
                for (int i = 0; i < 4; i++)
                {
                    cancellation.ThrowIfCancellationRequested(); progress?.Report(names[i] + "…"); var stage = new StageResult { Stage = names[i], Executable = Path.GetFullPath(executables[i]), LogPath = Path.Combine(workspace, names[i] + ".log") }; result.Stages.Add(stage);
                    string[] arguments = i == 0 ? new[] { "-threads", "1", "-wadinclude", "textures1.wad", basename } : new[] { "-threads", "1", basename };
                    // Every used WAD is embedded; no game installation or redistributable resources required.
                    if (i == 0) arguments = new[] { "-threads", "1" }.Concat(wadPaths.SelectMany(p => new[] { "-wadinclude", Path.GetFileName(p) })).Concat(new[] { basename }).ToArray();
                    var output = new StringBuilder(); var gate = new object(); var timer = Stopwatch.StartNew();
                    using (var process = new Process())
                    {
                        process.StartInfo = new ProcessStartInfo(stage.Executable, string.Join(" ", arguments.Select(QuoteArgument))) { WorkingDirectory = workspace, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                        process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (gate) output.AppendLine(e.Data); }; process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (gate) output.AppendLine(e.Data); };
                        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                        try { while (!process.WaitForExit(100)) { if (cancellation.IsCancellationRequested) { process.Kill(); process.WaitForExit(); cancellation.ThrowIfCancellationRequested(); } } process.WaitForExit(); stage.ExitCode = process.ExitCode; }
                        finally { stage.Seconds = timer.Elapsed.TotalSeconds; lock (gate) File.WriteAllText(stage.LogPath, output.ToString(), new UTF8Encoding(false)); }
                    }
                    if (stage.ExitCode != 0 || Regex.IsMatch(output.ToString(), @"(?im)^\s*(?:Error:|.*fatal error|.*detected LEAK)")) throw new InvalidOperationException(names[i] + " falló; consulte " + stage.LogPath);
                }
                cancellation.ThrowIfCancellationRequested(); string built = basename + ".bsp"; BspInspector.Read(built);
                string target = Path.ChangeExtension(mapPath, ".bsp"), temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.Copy(built, temp); try { if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target); } finally { if (File.Exists(temp)) File.Delete(temp); }
                result.BspPath = target; result.Published = true; progress?.Report("BSP publicado: " + target); return result;
            }
            catch (Exception ex) { result.Error = ex.Message; throw; }
            finally { using (var stream = File.Create(Path.Combine(workspace, "compilation_report.json"))) new DataContractJsonSerializer(typeof(CompilationResult)).WriteObject(stream, result); }
        }
    }
    public sealed class BspInspection
    { public string Entities { get; internal set; } = ""; public int Models { get; internal set; } public int Version { get; internal set; } }
    public static class BspInspector
    {
        public static BspInspection Read(string path)
        {
            using (var file = File.OpenRead(path)) using (var reader = new BinaryReader(file))
            {
                if (file.Length < 124 || reader.ReadInt32() != 30) throw new InvalidDataException("El BSP no es GoldSrc versión 30.");
                var offsets = new int[15]; var lengths = new int[15]; for (int i = 0; i < 15; i++) { offsets[i] = reader.ReadInt32(); lengths[i] = reader.ReadInt32(); if (offsets[i] < 0 || lengths[i] < 0 || (long)offsets[i] + lengths[i] > file.Length) throw new InvalidDataException("Lump BSP inválido."); }
                if (lengths[14] < 64 || lengths[14] % 64 != 0 || lengths[0] < 2) throw new InvalidDataException("BSP sin modelos o entidades válidos.");
                file.Position = offsets[0]; var entities = Encoding.ASCII.GetString(reader.ReadBytes(lengths[0])).TrimEnd('\0'); if (!entities.Contains("\"worldspawn\"")) throw new InvalidDataException("BSP sin worldspawn.");
                int models = lengths[14] / 64; foreach (Match match in Regex.Matches(entities, "\\\"model\\\"\\s+\\\"\\*(\\d+)\\\"")) if (int.Parse(match.Groups[1].Value) >= models) throw new InvalidDataException("Entidad BSP con modelo inexistente.");
                file.Position = offsets[14]; for (int i = 0; i < models; i++) { var mins = new[] { reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() }; var maxs = new[] { reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() }; for (int axis = 0; axis < 3; axis++) if (float.IsNaN(mins[axis]) || float.IsInfinity(mins[axis]) || float.IsNaN(maxs[axis]) || float.IsInfinity(maxs[axis]) || mins[axis] >= maxs[axis]) throw new InvalidDataException("Modelo BSP degenerado."); file.Position += 40; }
                return new BspInspection { Entities = entities, Models = models, Version = 30 };
            }
        }
    }
}
