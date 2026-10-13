using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Revit2GoldSrc.Core;

namespace Revit2Source
{
    public static class Vmf
    {
        internal static string Number(double n)
        {
            if (!Vec3.Finite(n))
                throw new ArgumentException("Número no finito.");
            return n.ToString("G17", CultureInfo.InvariantCulture);
        }

        public static void AssignIds(SourceDocument doc)
        {
            int id = 1;
            foreach (var e in new[]
            {
                doc.World
            }.Concat(doc.Entities))
            {
                e.Id = id++;
                foreach (var solid in e.Solids)
                {
                    solid.Id = id++;
                    foreach (var side in solid.Sides)
                        side.Id = id++;
                }
            }
        }

        public static string Write(SourceDocument doc, double tolerance = .0001, Action? checkpoint = null)
        {
            Validate(doc, tolerance, checkpoint: checkpoint);
            var nodes = new List<KvNode>
            {
                new KvNode("versioninfo").Add("editorversion", "400").Add("editorbuild", "0").Add("mapversion", "1").Add("formatversion", "100").Add("prefab", "0"),
                new KvNode("viewsettings").Add("bSnapToGrid", "1").Add("nGridSpacing", "16")
            };
            foreach (var e in new[]
            {
                doc.World
            }.Concat(doc.Entities))
            {
                var node = new KvNode(ReferenceEquals(e, doc.World) ? "world" : "entity").Add("id", e.Id.ToString(CultureInfo.InvariantCulture));
                foreach (var p in e.Properties)
                    node.Add(p.Key, p.Value);
                foreach (var s in e.Solids)
                {
                    var sn = new KvNode("solid").Add("id", s.Id.ToString(CultureInfo.InvariantCulture));
                    foreach (var f in s.Sides)
                        sn.Children.Add(new KvNode("side").Add("id", f.Id.ToString(CultureInfo.InvariantCulture)).Add("plane", "(" + f.A + ") (" + f.B + ") (" + f.C + ")").Add("material", f.Material).Add("uaxis", "[" + f.U + " " + Number(f.UShift) + "] " + Number(f.UScale)).Add("vaxis", "[" + f.V + " " + Number(f.VShift) + "] " + Number(f.VScale)).Add("rotation", "0").Add("lightmapscale", f.LightmapScale.ToString(CultureInfo.InvariantCulture)).Add("smoothing_groups", "0"));
                    node.Children.Add(sn);
                }

                if (e.Connections.Count > 0)
                {
                    var c = new KvNode("connections");
                    foreach (var output in e.Connections)
                        c.Add(output.Output, string.Join(",", output.Target, output.Input, output.Parameter, Number(output.Delay), output.Times.ToString(CultureInfo.InvariantCulture)));
                    node.Children.Add(c);
                }

                nodes.Add(node);
            }

            return KeyValues.Write(nodes);
        }

        public static Vec3 Vector(string value)
        {
            var n = Numbers(value, 3);
            return new Vec3(n[0], n[1], n[2]);
        }

        private static double[] Numbers(string value, int count)
        {
            var parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != count)
                throw new FormatException("Vector inválido.");
            var values = parts.Select(p => double.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
            if (values.Any(v => !Vec3.Finite(v)))
                throw new FormatException("Vector no finito.");
            return values;
        }

