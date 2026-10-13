using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Revit2GoldSrc.Core;

namespace Revit2Source
{
    public static class SourceExporter
    {
        public static SourceExportResult Prepare(IReadOnlyList<BimElement> elements, SourceOptions options, ExportReport? extraction = null, IReadOnlyList<DoorDefinition>? doors = null, Action<string>? progress = null)
        {
            if (!Regex.IsMatch(options.MapName, @"^[a-zA-Z0-9_]{1,48}$") || !Vec3.Finite(options.UnitsPerFoot) || options.UnitsPerFoot <= 0 || !Vec3.Finite(options.ToleranceFeet) || options.ToleranceFeet <= 0 || !options.OriginFeet.IsFinite)
                throw new ArgumentException("Nombre, escala, tolerancia u origen inválidos.");
            var timer = Stopwatch.StartNew();
            var report = new SourceReport
            {
                RevitVersion = extraction?.RevitVersion ?? "Independent C#",
                ElementsFound = extraction?.ElementsFound ?? elements.Count,
                UnitsPerFoot = options.UnitsPerFoot
            };
            if (extraction != null)
                report.Diagnostics.AddRange(extraction.Diagnostics);
            var vertices = elements.SelectMany(e => e.Vertices).ToList();
            if (vertices.Count == 0)
                throw new ArgumentException("No hay arquitectura convertible.");
            var origin = options.CenterOrigin ? Coordinates.NormalizeCoordinates(vertices) : options.OriginFeet;
            report.OriginFeet = origin.ToString();
            double t = options.ToleranceFeet * options.UnitsPerFoot;
            var map = new SourceDocument();
            map.World.Properties["mapversion"] = "1";
            map.World.Properties["skyname"] = "sky_day01_01";
            var materials = new SourceMaterials(options.MapName, report);
            var walkableFloors = new List<SourceSolid>();
            foreach (var element in elements)
            {
                progress?.Invoke("Source: elemento " + element.Id);
                var generated = new List<SourceSolid>();
                try
                {
                    var geometries = element.Solids.Select(b => SourceGeometry.FromBox(b, options.ToleranceFeet)).Concat(element.Prisms.Select(p => SourceGeometry.FromPrism(p, options.ToleranceFeet))).Concat(element.ConvexSolids);
                    foreach (var geometry in geometries.SelectMany(g => Fragment(g, options.ToleranceFeet, progress)))
                    {
                        progress?.Invoke("Source: planos y materiales " + element.Id);
                        var converted = geometry.Convert(origin, options.UnitsPerFoot);
                        var solid = SourceGeometry.Build(converted, t, () => progress?.Invoke("Source: validando sólido " + element.Id));
                        materials.Apply(solid, geometry, element, origin, options.UnitsPerFoot, options.ToleranceFeet, () => progress?.Invoke("Source: materiales " + element.Id));
                        generated.Add(solid);
                    }

                    if (generated.Count == 0)
                        throw new ArgumentException("Sin sólidos.");
                    map.World.Solids.AddRange(generated);
                    if (element.Category == BimCategory.Floor)
                        walkableFloors.AddRange(generated);
                    report.ElementsConverted++;
                    report.Elements.Add(new ElementExportInfo { Origin = element.Origin, Category = element.CategoryName, Family = element.FamilyName, Type = element.TypeName, Status = "Converted", Collision = true, Quality = element.Quality?.ToString() ?? "Exact", VolumeError = element.VolumeError, SurfaceDeviationFeet = element.SurfaceDeviationFeet, Brushes = generated.Count });
                }
                catch (ArgumentException ex)
                {
                    report.Diagnostics.Add(new Diagnostic(element.Id, ex.Message) { Code = "source_geometry_rejected" });
                    report.Elements.Add(new ElementExportInfo { Origin = element.Origin, Category = element.CategoryName, Status = "Omitted" });
                }
            }

            if (extraction != null)
                report.Elements.AddRange(extraction.Elements.Where(e => e.Status == "Omitted"));
            report.ElementsSkipped = report.ElementsFound - report.ElementsConverted;
            var closedDoorSolids = BuildDoors(map, doors ?? Array.Empty<DoorDefinition>(), materials, origin, options.UnitsPerFoot, t, report, progress, options.UncertainObstaclesFeet);
            var collision = map.World.Solids.Concat(closedDoorSolids).ToList();
            foreach (var placement in options.Configuration.Placements.Where(p => p.Kind == "Checkpoint"))
                AddCheckpoint(map, placement, options, origin, collision, report, t);
            var placed = options.Configuration.Placements.Where(p => p.Kind == "Survivor").ToList();
            var survivors = new List<Tuple<Vec3, int, double>>();
            foreach (var p in placed)
            {
                if (p.SurvivorOrder < 1 || p.SurvivorOrder > 4 || survivors.Any(s => s.Item2 == p.SurvivorOrder))
                    throw new ArgumentException("Orden de superviviente duplicado o fuera de 1–4.");
                var point = Coordinates.TransformCoordinates(p.Position, origin, options.UnitsPerFoot);
                ValidateSurvivor(point, collision, survivors.Select(s => s.Item1).ToList());
                survivors.Add(Tuple.Create(point, p.SurvivorOrder, p.Yaw));
            }

            if (options.AutoSurvivors && survivors.Count < 4)
            {
                int candidates = 0;
                var searchTimer = Stopwatch.StartNew();
                foreach (var floor in walkableFloors)
                {
                    if (survivors.Count == 4)
                        break;
                    var geom = SourceGeometry.Validate(floor, t);
                    var bounds = DoorMotion.Bounds(geom.Vertices);
                    foreach (var face in floor.Sides.Where(f => f.Normal.Z > .7))
                        for (double x = bounds.Min.X + 18; x <= bounds.Max.X - 18 && survivors.Count < 4; x += 40)
                            for (double y = bounds.Min.Y + 18; y <= bounds.Max.Y - 18 && survivors.Count < 4; y += 40)
                            {
                                progress?.Invoke("Source: buscando posiciones de supervivientes");
                                if (++candidates > 25000 || searchTimer.Elapsed.TotalSeconds > 10)
                                    throw new ArgumentException("Búsqueda de supervivientes agotó su presupuesto. Coloque cuatro posiciones manualmente.");
                                double z = (face.Distance - face.Normal.X * x - face.Normal.Y * y) / face.Normal.Z;
                                var p = new Vec3(x, y, z);
                                try
                                {
                                    ValidateSurvivor(p, collision, survivors.Select(s => s.Item1).ToList());
                                    int order = Enumerable.Range(1, 4).First(i => survivors.All(s => s.Item2 != i));
                                    survivors.Add(Tuple.Create(p, order, 0.0));
                                }
                                catch (ArgumentException)
                                {
                                }
                            }
                }
            }

            if (survivors.Count != 4)
                throw new ArgumentException("No hay cuatro posiciones comprobables. Coloque cuatro supervivientes manualmente sobre pisos con espacio libre.");
            var names = new[]
            {
                "Nick",
                "Rochelle",
                "Coach",
                "Ellis"
            };
            foreach (var s in survivors.OrderBy(s => s.Item2))
            {
                var entity = Point("info_survivor_position", s.Item1 + new Vec3(0, 0, .1), s.Item3);
                entity.Properties["Order"] = s.Item2.ToString(System.Globalization.CultureInfo.InvariantCulture);
                entity.Properties["SurvivorName"] = names[s.Item2 - 1];
                entity.Properties["GameMode"] = "coop";
                map.Entities.Add(entity);
            }

            map.Entities.Add(Point("info_player_start", survivors[0].Item1 + new Vec3(0, 0, .1), survivors[0].Item3));
            var director = Point("info_director", survivors[0].Item1 + new Vec3(0, 0, 8));
            director.Properties["targetname"] = "revit_director";
            director.Connections.Add(new SourceConnection { Output = "OnGameplayStart", Target = "revit_director", Input = "ForceSurvivorPositions", Delay = 0, Times = 1 });
            director.Connections.Add(new SourceConnection { Output = "OnGameplayStart", Target = "revit_director", Input = "ReleaseSurvivorPositions", Delay = .2, Times = 1 });
            map.Entities.Add(director);
            map.Entities.Add(Point("info_map_parameters", survivors[0].Item1 + new Vec3(0, 0, 8)));
            foreach (var placement in options.Configuration.Placements.Where(p => p.Kind != "Survivor" && p.Kind != "Checkpoint"))
            {
                var p = Coordinates.TransformCoordinates(placement.Position, origin, options.UnitsPerFoot);
                string cls = placement.Kind == "Light" ? "light" : placement.Kind == "Weapon" ? "weapon_pistol_spawn" : placement.Kind == "Medkit" ? "weapon_first_aid_kit_spawn" : throw new ArgumentException("Colocación desconocida: " + placement.Kind);
                if (collision.Any(s => s.Sides.All(f => f.Normal.Dot(p) - f.Distance < 2)))
                    throw new ArgumentException("Luz o recurso dentro de geometría.");
                var e = Point(cls, p, placement.Yaw);
                if (cls == "light")
                    e.Properties["_light"] = ValidateLight(placement.LightColor);
                else
                {
                    e.Properties["spawnflags"] = "2";
                    if (cls == "weapon_pistol_spawn")
                        e.Properties["count"] = "1";
                }

                map.Entities.Add(e);
            }

            if (options.AutoLight && !map.Entities.Any(e => e.Properties["classname"] == "light"))
            {
                var p = survivors[0].Item1 + new Vec3(0, 0, 84);
                if (collision.Any(s => s.Sides.All(f => f.Normal.Dot(p) - f.Distance < 2)))
                    throw new ArgumentException("No hay posición automática válida para luz; coloque una manualmente.");
                var light = Point("light", p);
                light.Properties["_light"] = "255 240 220 300";
                map.Entities.Add(light);
            }

            Vmf.AssignIds(map);
            progress?.Invoke("Validando VMF Source y FGD L4D2");
            Vmf.Validate(map, t, options.Fgd, () => progress?.Invoke("Validando VMF Source"));
            report.FgdValidated = options.Fgd != null;
            if (options.Fgd == null)
                report.Diagnostics.Add(new Diagnostic(null, "FGD no disponible: validación de entidades pendiente.") { Code = "source_fgd_pending" });
            var encoded = Vmf.Write(map, t, () => progress?.Invoke("Serializando VMF Source"));
            Vmf.Read(encoded, t, () => progress?.Invoke("Releyendo VMF Source"));
            report.Solids = map.World.Solids.Count + map.Entities.Sum(e => e.Solids.Count);
            report.Sides = map.World.Solids.Sum(s => s.Sides.Count) + map.Entities.Sum(e => e.Solids.Sum(s => s.Sides.Count));
            report.Entities = map.Entities.Count;
            materials.CompleteReport(map);
            report.Seconds = timer.Elapsed.TotalSeconds + (extraction?.ElapsedSeconds ?? 0);
            return new SourceExportResult(map, report, materials.Assets(), options.Configuration);
        }

