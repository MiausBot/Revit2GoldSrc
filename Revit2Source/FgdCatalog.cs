using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Revit2Source
{
    public sealed class FgdCatalog
    {
        private sealed class Definition
        {
            public bool Solid;
            public ulong Flags;
            public List<string> Bases = new List<string>();
            public HashSet<string> Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase), Inputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase), Outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private readonly Dictionary<string, Definition> definitions = new Dictionary<string, Definition>(StringComparer.Ordinal);
        public static FgdCatalog Read(string path)
        {
            var catalog = new FgdCatalog();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void File(string file)
            {
                file = Path.GetFullPath(file);
                if (!visited.Add(file))
                    return;
                if (!System.IO.File.Exists(file))
                    throw new FileNotFoundException("FGD incluido ausente.", file);
                string text = Regex.Replace(System.IO.File.ReadAllText(file), @"//[^\r\n]*", "");
                foreach (Match m in Regex.Matches(text, "@include\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
                    File(Path.Combine(Path.GetDirectoryName(file)!, m.Groups[1].Value));
                foreach (Match m in Regex.Matches(text, @"@(BaseClass|PointClass|SolidClass)\b([^@]*?)=\s*(\w+)", RegexOptions.IgnoreCase))
                {
                    int start = text.IndexOf('[', m.Index + m.Length);
                    if (start < 0)
                        continue;
                    int depth = 1, i = start + 1;
                    bool quoted = false;
                    for (; i < text.Length && depth > 0; i++)
                    {
                        char c = text[i];
                        if (c == '"' && (i == 0 || text[i - 1] != '\\'))
                            quoted = !quoted;
                        if (!quoted)
                        {
                            if (c == '[')
                                depth++;
                            if (c == ']')
                                depth--;
                        }
                    }

                    if (depth != 0)
                        throw new FormatException("FGD sin cierre.");
                    string body = text.Substring(start + 1, i - start - 2);
                    var def = new Definition
                    {
                        Solid = m.Groups[1].Value.Equals("SolidClass", StringComparison.OrdinalIgnoreCase)
                    };
                    var bases = Regex.Match(m.Groups[2].Value, @"base\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
                    if (bases.Success)
                        def.Bases = bases.Groups[1].Value.Split(',').Select(b => b.Trim()).ToList();
                    foreach (Match field in Regex.Matches(body, @"(?m)^\s*(?:(input|output)\s+)?(\w+)\s*\("))
                    {
                        string kind = field.Groups[1].Value;
                        var set = kind == "input" ? def.Inputs : kind == "output" ? def.Outputs : def.Fields;
                        set.Add(field.Groups[2].Value);
                    }

                    foreach (Match flags in Regex.Matches(body, @"spawnflags\s*\(flags\)\s*=\s*\[([^\]]*)\]", RegexOptions.IgnoreCase))
                        foreach (Match bit in Regex.Matches(flags.Groups[1].Value, @"(?m)^\s*(\d+)\s*:"))
                            def.Flags |= ulong.Parse(bit.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    catalog.definitions[m.Groups[3].Value] = def;
                }
            }

            File(path);
            return catalog;
        }

        private HashSet<string> Members(string name, int kind, HashSet<string>? stack = null)
        {
            stack = stack ?? new HashSet<string>(StringComparer.Ordinal);
            if (!stack.Add(name))
                throw new FormatException("Herencia FGD circular: " + name + " / " + string.Join(" -> ", stack));
            if (!definitions.TryGetValue(name, out var d))
                throw new ArgumentException("Clase ausente en FGD: " + name);
            var result = new HashSet<string>(kind == 0 ? d.Fields : kind == 1 ? d.Inputs : d.Outputs, StringComparer.OrdinalIgnoreCase);
            foreach (var b in d.Bases)
                result.UnionWith(Members(b, kind, new HashSet<string>(stack, StringComparer.Ordinal)));
            return result;
        }

        private ulong Flags(string name, HashSet<string>? seen = null)
        {
            seen = seen ?? new HashSet<string>(StringComparer.Ordinal);
            if (!seen.Add(name))
                throw new FormatException("Herencia FGD circular.");
            var d = definitions[name];
            ulong flags = d.Flags;
            foreach (var parent in d.Bases)
                flags |= Flags(parent, new HashSet<string>(seen, StringComparer.Ordinal));
            return flags;
        }

        public void Validate(SourceDocument doc)
        {
            var all = new[]
            {
                doc.World
            }.Concat(doc.Entities).ToList();
            foreach (var e in all)
            {
                string cls = e.Properties["classname"];
                var fields = Members(cls, 0);
                foreach (var p in e.Properties.Keys)
                    if (p != "classname" && p != "mapversion" && p != "origin" && !fields.Contains(p))
                        throw new ArgumentException("Propiedad no definida en FGD L4D2: " + cls + "." + p);
                if (e.Properties.TryGetValue("spawnflags", out var value) && (!ulong.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var flags) || (flags & ~Flags(cls)) != 0))
                    throw new ArgumentException("Flags no definidos en FGD: " + cls);
                if (cls != "worldspawn" && e.Solids.Count > 0 && !definitions[cls].Solid)
                    throw new ArgumentException("Entidad puntual con brushes: " + cls);
                foreach (var c in e.Connections)
                {
                    if (!Members(cls, 2).Contains(c.Output))
                        throw new ArgumentException("Output no definido: " + cls + "." + c.Output);
                    foreach (var target in all.Where(n => n.Properties.TryGetValue("targetname", out var name) && name == c.Target))
                        if (!Members(target.Properties["classname"], 1).Contains(c.Input))
                            throw new ArgumentException("Input no definido: " + c.Input);
                }
            }
        }
    }
}
