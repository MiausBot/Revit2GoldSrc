using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
namespace Revit2GoldSrc.Core
{
    public enum GeometryQuality { Balanced, High }
    public sealed class QualitySettings
    {
        public GeometryQuality Quality { get; }
        public double AccuracyFeet => (Quality == GeometryQuality.High ? 1 : 5) / 304.8;
        public double LevelOfDetail => Quality == GeometryQuality.High ? 1 : .5;
        public double VolumeError => Quality == GeometryQuality.High ? .01 : .03;
        public QualitySettings(GeometryQuality quality) { if (!Enum.IsDefined(typeof(GeometryQuality), quality)) throw new ArgumentException("Calidad inválida."); Quality = quality; }
    }
    [DataContract]
    public sealed class ElementOrigin
    {
        [DataMember] public string Document { get; set; } = "";
        [DataMember] public string DocumentName { get; set; } = "";
        [DataMember] public List<long> LinkInstances { get; set; } = new List<long>();
        [DataMember] public long ElementId { get; set; }
        public string Key => Document + "|" + string.Join("/", LinkInstances) + "|" + ElementId;
    }
    [DataContract]
    public sealed class ElementExportInfo
    {
        [DataMember] public ElementOrigin Origin { get; set; } = new ElementOrigin();
        [DataMember] public string Category { get; set; } = "";
        [DataMember] public string Family { get; set; } = "";
        [DataMember] public string Type { get; set; } = "";
        [DataMember] public string Status { get; set; } = "";
        [DataMember] public bool Collision { get; set; } = true;
        [DataMember] public string Quality { get; set; } = "Exact";
        [DataMember] public double VolumeError { get; set; }
        [DataMember] public double SurfaceDeviationFeet { get; set; }
        [DataMember] public int Brushes { get; set; }
    }
    public sealed class MeshFace
    {
        public IReadOnlyList<Vec3> Points { get; }
        public BimMaterial? Material { get; }
        public bool Painted { get; }
        public bool MaterialAmbiguous { get; }
        public Vec3 Normal => NormalOf(Points);
        public MeshFace(IReadOnlyList<Vec3> points, BimMaterial? material = null, bool painted = false, bool materialAmbiguous = false) { Points = points.ToArray(); Material = material; Painted = painted; MaterialAmbiguous = materialAmbiguous; }
        internal static Vec3 NormalOf(IReadOnlyList<Vec3> points)
        { var best = Vec3.Zero; for (int i = 1; i < points.Count - 1; i++) { var n = (points[i] - points[0]).Cross(points[i + 1] - points[0]); if (n.Length > best.Length) best = n; } if (best.Length > 1e-14) return best.Unit(); throw new ArgumentException("Cara degenerada."); }
    }
    public sealed class TriangleMesh
    {
        public IReadOnlyList<MeshFace> Triangles { get; }
        public TriangleMesh(IReadOnlyList<MeshFace> triangles) { Triangles = triangles; }
    }
    public sealed class ConvexSolid
    {
        public IReadOnlyList<MeshFace> Faces { get; }
        public IEnumerable<Vec3> Vertices => Faces.SelectMany(f => f.Points);
        public double Volume => ConvexMeshDecomposer.Volume(Faces);
        public ConvexSolid(IReadOnlyList<MeshFace> faces) { Faces = faces; }
        public ConvexSolid Convert(Vec3 origin, double scale) => new ConvexSolid(Faces.Select(f => new MeshFace(f.Points.Select(p => Coordinates.TransformCoordinates(p, origin, scale)).ToList(), f.Material, f.Painted, f.MaterialAmbiguous)).ToList());
        public void Validate(double t, Action? checkpoint = null)
        {
            if (Faces.Count < 4 || Faces.Count > 128 || Vertices.Any(p => !p.IsFinite) || Volume <= t * t * t) throw new ArgumentException("Sólido convexo inválido.");
            var vertices = Vertices.ToList(); foreach (var face in Faces) { checkpoint?.Invoke(); var n = face.Normal; double d = n.Dot(face.Points[0]); if (face.Points.Any(p => Math.Abs(n.Dot(p) - d) > t) || vertices.Any(p => { checkpoint?.Invoke(); return n.Dot(p) - d > t * 2; })) throw new ArgumentException("Sólido no convexo o con caras no planas."); }
        }
    }
    public static class ConvexMeshDecomposer
    {
        public static double Volume(IReadOnlyList<MeshFace> faces)
        {
            if (faces.Count == 0) return 0; var anchor = faces[0].Points[0]; double v = 0;
            foreach (var face in faces) for (int i = 1; i < face.Points.Count - 1; i++) v += (face.Points[0] - anchor).Dot((face.Points[i] - anchor).Cross(face.Points[i + 1] - anchor)) / 6; return v;
        }
        // Tolerance-aware welding avoids quadratic searches before the first UI checkpoint.
        private sealed class VertexIndex
        {
            private readonly double tolerance;
            private readonly Dictionary<Tuple<double, double, double>, List<int>> cells = new Dictionary<Tuple<double, double, double>, List<int>>();
            public List<Vec3> Points { get; } = new List<Vec3>();
            public VertexIndex(double tolerance) { this.tolerance = tolerance; }
            public int Node(Vec3 p)
            {
                double x = Math.Floor(p.X / tolerance), y = Math.Floor(p.Y / tolerance), z = Math.Floor(p.Z / tolerance);
                if (!Vec3.Finite(x) || !Vec3.Finite(y) || !Vec3.Finite(z)) throw new ArgumentException("Coordenadas fuera del presupuesto de análisis.");
                int found = int.MaxValue;
                for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++) for (int c = -1; c <= 1; c++)
                    if (cells.TryGetValue(Tuple.Create(x + a, y + b, z + c), out var bucket))
                        foreach (int i in bucket) if (i < found && (Points[i] - p).Length <= tolerance) found = i;
                if (found != int.MaxValue) return found;
                int id = Points.Count;
                if (id >= 12000) throw new ArgumentException("Malla supera 12000 vértices de análisis.");
                Points.Add(p); var key = Tuple.Create(x, y, z);
                if (!cells.TryGetValue(key, out var list)) { list = new List<int>(); cells.Add(key, list); }
                list.Add(id); return id;
            }
        }
        public sealed class BudgetExceededException : ArgumentException
        {
            public BudgetExceededException(TimeSpan duration) : base("Se agotó el presupuesto de descomposición de " + duration.TotalSeconds + " segundos; elemento omitido sin reducir la calidad.") { }
        }
        private static List<MeshFace> Conform(IReadOnlyList<MeshFace> faces, double t, Action? checkpoint = null)
        {
            var index = new VertexIndex(t); foreach (var p in faces.SelectMany(f => f.Points)) { checkpoint?.Invoke(); index.Node(p); }
            var vertices = index.Points;
            if (vertices.Count > 12000) throw new ArgumentException("Malla supera 12000 vértices de análisis.");
            var sorted = new[] { vertices.OrderBy(p => p.X).ToArray(), vertices.OrderBy(p => p.Y).ToArray(), vertices.OrderBy(p => p.Z).ToArray() };
            var result = new List<MeshFace>();
            foreach (var face in faces)
            {
                checkpoint?.Invoke();
                var points = new List<Vec3>(); for (int i = 0; i < face.Points.Count; i++)
                {
                    var a = face.Points[i]; var b = face.Points[(i + 1) % face.Points.Count]; var delta = b - a; double length = delta.Length; if (length <= t) throw new ArgumentException("Arista de malla degenerada.");
                    int axis = Math.Abs(delta.X) >= Math.Abs(delta.Y) && Math.Abs(delta.X) >= Math.Abs(delta.Z) ? 0 : Math.Abs(delta.Y) >= Math.Abs(delta.Z) ? 1 : 2;
                    var candidates = sorted[axis]; double lo = Math.Min(a[axis], b[axis]) - t, hi = Math.Max(a[axis], b[axis]) + t;
                    int left = 0, right = candidates.Length;
                    while (left < right) { int mid = left + (right - left) / 2; if (candidates[mid][axis] < lo) left = mid + 1; else right = mid; }
                    var edgePoints = new List<Tuple<Vec3, double>>(); double squared = delta.Dot(delta);
                    for (int j = left; j < candidates.Length && candidates[j][axis] <= hi; j++)
                    {
                        checkpoint?.Invoke(); var p = candidates[j]; double fraction = (p - a).Dot(delta) / squared;
                        if (fraction > t / length && fraction < 1 - t / length && (a + delta * fraction - p).Length <= t)
                            edgePoints.Add(Tuple.Create(p, fraction));
                    }
                    points.Add(a); points.AddRange(edgePoints.OrderBy(p => p.Item2).Select(p => p.Item1));
                }
                result.Add(new MeshFace(points, face.Material, face.Painted, face.MaterialAmbiguous));
            }
            return result;
        }
        private static List<Tuple<Vec3, Vec3>> OpenEdges(IReadOnlyList<MeshFace> faces, double t, Action? checkpoint = null)
        {
            var index = new VertexIndex(t); var edges = new Dictionary<string, Tuple<Vec3, Vec3, int, int>>();
            int Node(Vec3 p) { checkpoint?.Invoke(); return index.Node(p); }
            foreach (var face in faces) for (int i = 0; i < face.Points.Count; i++)
            {
                var a = face.Points[i]; var b = face.Points[(i + 1) % face.Points.Count]; int ia = Node(a), ib = Node(b); if (ia == ib) throw new ArgumentException("Arista degenerada."); string key = Math.Min(ia, ib) + ":" + Math.Max(ia, ib); int direction = ia < ib ? 1 : -1;
                if (edges.TryGetValue(key, out var old)) edges[key] = Tuple.Create(old.Item1, old.Item2, old.Item3 + 1, old.Item4 + direction); else edges.Add(key, Tuple.Create(a, b, 1, direction));
            }
            if (edges.Values.Any(e => e.Item3 > 2 || (e.Item3 == 2 && e.Item4 != 0))) throw new ArgumentException("Malla no manifold o con orientación inconsistente.");
            return edges.Values.Where(e => e.Item3 == 1).Select(e => Tuple.Create(e.Item1, e.Item2)).ToList();
        }
        public static IReadOnlyList<ConvexSolid> Decompose(TriangleMesh mesh, double t, Action? checkpoint = null, TimeSpan? maxDuration = null, GeometryProfile? profile = null)
        {
            if (!Vec3.Finite(t) || t <= 0 || mesh.Triangles.Count < 4 || mesh.Triangles.Count > 24000 || mesh.Triangles.Any(f => f.Points.Count != 3 || f.Points.Any(p => !p.IsFinite))) throw new ArgumentException("Malla triangular inválida o demasiado compleja.");
            profile = profile ?? GeometryProfile.GoldSrc;
            var duration = maxDuration ?? TimeSpan.FromSeconds(30); if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxDuration));
            var timer = System.Diagnostics.Stopwatch.StartNew(); int work = 0; void Budget() { if ((work++ & 255) != 0) return; checkpoint?.Invoke(); if (timer.Elapsed >= duration) throw new BudgetExceededException(duration); }
            Budget();
            var input = Conform(mesh.Triangles, t, Budget); if (OpenEdges(input, t, Budget).Count > 0) throw new ArgumentException("Malla abierta; no se exporta como sólido.");
            double volume = Volume(input); if (Math.Abs(volume) <= t * t * t) throw new ArgumentException("Malla sin volumen."); if (volume < 0) input = input.Select(f => new MeshFace(f.Points.Reverse().ToList(), f.Material, f.Painted, f.MaterialAmbiguous)).ToList();
            double expected = Math.Abs(volume); var output = new List<ConvexSolid>(); int nodes = 0;
            void Build(List<MeshFace> faces, int depth)
            {
                Budget(); if (++nodes > 8192 || depth > 64) throw new ArgumentException("Se agotó el presupuesto de descomposición convexa (30 s / 8192 nodos / profundidad 64).");
                var vertices = faces.SelectMany(f => f.Points).ToList(); Vec3 cut = Vec3.Zero; double distance = 0; bool nonconvex = false;
                foreach (var face in faces) { Budget(); var n = face.Normal; double d = n.Dot(face.Points[0]); if (vertices.Any(p => { Budget(); return n.Dot(p) - d > t * 2; })) { cut = n; distance = d; nonconvex = true; break; } }
                if (!nonconvex)
                {
                    var merged = Merge(faces, t, Budget);
                    if (merged.Count <= profile.MaxFaces && merged.All(f => f.Points.Count <= profile.MaxFaceVertices)) { var solid = new ConvexSolid(merged); solid.Validate(t, Budget); if (Math.Abs(solid.Volume - Volume(faces)) > Math.Max(Math.Abs(Volume(faces)) * 1e-7, t * 100)) throw new ArgumentException("Fusión cambió volumen: " + Volume(faces) + " -> " + solid.Volume + "; caras " + faces.Count + " -> " + merged.Count); output.Add(solid); if (output.Count > profile.MaxPieces) throw new ArgumentException("Sólido supera 4096 brushes."); return; }
                    if (merged.Any(f => f.Points.Count > profile.MaxFaceVertices))
                    {
                        var face = merged.OrderByDescending(f => f.Points.Count).First(); cut = face.Normal.Cross(Math.Abs(face.Normal.Z) < .9 ? Vec3.ZAxis : Vec3.YAxis).Unit(); distance = (face.Points.Min(p => cut.Dot(p)) + face.Points.Max(p => cut.Dot(p))) / 2;
                    }
                    else
                    {
                        var bounds = DoorMotion.Bounds(vertices); var size = bounds.Max - bounds.Min; int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2; cut = axis == 0 ? Vec3.XAxis : axis == 1 ? Vec3.YAxis : Vec3.ZAxis; distance = (bounds.Min[axis] + bounds.Max[axis]) / 2;
                    }
                }
                var negative = Clip(faces, cut, distance, t, Budget); var positive = Clip(faces, -cut, -distance, t, Budget);
                if (negative.Count < 4 || positive.Count < 4) throw new ArgumentException("División BSP degenerada.");
                double before = Volume(faces), after = Volume(negative) + Volume(positive); if (Math.Abs(after - before) > Math.Max(Math.Abs(before) * 1e-7, t * vertices.Max(p => (p - vertices[0]).Length) * vertices.Max(p => (p - vertices[0]).Length) * 12)) throw new ArgumentException("La división BSP no conserva el volumen.");
                Build(negative, depth + 1); Build(positive, depth + 1);
            }
            Build(input, 0);
            if (Math.Abs(output.Sum(s => s.Volume) - expected) > Math.Max(expected * 1e-7, t * Math.Pow(input.SelectMany(f => f.Points).Max(p => (p - input[0].Points[0]).Length), 2) * 12)) throw new ArgumentException("Descomposición no conserva volumen de malla: " + expected + " -> " + output.Sum(s => s.Volume));
            Budget(); return MergeCompatible(output, t, checkpoint, profile);
        }
        private static List<MeshFace> Clip(IReadOnlyList<MeshFace> faces, Vec3 normal, double distance, double t, Action checkpoint)
        {
            var result = new List<MeshFace>();
            foreach (var face in faces)
            {
                checkpoint();
                var values = face.Points.Select(p => normal.Dot(p) - distance).ToList();
                if (values.All(v => Math.Abs(v) <= t)) { if (face.Normal.Dot(normal) > 0) result.Add(face); continue; }
                var points = new List<Vec3>(); for (int i = 0; i < face.Points.Count; i++)
                {
                    var a = face.Points[i]; var b = face.Points[(i + 1) % face.Points.Count]; double da = values[i], db = values[(i + 1) % values.Count]; if (Math.Abs(da) <= t) a -= normal * da; if (Math.Abs(db) <= t) b -= normal * db;
                    if (da <= t) points.Add(a); if ((da < -t && db > t) || (da > t && db < -t)) points.Add(a + (b - a) * (da / (da - db)));
                }
                var unique = new List<Vec3>(); foreach (var p in points) if (unique.Count == 0 || (unique.Last() - p).Length > t) unique.Add(p); if (unique.Count > 1 && (unique[0] - unique.Last()).Length <= t) unique.RemoveAt(unique.Count - 1);
                if (unique.Count >= 3) { try { if (Area3(unique) > t * t) result.Add(new MeshFace(unique, face.Material, face.Painted, face.MaterialAmbiguous)); } catch (ArgumentException) { } }
            }
            result = Conform(result, t, checkpoint); var open = OpenEdges(result, t, checkpoint); if (open.Any(e => Math.Abs(normal.Dot(e.Item1) - distance) > t * 3 || Math.Abs(normal.Dot(e.Item2) - distance) > t * 3)) throw new ArgumentException("Frontera de corte no verificable.");
            if (open.Count == 0) return result;
            var u = normal.Cross(Math.Abs(normal.Z) < .9 ? Vec3.ZAxis : Vec3.YAxis).Unit(); var v = normal.Cross(u); var anchor = normal * distance;
            var segments = open.Select(e => Tuple.Create(new Vec3((e.Item1 - anchor).Dot(u), (e.Item1 - anchor).Dot(v), 0), new Vec3((e.Item2 - anchor).Dot(u), (e.Item2 - anchor).Dot(v), 0))).ToList();
            var points2 = segments.SelectMany(e => new[] { e.Item1, e.Item2 }).ToList(); var cells = new List<List<Vec3>> { new List<Vec3> { new Vec3(points2.Min(p => p.X), points2.Min(p => p.Y), 0), new Vec3(points2.Max(p => p.X), points2.Min(p => p.Y), 0), new Vec3(points2.Max(p => p.X), points2.Max(p => p.Y), 0), new Vec3(points2.Min(p => p.X), points2.Max(p => p.Y), 0) } };
            foreach (var edge in segments)
            {
                var delta = edge.Item2 - edge.Item1; var n = new Vec3(-delta.Y, delta.X, 0).Unit(); double d = n.Dot(edge.Item1); var next = new List<List<Vec3>>();
                foreach (var cell in cells) { checkpoint(); if (cell.Min(p => n.Dot(p) - d) < -t && cell.Max(p => n.Dot(p) - d) > t) { next.Add(PolygonGeometry.Clean(PolygonGeometry.Clip(cell, n, d), t)); next.Add(PolygonGeometry.Clean(PolygonGeometry.Clip(cell, -n, -d), t)); } else next.Add(cell); }
                cells = next.Where(c => c.Count >= 3 && Math.Abs(PolygonGeometry.Area(c)) > t * t).ToList(); if (cells.Count > 10000) throw new ArgumentException("Cara de corte supera presupuesto de celdas.");
            }
            foreach (var cell in cells) { checkpoint(); var center = cell.Aggregate(Vec3.Zero, (a, b) => a + b) / cell.Count; int hits = segments.Count(e => { checkpoint(); return (e.Item1.Y > center.Y) != (e.Item2.Y > center.Y) && center.X < (e.Item2.X - e.Item1.X) * (center.Y - e.Item1.Y) / (e.Item2.Y - e.Item1.Y) + e.Item1.X; }); if (hits % 2 == 1) { var poly = PolygonGeometry.Area(cell) > 0 ? cell : cell.AsEnumerable().Reverse().ToList(); result.Add(new MeshFace(poly.Select(p => anchor + u * p.X + v * p.Y).ToList())); } }
            result = Conform(result, t, checkpoint); if (OpenEdges(result, t, checkpoint).Count > 0) throw new ArgumentException("El cierre de corte dejó una malla abierta."); return result;
        }
        private static double Area3(IReadOnlyList<Vec3> p) { var normal = MeshFace.NormalOf(p); double area = 0; for (int i = 1; i < p.Count - 1; i++) area += (p[i] - p[0]).Cross(p[i + 1] - p[0]).Dot(normal) / 2; return Math.Abs(area); }
        private sealed class MergeTimeoutException : Exception { }
        public static IReadOnlyList<ConvexSolid> MergeCompatible(IReadOnlyList<ConvexSolid> source, double t, Action? checkpoint = null, GeometryProfile? profile = null)
        {
            profile = profile ?? GeometryProfile.GoldSrc;
            var pieces = source.ToList(); var timer = System.Diagnostics.Stopwatch.StartNew(); bool changed = true;
            // Optional optimization is bounded; stopping it does not change geometry or quality.
            if (pieces.Count > 256) return pieces;
            int work = 0; void Check() { if ((work++ & 255) != 0) return; checkpoint?.Invoke(); if (timer.Elapsed.TotalSeconds > 2) throw new MergeTimeoutException(); }
            try
            {
                while (changed)
                {
                    changed = false;
                    for (int i = 0; i < pieces.Count && !changed; i++) for (int j = i + 1; j < pieces.Count && !changed; j++)
                    {
                        checkpoint?.Invoke(); if (timer.Elapsed.TotalSeconds > 2) return pieces;
                        MeshFace? sharedA = null, sharedB = null;
                        foreach (var a in pieces[i].Faces)
                        {
                            sharedB = pieces[j].Faces.FirstOrDefault(b => { Check(); return (a.Normal + b.Normal).Length < 1e-8 && Math.Abs(a.Normal.Dot(a.Points[0] - b.Points[0])) <= t && a.Points.All(p => b.Points.Any(q => (p - q).Length <= t)) && b.Points.All(p => a.Points.Any(q => (p - q).Length <= t)); });
                            if (sharedB != null) { sharedA = a; break; }
                        }
                        if (sharedA == null || sharedB == null) continue;
                        var boundary = pieces[i].Faces.Where(f => !ReferenceEquals(f, sharedA)).Concat(pieces[j].Faces.Where(f => !ReferenceEquals(f, sharedB))).ToList(); var vertices = boundary.SelectMany(f => f.Points).ToList();
                        if (boundary.Any(f => vertices.Any(p => { Check(); return f.Normal.Dot(p - f.Points[0]) > t * 2; }))) continue;
                        List<MeshFace> merged; try { merged = Merge(boundary, t, Check); } catch (ArgumentException) { continue; }
                        if (merged.Count > profile.MaxFaces || merged.Any(f => f.Points.Count > profile.MaxFaceVertices || f.MaterialAmbiguous)) continue;
                        var union = new ConvexSolid(merged); try { union.Validate(t, Check); } catch (ArgumentException) { continue; }
                        double expected = pieces[i].Volume + pieces[j].Volume;
                        if (Math.Abs(union.Volume - expected) > Math.Max(expected * 1e-7, t * Math.Pow(vertices.Max(p => (p - vertices[0]).Length), 2) * 12)) continue;
                        pieces[i] = union; pieces.RemoveAt(j); changed = true;
                    }
                }
            }
            catch (MergeTimeoutException) { /* Keep the already validated pieces. */ }
            return pieces;
        }
        private static List<MeshFace> Merge(IReadOnlyList<MeshFace> faces, double t, Action? checkpoint = null)
        {
            var groups = new List<List<MeshFace>>(); foreach (var face in faces) { checkpoint?.Invoke(); var n = face.Normal; double d = n.Dot(face.Points[0]); var g = groups.FirstOrDefault(group => { checkpoint?.Invoke(); return (group[0].Normal - n).Length < 1e-8 && Math.Abs(group[0].Normal.Dot(group[0].Points[0]) - d) <= t; }); if (g == null) { g = new List<MeshFace>(); groups.Add(g); } g.Add(face); }
            var output = new List<MeshFace>(); foreach (var group in groups)
            {
                checkpoint?.Invoke();
                var n = group[0].Normal; var u = n.Cross(Math.Abs(n.Z) < .9 ? Vec3.ZAxis : Vec3.YAxis).Unit(); var v = n.Cross(u); var points = group.SelectMany(f => f.Points).ToList(); var center = points.Aggregate(Vec3.Zero, (a, b) => a + b) / points.Count;
                var projected = points.Select(p => new Vec3((p - center).Dot(u), (p - center).Dot(v), 0)).ToList();
                List<double> Grid(IEnumerable<double> values) { var grid = new List<double>(); foreach (double value in values.OrderBy(x => x)) if (grid.Count == 0 || value - grid.Last() > t) grid.Add(value); return grid; }
                var xs = Grid(projected.Select(p => p.X)); var ys = Grid(projected.Select(p => p.Y));
                var ordered = projected.Select(p => new Vec3(xs.First(x => { checkpoint?.Invoke(); return Math.Abs(x - p.X) <= t; }), ys.First(y => { checkpoint?.Invoke(); return Math.Abs(y - p.Y) <= t; }), 0)).OrderBy(p => p.X).ThenBy(p => p.Y).ToList(); var unique = new List<Vec3>(); foreach (var p in ordered) if (unique.Count == 0 || (unique.Last() - p).Length > t) unique.Add(p);
                var hull = new List<Vec3>(); foreach (var p in unique) { while (hull.Count >= 2 && PolygonGeometry.Cross(hull.Last() - hull[hull.Count - 2], p - hull.Last()) <= t * t) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
                int lower = hull.Count; for (int i = unique.Count - 2; i >= 0; i--) { var p = unique[i]; while (hull.Count > lower && PolygonGeometry.Cross(hull.Last() - hull[hull.Count - 2], p - hull.Last()) <= t * t) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
                if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);
                var material = group[0].Material; bool same = group.All(f => (f.Material?.SourceKey ?? "") == (material?.SourceKey ?? "") && f.Painted == group[0].Painted);
                var mergedFace = new MeshFace(hull.Select(p => center + u * p.X + v * p.Y).ToList(), same ? material : null, same && group[0].Painted, !same || group.Any(f => f.MaterialAmbiguous));
                if (Math.Abs(Area3(mergedFace.Points) - group.Sum(f => Area3(f.Points))) > t * 100) throw new ArgumentException("Fusión de caras no conserva área.");
                output.Add(mergedFace);
            }
            return output;
        }
    }
}
