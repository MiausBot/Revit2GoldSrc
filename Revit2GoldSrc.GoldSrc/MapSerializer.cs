using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Revit2GoldSrc.Core;
namespace Revit2GoldSrc.GoldSrc
{
    public sealed class MapEntity
    {
        public Dictionary<string, string> Properties { get; } = new Dictionary<string, string>();
        public List<GoldSrcBrush> Brushes { get; } = new List<GoldSrcBrush>();
        public MapEntity(string classname) { Properties.Add("classname", classname); }
    }
    public sealed class MapDocument
    {
        public List<MapEntity> Entities { get; } = new List<MapEntity>();
        internal List<Box> DoorMotionBoundsHU { get; } = new List<Box>();
    }
    public static class MapSerializer
    {
        private static string N(double n) => n.ToString("G17", CultureInfo.InvariantCulture);
        private static string Quoted(string value)
        {
            if (value.Any(c => c == '"' || c == '\\' || c < 32 || c > 126)) throw new ArgumentException("Los valores MAP deben ser ASCII sin comillas, barras inversas ni controles.");
            return "\"" + value + "\"";
        }
        public static string Serialize(MapDocument map, double tolerance)
        {
            if (map.Entities.Count == 0 || map.Entities[0].Properties["classname"] != "worldspawn") throw new ArgumentException("Falta worldspawn.");
            ToolBrushes.Validate(map);
            var text = new StringBuilder();
            foreach (var entity in map.Entities)
            {
                text.AppendLine("{"); foreach (var p in entity.Properties) text.AppendLine(Quoted(p.Key) + " " + Quoted(p.Value));
                foreach (var brush in entity.Brushes)
                {
                    ConvexGeometryValidator.Validate(brush, tolerance); text.AppendLine("{");
                    foreach (var f in brush.Faces) text.AppendLine("( " + f.A + " ) ( " + f.B + " ) ( " + f.C + " ) " + f.Texture +
                     " [ " + f.U + " " + N(f.UShift) + " ] [ " + f.V + " " + N(f.VShift) + " ] 0 " + N(f.UScale) + " " + N(f.VScale));
                    text.AppendLine("}");
                }
                text.AppendLine("}");
            }
            return text.ToString();
        }
    }
    public static class MapValidator
    {
        // Strict parser for the Valve 220 subset emitted here. Accepts arbitrary convex brushes.
        public static MapDocument Parse(string text, double tolerance)
        {
            var matches = Regex.Matches(text, @"//[^\r\n]*|""[^""\r\n]*""|[{}()\[\]]|[^\s{}()\[\]""]+").Cast<Match>().ToList();
            int cursor = 0;
            foreach (var match in matches)
            {
                if (text.Substring(cursor, match.Index - cursor).Any(c => !char.IsWhiteSpace(c))) throw new InvalidDataException("Carácter MAP no reconocido.");
                cursor = match.Index + match.Length;
            }
            if (text.Substring(cursor).Any(c => !char.IsWhiteSpace(c))) throw new InvalidDataException("Texto MAP incompleto.");
            var tokens = matches.Select(m => m.Value).Where(t => !t.StartsWith("//", StringComparison.Ordinal)).ToArray();
            var parser = new Parser(tokens, tolerance); return parser.Read();
        }
        private sealed class Parser
        {
            private readonly string[] tokens; private int pos; private readonly double tolerance;
            public Parser(string[] tokens, double t) { this.tokens = tokens; tolerance = t; }
            private string Next() { if (pos == tokens.Length) throw new InvalidDataException("Fin inesperado de MAP."); return tokens[pos++]; }
            private string Peek() => pos < tokens.Length ? tokens[pos] : "";
            private void Expect(string value) { if (Next() != value) throw new InvalidDataException("Se esperaba " + value); }
            private double Number()
            {
                if (!double.TryParse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || !Vec3.Finite(n)) throw new InvalidDataException("Número MAP inválido."); return n;
            }
            private Vec3 Vector() => new Vec3(Number(), Number(), Number());
            private Vec3 Point() { Expect("("); var p = Vector(); Expect(")"); return p; }
            private string String()
            {
                string s = Next(); if (s.Length < 2 || s[0] != '"' || s[s.Length - 1] != '"') throw new InvalidDataException("Propiedad MAP sin comillas.");
                s = s.Substring(1, s.Length - 2); if (s.Any(c => c < 32 || c > 126 || c == '\\')) throw new InvalidDataException("Propiedad MAP inválida."); return s;
            }
            public MapDocument Read()
            {
                var map = new MapDocument(); while (pos < tokens.Length)
                {
                    Expect("{"); var entity = new MapEntity(""); entity.Properties.Clear();
                    while (Peek() != "}")
                    {
                        if (Peek() == "{")
                        {
                            Next(); var faces = new List<GoldSrcFace>();
                            while (Peek() != "}")
                            {
                                var a = Point(); var b = Point(); var c = Point(); string texture = Next();
                                Expect("["); var u = Vector(); double us = Number(); Expect("]"); Expect("["); var v = Vector(); double vs = Number(); Expect("]");
                                Number(); double sx = Number(), sy = Number(); faces.Add(new GoldSrcFace(a, b, c, texture, u, v, us, vs, sx, sy));
                            }
                            Expect("}"); var role = faces.All(f=>string.Equals(f.Texture,"ORIGIN",StringComparison.OrdinalIgnoreCase)) ? BrushRole.Origin :
                                faces.All(f=>string.Equals(f.Texture,"AAATRIGGER",StringComparison.OrdinalIgnoreCase)) ? BrushRole.Trigger : BrushRole.Solid;
                            var brush = new GoldSrcBrush(faces,role); ConvexGeometryValidator.Validate(brush, tolerance); entity.Brushes.Add(brush);
                        }
                        else
                        {
                            string key = String(), value = String(); if (entity.Properties.ContainsKey(key)) throw new InvalidDataException("Propiedad duplicada."); entity.Properties.Add(key, value);
                        }
                    }
                    Expect("}"); if (!entity.Properties.TryGetValue("classname", out string? classname) || string.IsNullOrEmpty(classname)) throw new InvalidDataException("Entidad sin classname.");
                    if (classname == "worldspawn" && (map.Entities.Count != 0 || !entity.Properties.TryGetValue("mapversion", out string? version) || version != "220"))
                        throw new InvalidDataException("worldspawn o mapversion inválido.");
                    if (entity.Properties.TryGetValue("origin", out string? origin))
                    {
                        var values = origin.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (values.Length != 3 || values.Any(s => !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || !Vec3.Finite(n))) throw new InvalidDataException("Origin inválido.");
                    }
                    map.Entities.Add(entity);
                }
                if (map.Entities.Count == 0 || map.Entities[0].Properties["classname"] != "worldspawn" || map.Entities[0].Brushes.Count == 0) throw new InvalidDataException("MAP sin worldspawn o brushes.");
                ToolBrushes.Validate(map);
                return map;
            }
        }
    }
}