        private static IReadOnlyList<ConvexSolid> Fragment(ConvexSolid solid, double tolerance, Action<string>? progress)
        {
            if (solid.Faces.Count <= GeometryProfile.Source.MaxFaces && solid.Faces.All(f => f.Points.Count <= GeometryProfile.Source.MaxFaceVertices)) return new[] { solid };
            var triangles = new List<MeshFace>();
            foreach (var face in solid.Faces) for (int i = 1; i < face.Points.Count - 1; i++)
                if ((face.Points[i] - face.Points[0]).Cross(face.Points[i + 1] - face.Points[0]).Length > tolerance * tolerance)
                    triangles.Add(new MeshFace(new[] { face.Points[0], face.Points[i], face.Points[i + 1] }, face.Material, face.Painted, face.MaterialAmbiguous));
            return ConvexMeshDecomposer.Decompose(new TriangleMesh(triangles), tolerance, () => progress?.Invoke("Source: dividiendo caras para VBSP"), profile: GeometryProfile.Source);
        }

        private static string ValidateLight(string value)
        {
            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4)
                throw new ArgumentException("Luz: R G B intensidad.");
            var n = parts.Select(p => double.Parse(p, System.Globalization.CultureInfo.InvariantCulture)).ToList();
            if (n.Any(v => !Vec3.Finite(v) || v < 0) || n.Take(3).Any(v => v > 255) || n[3] > 10000)
                throw new ArgumentException("Color o intensidad de luz inválidos.");
            return value;
        }

