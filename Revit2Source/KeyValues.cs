using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Revit2Source
{
    public sealed class KvNode
    {
        public string Key { get; set; }
        public string? Value { get; set; }
        public List<KvNode> Children { get; } = new List<KvNode>();

        public KvNode(string key, string? value = null)
        {
            Key = key;
            Value = value;
        }

        public string Get(string key, string fallback = "") => Children.FirstOrDefault(n => n.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value ?? fallback;
        public KvNode? Block(string key) => Children.FirstOrDefault(n => n.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && n.Value == null);
        public KvNode Add(string key, string value)
        {
            Children.Add(new KvNode(key, value));
            return this;
        }
    }

    public static class KeyValues
    {
        public static List<KvNode> Parse(string source)
        {
            int pos = 0;
            string? Token()
            {
                while (pos < source.Length)
                {
                    if (char.IsWhiteSpace(source[pos]))
                    {
                        pos++;
                        continue;
                    }

                    if (pos + 1 < source.Length && source[pos] == '/' && source[pos + 1] == '/')
                    {
                        while (pos < source.Length && source[pos] != '\n')
                            pos++;
                        continue;
                    }

                    break;
                }

                if (pos == source.Length)
                    return null;
                char c = source[pos++];
                if (c == '{' || c == '}')
                    return c.ToString();
                var value = new StringBuilder();
                if (c == '"')
                {
                    while (pos < source.Length)
                    {
                        c = source[pos++];
                        if (c == '"')
                            return value.ToString();
                        if (c == '\\')
                        {
                            if (pos == source.Length)
                                throw new FormatException("Escape incompleto.");
                            c = source[pos++];
                            if (c == 'n')
                                c = '\n';
                            else if (c == 'r')
                                c = '\r';
                            else if (c != '"' && c != '\\')
                            {
                                value.Append('\\');
                            }
                        }

                        value.Append(c);
                    }

                    throw new FormatException("Cadena sin cierre.");
                }

                value.Append(c);
                while (pos < source.Length && !char.IsWhiteSpace(source[pos]) && source[pos] != '{' && source[pos] != '}')
                    value.Append(source[pos++]);
                return value.ToString();
            }

            List<KvNode> Read(bool nested, int depth)
            {
                if (depth > 128)
                    throw new FormatException("KeyValues demasiado profundo.");
                var nodes = new List<KvNode>();
                while (true)
                {
                    string? key = Token();
                    if (key == null)
                    {
                        if (nested)
                            throw new FormatException("Bloque sin cierre.");
                        return nodes;
                    }

                    if (key == "}")
                    {
                        if (!nested)
                            throw new FormatException("Cierre inesperado.");
                        return nodes;
                    }

                    if (key == "{")
                        throw new FormatException("Bloque sin nombre.");
                    string? val = Token();
                    if (val == null || val == "}")
                        throw new FormatException("Falta valor.");
                    var node = new KvNode(key);
                    if (val == "{")
                        node.Children.AddRange(Read(true, depth + 1));
                    else
                        node.Value = val;
                    nodes.Add(node);
                }
            }

            return Read(false, 0);
        }

        public static string Quote(string s)
        {
            if (s.IndexOf('\0') >= 0)
                throw new ArgumentException("Cadena con NUL.");
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }

        public static string Write(IEnumerable<KvNode> nodes)
        {
            var result = new StringBuilder();
            void Node(KvNode n, int depth)
            {
                string pad = new string ('\t', depth);
                if (n.Value != null)
                    result.Append(pad).Append(Quote(n.Key)).Append(' ').Append(Quote(n.Value)).Append('\n');
                else
                {
                    result.Append(pad).Append(Quote(n.Key)).Append("\n").Append(pad).Append("{\n");
                    foreach (var c in n.Children)
                        Node(c, depth + 1);
                    result.Append(pad).Append("}\n");
                }
            }

            foreach (var n in nodes)
                Node(n, 0);
            return result.ToString();
        }
    }
}
