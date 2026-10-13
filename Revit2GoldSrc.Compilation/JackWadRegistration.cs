using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Revit2GoldSrc.GoldSrc;
namespace Revit2GoldSrc.Compilation
{
    public sealed class JackProfile
    {
        public string Section { get; }
        public string Title { get; }
        internal JackProfile(string section, string title) { Section = section; Title = title; }
        public override string ToString() => Title + " (" + Section + ")";
    }
    public sealed class JackRegistrationPlan
    {
        public string ConfigPath { get; }
        public IReadOnlyList<string> WadPaths { get; }
        public int Added { get; }
        internal byte[] Original { get; }
        internal byte[] Proposed { get; }
        internal JackRegistrationPlan(string path, IReadOnlyList<string> wads, int added, byte[] original, byte[] proposed) { ConfigPath = path; WadPaths = wads; Added = added; Original = original; Proposed = proposed; }
    }
    public sealed class JackRegistrationResult
    {
        public bool Installed { get; internal set; }
        public string? BackupPath { get; internal set; }
        public string? ProposedPath { get; internal set; }
        public string Message { get; internal set; } = "";
    }
    public static class JackWadRegistration
    {
        public static string? DetectConfig()
        {
            string[] paths = { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"VirtualStore\Program Files\J.A.C.K\VDKGameCfg.ini"), @"C:\Program Files\J.A.C.K\VDKGameCfg.ini", @"C:\Program Files (x86)\J.A.C.K\VDKGameCfg.ini" };
            return paths.FirstOrDefault(File.Exists);
        }
        private static Encoding Decode(byte[] bytes, out int bom)
        {
            bom = 0; if (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191) { bom = 3; return new UTF8Encoding(true, true); }
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254) { bom = 2; return new UnicodeEncoding(false, true, true); }
            try { new UTF8Encoding(false, true).GetString(bytes); return new UTF8Encoding(false, true); } catch (DecoderFallbackException) { return Encoding.GetEncoding("iso-8859-1"); }
        }
        private static List<string> Lines(string path, out byte[] original, out Encoding encoding, out string newline, out bool trailing)
        {
            original = File.ReadAllBytes(path); encoding = Decode(original, out int bom); string text = encoding.GetString(original, bom, original.Length - bom); newline = text.Contains("\r\n") ? "\r\n" : "\n"; trailing = text.EndsWith(newline, StringComparison.Ordinal); return text.Split(new[] { newline }, StringSplitOptions.None).Take(text.Split(new[] { newline }, StringSplitOptions.None).Length - (trailing ? 1 : 0)).ToList();
        }
        public static IReadOnlyList<JackProfile> Profiles(string path)
        {
            var lines = Lines(path, out _, out _, out _, out _); var profiles = new List<JackProfile>(); string section = "", title = "", kind = "", enabled = "true";
            void Save() { if (section.Length > 0 && kind.Equals("vpHalfLife", StringComparison.OrdinalIgnoreCase) && enabled.Equals("true", StringComparison.OrdinalIgnoreCase)) profiles.Add(new JackProfile(section, title.Length > 0 ? title : section)); }
            foreach (string line in lines)
            {
                var header = Regex.Match(line, @"^\s*\[(Config\d+)\]\s*$"); if (header.Success) { Save(); section = header.Groups[1].Value; title = ""; kind = ""; enabled = "true"; continue; }
                if (Regex.IsMatch(line, @"^\s*\[")) { Save(); section = ""; continue; }
                var pair = Regex.Match(line, @"^\s*(Title|MapProfile|Enabled)\s*=(.*)$", RegexOptions.IgnoreCase); if (!pair.Success) continue;
                string value = pair.Groups[2].Value.Trim(); switch (pair.Groups[1].Value.ToLowerInvariant()) { case "title": title = value; break; case "mapprofile": kind = value; break; case "enabled": enabled = value; break; }
            }
            Save(); return profiles;
        }
        public static JackRegistrationPlan Prepare(string mapPath, string configPath, string section)
        {
            mapPath = Path.GetFullPath(mapPath); configPath = Path.GetFullPath(configPath);
            if (!Profiles(configPath).Any(p => p.Section == section)) throw new ArgumentException("Seleccione un perfil Half-Life habilitado.");
            var map = MapValidator.Parse(File.ReadAllText(mapPath), .0001); var wads = map.Entities[0].Properties["wad"].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(p => Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(Path.GetDirectoryName(mapPath)!, p))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (string wad in wads) foreach (string name in WadCatalog.Read(wad).Textures) names.Add(name);
            foreach (var brush in map.Entities.SelectMany(e => e.Brushes)) foreach (var face in brush.Faces) { if (brush.Role == BrushRole.Solid) TextureMapper.ValidateSolidTexture(face.Texture); if (!names.Contains(face.Texture)) throw new ArgumentException("La textura " + face.Texture + " no existe en los WAD del MAP."); }
            var lines = Lines(configPath, out var original, out var encoding, out var newline, out bool trailing);
            int start = lines.FindIndex(l => Regex.IsMatch(l, @"^\s*\[" + Regex.Escape(section) + @"\]\s*$")); int end = start + 1; while (end < lines.Count && !Regex.IsMatch(lines[end], @"^\s*\[")) end++;
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var used = new HashSet<int>();
            for (int i = start + 1; i < end; i++) { var match = Regex.Match(lines[i], @"^\s*Parcel(\d+)\s*=(.*)$", RegexOptions.IgnoreCase); if (!match.Success) continue; used.Add(int.Parse(match.Groups[1].Value)); string path = match.Groups[2].Value.Trim().Trim('"'); try { existing.Add(Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Path.GetDirectoryName(configPath)!, path))); } catch (ArgumentException) { existing.Add(path); } }
            int added = 0; foreach (string wad in wads) { if (existing.Contains(wad)) continue; if (wad.Any(c => c > 127 || c < ' ')) throw new ArgumentException("La ruta WAD debe ser ASCII para el perfil GoldSrc."); int index = 0; while (used.Contains(index)) index++; used.Add(index); lines.Insert(end++, "Parcel" + index + "=" + wad.Replace('\\', '/')); existing.Add(wad); added++; }
            string proposed = string.Join(newline, lines) + (trailing ? newline : ""); byte[] body = encoding.GetBytes(proposed), preamble = encoding.GetPreamble(); var bytes = preamble.Concat(body).ToArray(); return new JackRegistrationPlan(configPath, wads, added, original, bytes);
        }
        public static JackRegistrationResult Apply(JackRegistrationPlan plan, string fallbackDirectory, Func<bool>? jackRunning = null)
        {
            bool Running() { if (jackRunning != null) return jackRunning(); var processes = Process.GetProcessesByName("jack"); try { return processes.Length > 0; } finally { foreach (var process in processes) process.Dispose(); } }
            if (Running()) throw new InvalidOperationException("Cierre J.A.C.K. antes de registrar el WAD. No se cerró ninguna aplicación.");
            if (!File.ReadAllBytes(plan.ConfigPath).SequenceEqual(plan.Original)) throw new InvalidOperationException("La configuración cambió desde su lectura. Vuelva a preparar el registro.");
            if (plan.Added == 0) return new JackRegistrationResult { Installed = true, Message = "Los WAD ya están registrados. Vuelva a abrir J.A.C.K. si cambió su contenido." };
            string token = DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "_" + Guid.NewGuid().ToString("N"), temp = plan.ConfigPath + "." + token + ".tmp", backup = plan.ConfigPath + "." + token + ".bak";
            try
            {
                File.WriteAllBytes(temp, plan.Proposed); if (Running()) throw new InvalidOperationException("J.A.C.K. se abrió durante la operación. Cierre el editor y reintente.");
                if (!File.ReadAllBytes(plan.ConfigPath).SequenceEqual(plan.Original)) throw new InvalidOperationException("La configuración cambió durante la operación.");
                File.Replace(temp, plan.ConfigPath, backup); return new JackRegistrationResult { Installed = true, BackupPath = backup, Message = "WAD registrados. Vuelva a abrir J.A.C.K. Copia de seguridad: " + backup };
            }
            catch (UnauthorizedAccessException) { return Fallback(); }
            catch (IOException) { return Fallback(); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            JackRegistrationResult Fallback()
            {
                Directory.CreateDirectory(fallbackDirectory); string path = Path.Combine(fallbackDirectory, "VDKGameCfg." + token + ".propuesto.ini"); File.WriteAllBytes(path, plan.Proposed);
                return new JackRegistrationResult { ProposedPath = path, Message = "No se pudo escribir la configuración. Original conservado. Con J.A.C.K. cerrado, haga una copia de seguridad y reemplace " + plan.ConfigPath + " por " + path + " usando los permisos necesarios. Después abra J.A.C.K." };
            }
        }
    }
}