        public static SourceEntity Point(string classname, Vec3 origin, double yaw = 0)
        {
            if (!origin.IsFinite || !Vec3.Finite(yaw))
                throw new ArgumentException("Colocación no finita.");
            var e = new SourceEntity(classname);
            e.Properties["origin"] = origin.ToString();
            if (classname != "info_director" && classname != "light")
                e.Properties["angles"] = "0 " + Vmf.Number(yaw) + " 0";
            return e;
        }

        private static void ValidateSurvivor(Vec3 p, IReadOnlyList<SourceSolid> collision, IReadOnlyList<Vec3> previous)
        {
            var supportPoints = new[] { p, p + new Vec3(-16,-16,0), p + new Vec3(-16,16,0), p + new Vec3(16,-16,0), p + new Vec3(16,16,0) };
            if (!p.IsFinite || !supportPoints.All(q => collision.Any(s => SourceGeometry.Supported(s, q))) || collision.Any(s => SourceGeometry.IntersectsHull(s, p, -.01)) || previous.Any(q => Math.Abs(q.X - p.X) < 34 && Math.Abs(q.Y - p.Y) < 34 && Math.Abs(q.Z - p.Z) < 74))
                throw new ArgumentException("Superviviente sin apoyo o con colisión.");
        }

        private static List<SourceSolid> BuildDoors(SourceDocument map, IReadOnlyList<DoorDefinition> doors, SourceMaterials materials, Vec3 origin, double scale, double t, SourceReport report, Action<string>? progress, IReadOnlyList<Box> uncertain)
        {
            var closed = new List<SourceSolid>();
            var moving = new List<Box>();
            var world = map.World.Solids.Select(s => DoorMotion.Bounds(SourceGeometry.Validate(s, t).Vertices)).ToList();
            foreach (var door in doors)
            {
                progress?.Invoke("Source: puerta " + door.Id);
                foreach (var frameBox in door.FramesFeet)
                {
                    var originalFrame = SourceGeometry.FromBox(frameBox, t / scale);
                    var frameSolid = SourceGeometry.Build(originalFrame.Convert(origin, scale), t);
                    materials.Apply(frameSolid, originalFrame, new BimElement(door.Id, BimCategory.Wall, new[] { frameBox }), origin, scale, t / scale);
                    map.World.Solids.Add(frameSolid);
                }

                var generated = new List<SourceEntity>();
                var candidate = new List<SourceSolid>();
                try
                {
                    door.Settings.Validate();
                    var frame = door.Frame;
                    if (Math.Abs(frame.W.Z) < .99999)
                        throw new ArgumentException("Puerta inclinada no soportada.");
                    var obstacles = world.Concat(moving).Concat(doors.Where(d => !ReferenceEquals(d, door)).SelectMany(d => d.Leaves).Select(l => Coordinates.Convert(l.GeometryFeet, origin, scale))).Concat(door.FramesFeet.Select(b => Coordinates.Convert(b, origin, scale))).ToList();
                    var unknown = uncertain.Select(b => Coordinates.Convert(b, origin, scale)).ToList();
                    for (int i = 0; i < door.Leaves.Count; i++)
                    {
                        var leaf = door.Leaves[i];
                        var box = Coordinates.Convert(leaf.GeometryFeet, origin, scale);
                        var original = SourceGeometry.FromBox(leaf.GeometryFeet, t / scale);
                        var solid = SourceGeometry.Build(original.Convert(origin, scale), t);
                        materials.Apply(solid, original, new BimElement(door.Id, BimCategory.Wall, new[] { leaf.GeometryFeet }, materialSurfaces: leaf.Materials, materialFrame: frame), origin, scale, t / scale);
                        if (!door.Settings.IsDynamic)
                        {
                            candidate.Add(solid);
                            continue;
                        }

                        if (door.UncertainGeometryFeet.Count > 0)
                            throw new ArgumentException("Hoja o marco ambiguo: configure una geometría verificable.");
                        if (obstacles.Any(b => DoorMotion.Intersects(box, b, t)))
                            throw new ArgumentException("Hoja cerrada interseca un obstáculo.");
                        var e = new SourceEntity(door.Settings.Kind == DoorKind.Rotating ? "func_door_rotating" : "func_door");
                        e.Solids.Add(solid);
                        e.Properties["targetname"] = "revit_door_" + door.Id + "_" + i;
                        e.Properties["speed"] = Vmf.Number(door.Settings.Speed);
                        e.Properties["wait"] = Vmf.Number(door.Settings.Wait);
                        e.Properties["spawnpos"] = "0";
                        e.Properties["dmg"] = "0";
                        if (door.Settings.Kind == DoorKind.Rotating)
                        {
                            var hinge = door.Settings.Hinge == HingeSide.Auto ? leaf.InferredHinge : door.Settings.Hinge;
                            if (hinge == HingeSide.Auto)
                                throw new ArgumentException("Bisagra ambigua; indique Left o Right.");
                            var pivotFeet = frame.ToWorld(new Vec3(hinge == HingeSide.Left ? leaf.GeometryFeet.Min.X : leaf.GeometryFeet.Max.X, (leaf.GeometryFeet.Min.Y + leaf.GeometryFeet.Max.Y) / 2, leaf.GeometryFeet.Min.Z)) + frame.U * door.Settings.HingeOffsetXFeet + frame.V * door.Settings.HingeOffsetYFeet;
                            var pivot = Coordinates.TransformCoordinates(pivotFeet, origin, scale);
                            double radius = box.Vertices.Max(p => new Vec3(p.X - pivot.X, p.Y - pivot.Y, 0).Length);
                            var sweepBounds = new Box(Frame.Identity, new Vec3(pivot.X - radius, pivot.Y - radius, box.Vertices.Min(p => p.Z)), new Vec3(pivot.X + radius, pivot.Y + radius, box.Vertices.Max(p => p.Z)));
                            if (unknown.Any(b => DoorMotion.Intersects(sweepBounds, b, t)))
                                throw new ArgumentException("Geometría no evaluable cerca del arco de apertura.");
                            double angle = door.Settings.OpenAngleDegrees * (door.Settings.ReverseDirection ? -1 : 1);
                            var reason = DoorMotion.CheckRotation(box, pivot, angle, obstacles, t);
                            if (reason != null)
                                throw new ArgumentException(reason);
                            e.Properties["origin"] = pivot.ToString();
                            e.Properties["angles"] = "0 0 0";
                            e.Properties["distance"] = Vmf.Number(door.Settings.OpenAngleDegrees);
                            e.Properties["spawnflags"] = (256 + 16 + (door.Settings.ReverseDirection ? 2 : 0)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            var direction = door.Settings.SlideDirection;
                            var vector = direction == SlideDirection.Up ? Vec3.ZAxis : direction == SlideDirection.Down ? -Vec3.ZAxis : direction == SlideDirection.Left ? -frame.U : direction == SlideDirection.Right ? frame.U : throw new ArgumentException("Indique dirección corrediza.");
                            double distance = door.Settings.SlideDistanceFeet * scale;
                            if (distance <= 0)
                                throw new ArgumentException("Indique recorrido positivo de corrediza.");
                            if (door.Settings.ReverseDirection)
                                vector = -vector;
                            var sweep = DoorMotion.SlidingSweep(box, vector * distance);
                            if (unknown.Any(b => DoorMotion.Intersects(sweep, b, t)))
                                throw new ArgumentException("Geometría no evaluable cerca del recorrido corredizo.");
                            if (obstacles.Any(b => DoorMotion.Intersects(sweep, b, t)))
                                throw new ArgumentException("Recorrido corredizo bloqueado.");
                            var size = DoorMotion.Bounds(box.Vertices).Max - DoorMotion.Bounds(box.Vertices).Min;
                            double projected = Math.Abs(vector.X) * (size.X - 2) + Math.Abs(vector.Y) * (size.Y - 2) + Math.Abs(vector.Z) * (size.Z - 2);
                            e.Properties["lip"] = Vmf.Number(projected - distance);
                            e.Properties["movedir"] = Math.Abs(vector.Z) > .99 ? (vector.Z > 0 ? "-90 0 0" : "90 0 0") : "0 " + Vmf.Number(Math.Atan2(vector.Y, vector.X) * 180 / Math.PI) + " 0";
                            e.Properties["origin"] = box.Frame.ToWorld((box.Min + box.Max) / 2).ToString();
                            e.Properties["spawnflags"] = "256";
                        }

                        generated.Add(e);
                        candidate.Add(solid);
                    }

                    foreach (var e in generated)
                    {
                        map.Entities.Add(e);
                        report.Doors.Add(new SourceDoorInfo { ElementId = door.Id, Document = door.Origin.Document, Kind = door.Settings.Kind.ToString(), Status = "Dynamic", Properties = new Dictionary<string, string>(e.Properties) });
                    }

                    if (generated.Count == 0)
                        report.Doors.Add(new SourceDoorInfo { ElementId = door.Id, Document = door.Origin.Document, Kind = "Static", Status = "Static" });
                    if (!door.Settings.IsDynamic)
                        map.World.Solids.AddRange(candidate);
                    closed.AddRange(candidate);
                    moving.AddRange(door.Leaves.Select(l => Coordinates.Convert(l.GeometryFeet, origin, scale)));
                    report.Diagnostics.Add(new Diagnostic(door.Id, "Puerta Source validada: " + door.Settings.Kind + "; " + generated.Count + " entidades.") { Code = "source_door_validated", Severity = "info" });
                }
                catch (ArgumentException ex)
                {
                    report.Doors.Add(new SourceDoorInfo { ElementId = door.Id, Document = door.Origin.Document, Kind = door.Settings.Kind.ToString(), Status = "Omitted", Reason = ex.Message });
                    report.Diagnostics.Add(new Diagnostic(door.Id, "Puerta omitida; vano conservado: " + ex.Message) { Code = "source_door_rejected" });
                }
            }

            return closed;
        }

        private static void AddCheckpoint(SourceDocument map, SourcePlacement placement, SourceOptions options, Vec3 origin, List<SourceSolid> collision, SourceReport report, double t)
        {
            try
            {
                SourceGeometry.ValidateMaterial(placement.Model);
                var bounds = options.ResolveModel?.Invoke(placement.Model) ?? throw new ArgumentException("Modelo de refugio no disponible o límites no verificables.");
                double width = Math.Max(bounds.Max.X - bounds.Min.X, bounds.Max.Y - bounds.Min.Y), height = bounds.Max.Z - bounds.Min.Z;
                if (placement.OpeningWidthFeet <= 0 || placement.OpeningHeightFeet <= 0 || Math.Abs(width - placement.OpeningWidthFeet * options.UnitsPerFoot) > 2 || Math.Abs(height - placement.OpeningHeightFeet * options.UnitsPerFoot) > 2)
                    throw new ArgumentException("Modelo oficial no encaja en el vano indicado.");
                var yaw = placement.Yaw;
                var frame = new Frame(Coordinates.TransformCoordinates(placement.Position, origin, options.UnitsPerFoot), DoorMotion.RotateVector(Vec3.XAxis, yaw), DoorMotion.RotateVector(Vec3.YAxis, yaw), Vec3.ZAxis);
                var box = new Box(frame, bounds.Min, bounds.Max);
                var obstacles = collision.Select(s => DoorMotion.Bounds(SourceGeometry.Validate(s, t).Vertices)).ToList();
                if (obstacles.Any(b => DoorMotion.Intersects(box, b, t)))
                    throw new ArgumentException("Refugio cerrado interseca arquitectura.");
                var reason = DoorMotion.CheckRotation(box, frame.Origin, 90, obstacles, t);
                if (reason != null)
                    throw new ArgumentException(reason);
                var e = Point("prop_door_rotating_checkpoint", frame.Origin, yaw);
                e.Properties["model"] = placement.Model;
                e.Properties["spawnflags"] = "8192";
                e.Properties["opendir"] = "2";
                e.Properties["distance"] = "90";
                map.Entities.Add(e);
                report.Doors.Add(new SourceDoorInfo { Kind = "Checkpoint", Status = "Dynamic", Properties = new Dictionary<string, string>(e.Properties) });
                collision.Add(SourceGeometry.Build(SourceGeometry.FromBox(box, t), t));
            }
            catch (ArgumentException ex)
            {
                report.Doors.Add(new SourceDoorInfo { Kind = "Checkpoint", Status = "Omitted", Reason = ex.Message });
                report.Diagnostics.Add(new Diagnostic(null, "Puerta de refugio omitida: " + ex.Message) { Code = "source_checkpoint_rejected" });
            }
        }
    }
}
