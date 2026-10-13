using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Revit2Source;

namespace Revit2Source.Compilation
{
    public sealed class SourceToolchain
    {
        public string GameRoot { get; set; } = "";
        public string GameDirectory => Path.Combine(GameRoot, "left4dead2");
        public string Vbsp { get; set; } = "";
        public string Vvis { get; set; } = "";
        public string Vrad { get; set; } = "";
        public string Fgd { get; set; } = "";

        public static SourceToolchain Detect(string? root = null)
        {
            if (root == null)
            {
                var steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
                var libraries = new List<string>
                {
                    steam
                };
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                        libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
                root = libraries.Select(p => Path.Combine(p, "steamapps", "common", "Left 4 Dead 2")).FirstOrDefault(p => File.Exists(Path.Combine(p, "left4dead2", "gameinfo.txt"))) ?? "";
            }

            return new SourceToolchain
            {
                GameRoot = root,
                Vbsp = Path.Combine(root, "bin", "vbsp.exe"),
                Vvis = Path.Combine(root, "bin", "vvis.exe"),
                Vrad = Path.Combine(root, "bin", "vrad.exe"),
                Fgd = Path.Combine(root, "bin", "left4dead2.fgd")
            };
        }

        public void Validate()
        {
            if (!File.Exists(Path.Combine(GameDirectory, "gameinfo.txt")))
                throw new ArgumentException("Falta la instalación L4D2.");
            var info = KeyValues.Parse(File.ReadAllText(Path.Combine(GameDirectory, "gameinfo.txt"))).Single();
            if (info.Block("FileSystem")?.Get("SteamAppId") != "550")
                throw new ArgumentException("Gameinfo no corresponde a L4D2 (550).");
            foreach (var path in new[]
            {
                Vbsp,
                Vvis,
                Vrad,
                Fgd
            }

            )
                if (!File.Exists(path))
                    throw new FileNotFoundException("Faltan Left 4 Dead 2 Authoring Tools.", path);
            string bin = Path.GetFullPath(Path.Combine(GameRoot, "bin")) + Path.DirectorySeparatorChar;
            foreach (var path in new[]
            {
                Vbsp,
                Vvis,
                Vrad,
                Fgd
            }

            )
                if (!Path.GetFullPath(path).StartsWith(bin, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Use los compiladores/FGD de la misma instalación L4D2.");
        }
    }

    [DataContract]
    public sealed class SourceStage
    {
        [DataMember]
        public string Name { get; set; } = "";

        [DataMember]
        public string Executable { get; set; } = "";

        [DataMember]
        public int ExitCode { get; set; }

        [DataMember]
        public double Seconds { get; set; }

        [DataMember]
        public string Log { get; set; } = "";

        [DataMember]
        public List<string> Warnings { get; set; } = new List<string>();
    }

    [DataContract]
    public sealed class SourceCompilationResult
    {
        [DataMember]
        public string Workspace { get; set; } = "";

        [DataMember]
        public string BspPath { get; set; } = "";

        [DataMember]
        public bool Published { get; set; }

        [DataMember]
        public string Error { get; set; } = "";

        [DataMember]
        public List<SourceStage> Stages { get; set; } = new List<SourceStage>();
    }

    public static class SourceCompiler
    {
        public static string Quote(string argument)
        {
            if (argument.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
                throw new ArgumentException("Argumento inválido.");
            var output = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\')
                {
                    slashes++;
                    continue;
                }

                if (c == '"')
                {
                    output.Append('\\', slashes * 2 + 1);
                    output.Append(c);
                }
                else
                {
                    output.Append('\\', slashes);
                    output.Append(c);
                }

                slashes = 0;
            }

            output.Append('\\', slashes * 2);
            return output.Append('"').ToString();
        }

        public static string StagedGameInfo(SourceToolchain tools, string content)
        {
            var root = KeyValues.Parse(File.ReadAllText(Path.Combine(tools.GameDirectory, "gameinfo.txt"))).Single();
            var paths = root.Block("FileSystem")?.Block("SearchPaths") ?? throw new ArgumentException("Gameinfo sin SearchPaths.");
            foreach (var path in paths.Children)
            {
                if (path.Value == null)
                    continue;
                string value = path.Value.Replace("|gameinfo_path|", tools.GameDirectory.TrimEnd('\\', '/') + "/").Replace("|all_source_engine_paths|", tools.GameRoot.TrimEnd('\\', '/') + "/");
                if (!Path.IsPathRooted(value))
                    value = Path.Combine(tools.GameRoot, value);
                path.Value = Path.GetFullPath(value).Replace('\\', '/');
            }

            paths.Children.Insert(0, new KvNode("Game", Path.GetFullPath(content).Replace('\\', '/')));
            return KeyValues.Write(new[] { root });
        }

        public static Task<SourceCompilationResult> CompileAsync(string vmfPath, SourceToolchain tools, IProgress<string>? progress = null, CancellationToken cancellation = default)
        {
            return Task.Run(() => Compile(vmfPath, tools, progress, cancellation), cancellation);
        }

        private static SourceCompilationResult Compile(string vmfPath, SourceToolchain tools, IProgress<string>? progress, CancellationToken cancellation)
        {
            tools.Validate();
            vmfPath = Path.GetFullPath(vmfPath);
            if (!File.Exists(vmfPath) || Path.GetExtension(vmfPath).ToLowerInvariant() != ".vmf")
                throw new ArgumentException("Seleccione un VMF existente.");
            var doc = Vmf.Read(File.ReadAllText(vmfPath));
            FgdCatalog.Read(tools.Fgd).Validate(doc);
            string folder = Path.GetDirectoryName(vmfPath)!;
            string name = Path.GetFileNameWithoutExtension(vmfPath);
            string workspace = Path.Combine(folder, ".Revit2Source-build", DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            string content = Path.Combine(workspace, "content");
            Directory.CreateDirectory(content);
            var result = new SourceCompilationResult
            {
                Workspace = workspace
            };
            try
            {
                cancellation.ThrowIfCancellationRequested();
                File.WriteAllText(Path.Combine(content, "gameinfo.txt"), StagedGameInfo(tools, content), new UTF8Encoding(false));
                foreach (var material in doc.World.Solids.Concat(doc.Entities.SelectMany(e => e.Solids)).SelectMany(s => s.Sides).Select(s => s.Material).Distinct())
                {
                    if (!material.StartsWith("revit2source/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    SourceGeometry.ValidateMaterial(material);
                    foreach (var extension in new[]
                    {
                        ".vmt",
                        ".vtf"
                    }

                    )
                    {
                        string relative = "materials/" + material + extension;
                        string source = Path.Combine(folder, relative);
                        if (!File.Exists(source))
                            throw new FileNotFoundException("Material generado ausente.", source);
                        if (extension == ".vtf")
                            VtfWriter.Validate(File.ReadAllBytes(source));
                        string target = Path.Combine(content, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(source, target);
                    }
                }

                string working = Path.Combine(workspace, name + ".vmf");
                File.WriteAllText(working, Vmf.Write(doc), new UTF8Encoding(false));
                string bsp = Path.ChangeExtension(working, ".bsp");
                var stages = new[]
                {
                    Tuple.Create("VBSP", tools.Vbsp, working),
                    Tuple.Create("VVIS", tools.Vvis, bsp),
                    Tuple.Create("VRAD", tools.Vrad, bsp)
                };
                foreach (var stage in stages)
                {
                    cancellation.ThrowIfCancellationRequested();
                    progress?.Report(stage.Item1 + "…");
                    cancellation.ThrowIfCancellationRequested();
                    var r = new SourceStage
                    {
                        Name = stage.Item1,
                        Executable = stage.Item2,
                        Log = Path.Combine(workspace, stage.Item1 + ".log")
                    };
                    result.Stages.Add(r);
                    var args = new List<string>
                    {
                        "-game",
                        content,
                        "-threads",
                        "2"
                    };
                    if (stage.Item1 == "VRAD")
                        args.Add("-both");
                    args.Add(stage.Item3);
                    var output = new StringBuilder();
                    var gate = new object ();
                    var timer = Stopwatch.StartNew();
                    using (var process = new Process())
                    {
                        process.StartInfo = new ProcessStartInfo(stage.Item2, string.Join(" ", args.Select(Quote)))
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            WorkingDirectory = content
                        };
                        process.StartInfo.EnvironmentVariables["VPROJECT"] = content;
                        DataReceivedEventHandler receive = (s, e) =>
                        {
                            if (e.Data == null)
                                return;
                            lock (gate)
                                output.AppendLine(e.Data);
                        };
                        process.OutputDataReceived += receive;
                        process.ErrorDataReceived += receive;
                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        while (!process.WaitForExit(100))
                        {
                            if (cancellation.IsCancellationRequested)
                            {
                                process.Kill();
                                process.WaitForExit();
                                File.WriteAllText(r.Log, output.ToString());
                                cancellation.ThrowIfCancellationRequested();
                            }
                        }

                        process.WaitForExit();
                        r.ExitCode = process.ExitCode;
                        r.Seconds = timer.Elapsed.TotalSeconds;
                    }

                    string log = output.ToString();
                    File.WriteAllText(r.Log, log, new UTF8Encoding(false));
                    r.Warnings = log.Split('\n').Where(line => Regex.IsMatch(line, @"warning|couldn't|unable|missing|cannot", RegexOptions.IgnoreCase)).Select(line => line.Trim()).ToList();
                    if (r.ExitCode != 0 || Regex.IsMatch(log, @"\*+\s*leaked\s*\*+|(?m)^\s*(?:Error:|Error loading|FATAL|Can't load|Could not load|Unable to load|Too many)", RegexOptions.IgnoreCase) || File.Exists(Path.ChangeExtension(working, ".lin")))
                        throw new InvalidOperationException(stage.Item1 + " falló o produjo un leak. Consulte " + r.Log);
                    if (!File.Exists(bsp))
                        throw new InvalidOperationException("Compilador no produjo BSP.");
                }

                cancellation.ThrowIfCancellationRequested();
                SourceBsp.Inspect(File.ReadAllBytes(bsp));
                var published = new Dictionary<string, byte[]>
                {
                    [name + ".bsp"] = File.ReadAllBytes(bsp)
                };
                result.BspPath = Path.Combine(folder, name + ".bsp");
                result.Published = true;
                published[name + "_source_compile.json"] = SourcePublication.Json(result);
                SourcePublication.AtomicFiles(folder, published);
                return result;
            }
            catch (Exception ex)
            {
                result.Published = false;
                result.Error = ex.Message;
                throw;
            }
            finally
            {
                File.WriteAllBytes(Path.Combine(workspace, "compile_result.json"), SourcePublication.Json(result));
            }
        }
    }

    public sealed class SourceBspInfo
    {
        public int Version { get; set; }
        public int Models { get; set; }
        public string Entities { get; set; } = "";
    }

    public static class SourceBsp
    {
        public static SourceBspInfo Inspect(byte[] bytes)
        {
            const int header = 1036;
            if (bytes.Length < header || BitConverter.ToInt32(bytes, 0) != 0x50534256 || BitConverter.ToInt32(bytes, 4) != 21)
                throw new ArgumentException("Se requiere BSP Source L4D2 versión 21.");
            foreach (bool reordered in new[]
            {
                false,
                true
            }

            )
            {
                try
                {
                    int Offset(int lump) => BitConverter.ToInt32(bytes, 8 + lump * 16 + (reordered ? 4 : 0));
                    int Length(int lump) => BitConverter.ToInt32(bytes, 8 + lump * 16 + (reordered ? 8 : 4));
                    for (int i = 0; i < 64; i++)
                    {
                        int offset = Offset(i), length = Length(i);
                        if (length < 0 || offset < 0 || (long)offset + length > bytes.Length || (length > 0 && offset < header))
                            throw new ArgumentException("Lump fuera de límites.");
                    }

                    int eo = Offset(0), el = Length(0);
                    string entities = Encoding.UTF8.GetString(bytes, eo, el).TrimEnd('\0');
                    if (!entities.Contains("worldspawn") || Length(14) < 48 || Length(14) % 48 != 0)
                        throw new ArgumentException("Entidades o modelos ausentes.");
                    var entityBlocks = KeyValues.Parse(Regex.Replace(entities, @"(?m)^\s*\{", "entity\n{"));
                    if (entityBlocks.Count(n => n.Get("classname") == "info_survivor_position") != 4 || !entityBlocks.Any(n => n.Get("classname") == "info_director"))
                        throw new ArgumentException("Entidades L4D2 ausentes.");
                    if (Length(4) == 0 || Length(8) == 0 && Length(53) == 0)
                        throw new ArgumentException("BSP sin VIS o iluminación.");
                    return new SourceBspInfo
                    {
                        Version = 21,
                        Models = Length(14) / 48,
                        Entities = entities
                    };
                }
                catch (ArgumentException)
                {
                }
                catch (FormatException)
                {
                }
            }

            throw new ArgumentException("BSP L4D2 inválido o incompleto.");
        }
    }
}