        public static SourceDocument Read(string text, double tolerance = .0001, Action? checkpoint = null)
        {
            var nodes = KeyValues.Parse(text);
            if (nodes.Count(n => n.Key == "world") != 1)
                throw new FormatException("Se requiere un world.");
            var result = new SourceDocument();
            foreach (var node in nodes.Where(n => n.Key == "world" || n.Key == "entity"))
            {
                var e = node.Key == "world" ? result.World : new SourceEntity(node.Get("classname"));
                e.Properties.Clear();
                e.Id = int.Parse(node.Get("id"), CultureInfo.InvariantCulture);
                foreach (var p in node.Children.Where(n => n.Value != null && n.Key != "id"))
                {
                    if (e.Properties.ContainsKey(p.Key))
                        throw new FormatException("Propiedad duplicada.");
                    e.Properties.Add(p.Key, p.Value!);
                }

                foreach (var sn in node.Children.Where(n => n.Key == "solid"))
                {
                    var solid = new SourceSolid
                    {
                        Id = int.Parse(sn.Get("id"), CultureInfo.InvariantCulture)
                    };
                    foreach (var fn in sn.Children.Where(n => n.Key == "side"))
                    {
                        var plane = Regex.Matches(fn.Get("plane"), @"\(([^)]+)\)");
                        if (plane.Count != 3)
                            throw new FormatException("Plano inválido.");
                        var side = new SourceSide
                        {
                            Id = int.Parse(fn.Get("id"), CultureInfo.InvariantCulture),
                            A = Vector(plane[0].Groups[1].Value),
                            B = Vector(plane[1].Groups[1].Value),
                            C = Vector(plane[2].Groups[1].Value),
                            Material = fn.Get("material"),
                            LightmapScale = int.Parse(fn.Get("lightmapscale", "16"), CultureInfo.InvariantCulture)
                        };
                        void Axis(string key, bool u)
                        {
                            var match = Regex.Match(fn.Get(key), @"^\[([^\]]+)\]\s+([^\s]+)$");
                            if (!match.Success)
                                throw new FormatException("Eje inválido.");
                            var values = Numbers(match.Groups[1].Value, 4);
                            var axis = new Vec3(values[0], values[1], values[2]);
                            double scale = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                            if (u)
                            {
                                side.U = axis;
                                side.UShift = values[3];
                                side.UScale = scale;
                            }
                            else
                            {
                                side.V = axis;
                                side.VShift = values[3];
                                side.VScale = scale;
                            }
                        }

                        Axis("uaxis", true);
                        Axis("vaxis", false);
                        solid.Sides.Add(side);
                    }

                    e.Solids.Add(solid);
                }

                foreach (var c in node.Block("connections")?.Children ?? new List<KvNode>())
                {
                    var v = (c.Value ?? "").Split(',');
                    if (v.Length != 5)
                        throw new FormatException("Conexión inválida.");
                    e.Connections.Add(new SourceConnection { Output = c.Key, Target = v[0], Input = v[1], Parameter = v[2], Delay = double.Parse(v[3], CultureInfo.InvariantCulture), Times = int.Parse(v[4], CultureInfo.InvariantCulture) });
                }

                if (node.Key == "entity")
                    result.Entities.Add(e);
            }

            Validate(result, tolerance, checkpoint: checkpoint);
            return result;
        }

        public static void Validate(SourceDocument doc, double t, FgdCatalog? fgd = null, Action? checkpoint = null)
        {
            if (!doc.World.Properties.TryGetValue("classname", out var world) || world != "worldspawn" || doc.World.Solids.Count == 0)
                throw new ArgumentException("Se requiere worldspawn sólido.");
            var ids = new HashSet<int>();
            void Id(int id)
            {
                if (id <= 0 || !ids.Add(id))
                    throw new ArgumentException("ID VMF inválido o duplicado.");
            }

            var all = new[]
            {
                doc.World
            }.Concat(doc.Entities).ToList();
            int solids = 0, sides = 0;
            foreach (var e in all)
            {
                Id(e.Id);
                if (!e.Properties.TryGetValue("classname", out var cls) || !Allowed.Contains(cls))
                    throw new ArgumentException("Entidad no permitida para L4D2: " + cls);
                foreach (var key in e.Properties.Keys)
                    if (new[]
                    {
                        "wad",
                        "sounds",
                        "anglespeed"
                    }.Contains(key.ToLowerInvariant()))
                        throw new ArgumentException("Propiedad GoldSrc no permitida: " + key);
                if (e.Properties.TryGetValue("origin", out var origin) && new[]
                {
                    Vector(origin).X,
                    Vector(origin).Y,
                    Vector(origin).Z
                }.Any(v => Math.Abs(v) >= 16384))
                    throw new ArgumentException("Entidad fuera del presupuesto espacial.");
                foreach (var solid in e.Solids)
                {
                    Id(solid.Id);
                    solids++;
                    sides += solid.Sides.Count;
                    var geometry = SourceGeometry.Validate(solid, t, checkpoint);
                    if (geometry.Vertices.Any(p => Math.Abs(p.X) >= 16384 || Math.Abs(p.Y) >= 16384 || Math.Abs(p.Z) >= 16384))
                        throw new ArgumentException("Geometría fuera del rango Source conservador ±16384.");
                    foreach (var side in solid.Sides)
                        Id(side.Id);
                }

                foreach (var c in e.Connections)
                {
                    if (c.Delay < 0 || !Vec3.Finite(c.Delay) || c.Times < -1 || new[]
                    {
                        c.Target,
                        c.Input,
                        c.Parameter
                    }.Any(v => v.Contains(",") || v.Contains("\n")))
                        throw new ArgumentException("Conexión inválida.");
                    if (!all.Any(target => target.Properties.TryGetValue("targetname", out var name) && name == c.Target))
                        throw new ArgumentException("Destino de conexión inexistente: " + c.Target);
                }
            }

            if (solids > 8192 || sides > 65536 || all.Count > 8192)
                throw new ArgumentException("Presupuesto global Source excedido.");
            fgd?.Validate(doc);
        }

        private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "worldspawn",
            "func_door",
            "func_door_rotating",
            "prop_door_rotating_checkpoint",
            "info_player_start",
            "info_survivor_position",
            "info_director",
            "info_map_parameters",
            "logic_auto",
            "light",
            "weapon_pistol_spawn",
            "weapon_first_aid_kit_spawn",
            "func_detail"
        };
    }
}
