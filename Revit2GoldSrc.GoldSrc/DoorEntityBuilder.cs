using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Revit2GoldSrc.Core;
namespace Revit2GoldSrc.GoldSrc
{
    public static class DoorEntityBuilder
    {
        private sealed class Motion
        {
            public DoorDefinition Door = null!; public Box Box = null!; public Vec3 Pivot; public Vec3 Delta; public double Angle;
            public GoldSrcBrush Brush = null!; public MapEntity Entity = null!;
            public string? Check(IReadOnlyList<Box> obstacles, double tolerance) => Door.Settings.Kind == DoorKind.Rotating
                ? DoorMotion.CheckRotation(Box, Pivot, Angle, obstacles, tolerance)
                : obstacles.Any(o => DoorMotion.Intersects(DoorMotion.SlidingSweep(Box, Delta), o, tolerance)) ? "Recorrido corredizo bloqueado." : null;
        }
        private static Box Envelope(Motion motion)
        {
            if (motion.Door.Settings.Kind == DoorKind.Sliding) return DoorMotion.Bounds(DoorMotion.SlidingSweep(motion.Box, motion.Delta).Vertices);
            var points = new List<Vec3>();
            foreach (var vertex in motion.Box.Vertices)
            {
                var delta = vertex - motion.Pivot; double phase = Math.Atan2(delta.Y, delta.X) * 180 / Math.PI;
                var angles = new List<double> { 0, motion.Angle };
                int first = (int)Math.Floor((phase + Math.Min(0, motion.Angle)) / 90), last = (int)Math.Ceiling((phase + Math.Max(0, motion.Angle)) / 90);
                for (int k = first; k <= last; k++) { double angle = k * 90 - phase; if (angle >= Math.Min(0, motion.Angle) && angle <= Math.Max(0, motion.Angle)) angles.Add(angle); }
                points.AddRange(angles.Select(angle => motion.Pivot + DoorMotion.RotateVector(delta, angle)));
            }
            return DoorMotion.Bounds(points);
        }
        private static string N(double x) => x.ToString("G17", CultureInfo.InvariantCulture);
        public static double SlidingLip(Box box, Vec3 direction, double distance)
        {
            var bounds = DoorMotion.Bounds(box.Vertices); var size = bounds.Max - bounds.Min;
            // SDK subtracts two from the engine bbox, which has been expanded by one on each side.
            return Math.Abs(direction.X * size.X) + Math.Abs(direction.Y * size.Y) + Math.Abs(direction.Z * size.Z) - distance;
        }
        public static int Add(MapDocument map, IReadOnlyList<DoorDefinition> doors, IReadOnlyList<Box> architectureFeet,
            IReadOnlyList<Box> unknownFeet, ExportOptions options, Vec3 origin, ExportReport report,
            Func<DoorDefinition, DoorLeaf, GoldSrcBrush> texture)
        {
            if (!options.Doors.Enabled) return 0;
            double scale = options.UnitsPerFoot, t = options.ToleranceFeet * scale;
            var architecture = architectureFeet.Concat(doors.SelectMany(d => d.FramesFeet)).Select(b => Coordinates.Convert(b, origin, scale)).ToList();
            var unknown = unknownFeet.Concat(doors.SelectMany(d => d.UncertainGeometryFeet)).Select(b => Coordinates.Convert(b, origin, scale)).ToList();
            var closed = doors.SelectMany(d => d.Leaves.Select(l => Tuple.Create(d, l, Coordinates.Convert(l.GeometryFeet, origin, scale)))).ToList();
            int converted = 0; var acceptedMotions = new List<Motion>();
            foreach (var group in doors.OrderBy(d => d.Id).GroupBy(d => string.IsNullOrWhiteSpace(d.Settings.Group) ? "id_" + (d.Origin.Document.Length>0?d.Origin.Key:d.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)) : "group_" + d.Settings.Group, StringComparer.Ordinal))
            {
                var infos = group.Select(d => new DoorExportInfo { ElementId = d.Id, Origin=d.Origin, Kind = d.Settings.Kind.ToString(), Activation = d.Settings.Activation.ToString(), Group = group.Key, Leaves = d.Leaves.Count }).ToList(); report.Doors.AddRange(infos);
                try
                {
                    foreach (var d in group)
                    {
                        d.Settings.Validate(); if (Math.Abs(d.Frame.W.Dot(Vec3.ZAxis)) < 1 - 1e-8) throw new ArgumentException("Puerta inclinada no soportada."); if (d.Leaves.Count == 0) throw new ArgumentException("Sin hojas verificadas.");
                        if (d.Leaves.Count > 2 || (d.Settings.LeafCount == DoorLeafCount.Single && d.Leaves.Count != 1) || (d.Settings.LeafCount == DoorLeafCount.Double && d.Leaves.Count != 2)) throw new ArgumentException("Cantidad de hojas incompatible con LeafCount."); foreach (var l in d.Leaves) { l.GeometryFeet.Validate(options.ToleranceFeet); if (Math.Abs(l.GeometryFeet.Frame.W.Dot(Vec3.ZAxis)) < 1 - 1e-8) throw new ArgumentException("Hoja no vertical."); }
                    }
                    if (group.Any(d => !d.Settings.IsDynamic))
                    {
                        if (group.Any(d => d.Settings.IsDynamic)) throw new ArgumentException("Grupo con puertas estáticas y animadas mezcladas.");
                        var staticBrushes = group.SelectMany(d => d.Leaves.Concat(d.FramesFeet.Select(b => new DoorLeaf(b, materials: d.Leaves[0].Materials))).Select(leaf => texture(d, leaf))).ToList();
                        map.Entities[0].Brushes.AddRange(staticBrushes);
                        converted += group.Count(); foreach (var info in infos) info.Status = "Static"; continue;
                    }
                    var first = group.First(); var settings = first.Settings;
                    if (group.Any(d => d.Settings.Kind != settings.Kind || d.Settings.Activation != settings.Activation || d.Settings.Wait != settings.Wait || d.Settings.Speed != settings.Speed || d.Settings.OpenAngleDegrees != settings.OpenAngleDegrees)) throw new ArgumentException("Configuración de grupo incompatible.");
                    var motions = new List<Motion>();
                    foreach (var d in group) foreach (var leaf in d.Leaves)
                    {
                        var box = Coordinates.Convert(leaf.GeometryFeet, origin, scale); var m = new Motion { Door = d, Box = box, Brush = texture(d, leaf) };
                        var obstacles = architecture.Concat(closed.Where(p => !(ReferenceEquals(p.Item1, d) && ReferenceEquals(p.Item2, leaf))).Select(p => p.Item3)).ToList();
                        if (d.Settings.Kind == DoorKind.Rotating)
                        {
                            var side = d.Settings.Hinge == HingeSide.Auto ? leaf.InferredHinge : d.Settings.Hinge;
                            if (d.Leaves.Count == 2 && d.Settings.Hinge != HingeSide.Auto)
                                side = ReferenceEquals(leaf, d.Leaves[0]) ? d.Settings.Hinge : (d.Settings.Hinge == HingeSide.Left ? HingeSide.Right : HingeSide.Left);
                            if (side == HingeSide.Auto) throw new ArgumentException("Bisagra ambigua: configure GoldSrc_HingeSide Left/Right.");
                            double x = side == HingeSide.Left ? box.Min.X : box.Max.X;
                            m.Pivot = box.Frame.ToWorld(new Vec3(x + d.Settings.HingeOffsetXFeet * scale, (d.Settings.ReverseDirection ? box.Min.Y : box.Max.Y) + d.Settings.HingeOffsetYFeet * scale, (box.Min.Z + box.Max.Z) / 2));
                            var free = box.Frame.U * (side == HingeSide.Left ? 1 : -1);
                            m.Angle = Math.Sign(Vec3.ZAxis.Cross(free).Dot(d.Frame.V)) * (d.Settings.ReverseDirection ? -1 : 1) * d.Settings.OpenAngleDegrees;
                            if (m.Angle == 0) throw new ArgumentException("Eje de bisagra no vertical.");
                            m.Entity = new MapEntity("func_door_rotating"); m.Entity.Properties["angles"] = "0 0 0"; m.Entity.Properties["distance"] = N(Math.Abs(m.Angle));
                            m.Entity.Brushes.Add(m.Brush); m.Entity.Brushes.Add(ToolBrushes.Origin(m.Pivot, t));
                        }
                        else
                        {
                            var choices = d.Settings.SlideDirection == SlideDirection.Auto ? new[] { SlideDirection.Right, SlideDirection.Left } : new[] { d.Settings.SlideDirection }; string? failure = null;
                            foreach (var direction in choices)
                            {
                                Vec3 unit = direction == SlideDirection.Up ? Vec3.ZAxis : direction == SlideDirection.Down ? -Vec3.ZAxis : box.Frame.U * (direction == SlideDirection.Left ? -1 : 1);
                                if (d.Settings.ReverseDirection) unit = -unit;
                                double distance = d.Settings.SlideDistanceFeet > 0 ? d.Settings.SlideDistanceFeet * scale : (direction == SlideDirection.Up || direction == SlideDirection.Down ? box.Max.Z - box.Min.Z : box.Max.X - box.Min.X) + 2;
                                m.Delta = unit * distance; failure = m.Check(obstacles.Concat(unknown).ToList(), t); if (failure == null) break;
                            }
                            if (failure != null) throw new ArgumentException(failure);
                            var v = m.Delta.Unit(); m.Entity = new MapEntity("func_door"); m.Entity.Properties["angles"] = Math.Abs(v.Z) > .999 ? (v.Z > 0 ? "0 -1 0" : "0 -2 0") : "0 " + N(Math.Atan2(v.Y, v.X) * 180 / Math.PI) + " 0";
                            m.Entity.Properties["lip"] = N(SlidingLip(box, v, m.Delta.Length)); m.Entity.Brushes.Add(m.Brush);
                        }
                        string? error = m.Check(obstacles, t); if (error != null) throw new ArgumentException(error);
                        if (m.Check(unknown, t) != null) throw new ArgumentException("Geometría no evaluable cerca del recorrido: no se puede garantizar el movimiento.");
                        motions.Add(m);
                    }
                    for (int i = 0; i < motions.Count; i++)
                    {
                        var sweep = Envelope(motions[i]);
                        if (motions.Take(i).Concat(acceptedMotions).Any(other => DoorMotion.Intersects(sweep, Envelope(other), t)))
                            throw new ArgumentException("Los volúmenes conservadores de recorrido de dos hojas se superponen; no se puede garantizar su movimiento conjunto.");
                    }
                    bool remote = motions.Count > 1 || settings.Activation == DoorActivation.Trigger;
                    string target = Target(group.Key); MapEntity? activator = null; Box? button = null;
                    if (remote)
                    {
                        bool useButton = settings.Activation == DoorActivation.Use || (settings.Activation == DoorActivation.Trigger && settings.ActivatorKind == DoorActivatorKind.Button);
                        var d = first; var anchor = Coordinates.TransformCoordinates(d.Frame.Origin, origin, scale);
                        var frame = new Frame(anchor, d.Frame.U, d.Frame.V, Vec3.ZAxis); var local = motions.SelectMany(m => m.Box.Vertices).Select(frame.ToLocal).ToList();
                        double half = (local.Max(p => p.X) - local.Min(p => p.X)) / 2, y = local.Max(p => p.Y);
                        var placements = settings.ManualActivator ? new[] { settings.ActivatorOffsetFeet * scale } : useButton
                            ? new[] { new Vec3(half + 12, y + 12, 64), new Vec3(-half - 12, y + 12, 64), new Vec3(half + 12, local.Min(p => p.Y) - 12, 64), new Vec3(-half - 12, local.Min(p => p.Y) - 12, 64) }
                            : new[] { new Vec3(0, y + 40, 36), new Vec3(0, local.Min(p => p.Y) - 40, 36) };
                        Box? chosen = null;
                        foreach (var p in placements)
                        {
                            var extent = useButton ? new Vec3(4, 4, 4) : new Vec3(Math.Min(24, half), 24, 36);
                            var candidate = new Box(frame, p - extent, p + extent);
                            if (architecture.Concat(unknown).Concat(closed.Select(c => c.Item3)).Any(o => DoorMotion.Intersects(candidate, o, t))) continue;
                            if (useButton && motions.Concat(acceptedMotions).Any(m => m.Check(new[] { candidate }, t) != null)) continue;
                            chosen = candidate; break;
                        }
                        if (chosen == null) throw new ArgumentException("Sin ubicación segura para el activador: configure GoldSrc_ActivatorPlacement Manual y offsets locales.");
                        activator = new MapEntity(useButton ? "func_button" : "trigger_multiple"); activator.Properties["target"] = target; activator.Properties["wait"] = N(Math.Max(.2, settings.Wait));
                        if (useButton) { activator.Properties["spawnflags"] = "1"; var buttonBrush = BrushBuilder.FromBox(chosen, motions[0].Brush.Faces[0].Texture, t); foreach (var f in buttonBrush.Faces) {f.SourceMaterialId = motions[0].Brush.Faces[0].SourceMaterialId;f.SourceMaterialKey=motions[0].Brush.Faces[0].SourceMaterialKey;} activator.Brushes.Add(buttonBrush); button = chosen; }
                        else { activator.Properties["spawnflags"] = "0"; activator.Brushes.Add(ToolBrushes.Build(chosen, BrushRole.Trigger, t)); }
                    }
                    foreach (var m in motions)
                    {
                        int flags = m.Door.Settings.Kind == DoorKind.Rotating ? 16 + (m.Angle < 0 ? 2 : 0) : 0;
                        if (!remote && settings.Activation == DoorActivation.Use) flags |= 256;
                        m.Entity.Properties["spawnflags"] = flags.ToString(CultureInfo.InvariantCulture); m.Entity.Properties["speed"] = N(settings.Speed); m.Entity.Properties["wait"] = N(settings.Wait); m.Entity.Properties["dmg"] = "0";
                        if (remote) m.Entity.Properties["targetname"] = target;
                    }
                    var frameBrushes = group.SelectMany(d => d.FramesFeet.Select(b => texture(d, new DoorLeaf(b, materials: d.Leaves[0].Materials)))).ToList();
                    foreach (var m in motions) map.Entities.Add(m.Entity); if (activator != null) map.Entities.Add(activator);
                    map.Entities[0].Brushes.AddRange(frameBrushes); if (button != null) architecture.Add(button); acceptedMotions.AddRange(motions);
                    foreach (var motion in motions) map.DoorMotionBoundsHU.Add(Envelope(motion));
                    converted += group.Count(); foreach (var info in infos) { info.Status = "Animated"; foreach (var m in motions.Where(m => m.Door.Id == info.ElementId)) { if (m.Door.Settings.Kind == DoorKind.Rotating) info.HingesHU.Add(m.Pivot.ToString()); info.Movements.Add(m.Door.Settings.Kind == DoorKind.Rotating ? N(m.Angle) + " degrees" : m.Delta.ToString() + " HU"); } }
                }
                catch (ArgumentException ex) { foreach (var info in infos) { info.Status = "Omitted"; report.Diagnostics.Add(new Diagnostic(info.ElementId, ex.Message + " Se conservó el vano libre.") { Code = "door_omitted" }); } }
            }
            return converted;
        }
        private static string Target(string value)
        { using (var hash = System.Security.Cryptography.SHA256.Create()) return "r2g_" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").Substring(0, 16).ToLowerInvariant(); }
    }
}
