using System;
using System.Collections.Generic;
using System.Linq;
namespace Revit2GoldSrc.Core
{
    public sealed class ConvexPrism
    {
        public IReadOnlyList<Vec3> Polygon { get; }
        public double Bottom { get; }
        public double Top { get; }
        public ConvexPrism(IReadOnlyList<Vec3> polygon, double bottom, double top) { Polygon = polygon.ToArray(); Bottom = bottom; Top = top; }
        public double Volume => Math.Abs(PolygonGeometry.Area(Polygon)) * (Top - Bottom);
        public IEnumerable<Vec3> Vertices => Polygon.Select(p => new Vec3(p.X, p.Y, Bottom)).Concat(Polygon.Select(p => new Vec3(p.X, p.Y, Top)));
        public void Validate(double tolerance)
        {
            if (!Vec3.Finite(tolerance) || tolerance <= 0 || !Vec3.Finite(Bottom) || !Vec3.Finite(Top) || Top - Bottom <= tolerance || Polygon.Count < 3 || Polygon.Count > 126 || Polygon.Any(p => !p.IsFinite || Math.Abs(p.Z) > tolerance) || !Vec3.Finite(Volume) || Volume <= tolerance * tolerance * tolerance) throw new ArgumentException("Prisma degenerado o no finito.");
            double sign = Math.Sign(PolygonGeometry.Area(Polygon));
            for (int i = 0; i < Polygon.Count; i++)
            {
                var a = Polygon[i]; var b = Polygon[(i + 1) % Polygon.Count]; if ((b - a).Length <= tolerance) throw new ArgumentException("Arista degenerada de prisma.");
                foreach (var p in Polygon) if (sign * PolygonGeometry.Cross(b - a, p - a) < -tolerance * (b - a).Length) throw new ArgumentException("Contorno de prisma no convexo.");
            }
            if (PolygonGeometry.SelfIntersects(Polygon, tolerance)) throw new ArgumentException("Contorno de prisma autointersectado.");
        }
        public ConvexPrism Convert(Vec3 origin, double scale) => new ConvexPrism(Polygon.Select(p => new Vec3((p.X - origin.X) * scale, (p.Y - origin.Y) * scale, 0)).ToList(), (Bottom - origin.Z) * scale, (Top - origin.Z) * scale);
    }
    public static class PolygonGeometry
    {
        public static double Cross(Vec3 a, Vec3 b) => a.X * b.Y - a.Y * b.X;
        public static double Area(IReadOnlyList<Vec3> p) { if (p.Count < 3) return 0; var anchor = p[0]; double sum = 0; for (int i = 1; i < p.Count - 1; i++) sum += Cross(p[i] - anchor, p[i + 1] - anchor); return sum / 2; }
        public static List<Vec3> Clean(IEnumerable<Vec3> source, double t)
        {
            var p = new List<Vec3>(); foreach (var v in source) if (p.Count == 0 || (v - p[p.Count - 1]).Length > t) p.Add(v);
            if (p.Count > 1 && (p[0] - p[p.Count - 1]).Length <= t) p.RemoveAt(p.Count - 1);
            bool changed = true; while (changed && p.Count >= 3) { changed = false; for (int i = 0; i < p.Count; i++) { var a = p[(i + p.Count - 1) % p.Count]; var b = p[i]; var c = p[(i + 1) % p.Count]; if (Math.Abs(Cross(b - a, c - a)) <= t * (c - a).Length && (b - a).Dot(b - c) <= t * t) { p.RemoveAt(i); changed = true; break; } } }
            return p;
        }
        public static List<Vec3> Clip(IReadOnlyList<Vec3> polygon, Vec3 normal, double distance, double tolerance = 0)
        {
            var output = new List<Vec3>(); if (polygon.Count == 0) return output;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count]; double da = normal.Dot(a) - distance, db = normal.Dot(b) - distance;
                if (da <= tolerance) output.Add(a); if ((da < 0 && db > 0) || (da > 0 && db < 0)) output.Add(a + (b - a) * (da / (da - db)));
            }
            return output;
        }
        public static bool Inside(IReadOnlyList<Vec3> polygon, Vec3 point)
        { bool inside = false; for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++) { var a = polygon[i]; var b = polygon[j]; if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside; } return inside; }
        public static bool ProperCross(Vec3 a, Vec3 b, Vec3 c, Vec3 d, double t)
        { double ab = (b - a).Length, cd = (d - c).Length; return Cross(b - a, c - a) * Cross(b - a, d - a) < -t * t * ab * ab && Cross(d - c, a - c) * Cross(d - c, b - c) < -t * t * cd * cd; }
        public static bool SelfIntersects(IReadOnlyList<Vec3> p, double t)
        { for (int i = 0; i < p.Count; i++) for (int j = i + 1; j < p.Count; j++) { if (j == i + 1 || (i == 0 && j == p.Count - 1)) continue; if (ProperCross(p[i], p[(i + 1) % p.Count], p[j], p[(j + 1) % p.Count], t)) return true; } return false; }
    }
    public static class PlanarSectionDecomposer
    {
        private sealed class Face { public List<List<Vec3>> Loops = null!; public Vec3 Normal; }
        private sealed class Segment { public Vec3 A; public Vec3 B; public Segment(Vec3 a, Vec3 b) { A = a; B = b; } }
        public static IReadOnlyList<ConvexPrism> Decompose(SolidGeometryDiagnostic solid)
        {
            double t = solid.ToleranceFeet;
            if (!Vec3.Finite(t) || t <= 0 || !Vec3.Finite(solid.OriginalVolumeFeet3) || solid.OriginalVolumeFeet3 <= t * t * t) throw new ArgumentException("Tolerancia o volumen inválidos.");
            var faces = new List<Face>(); var nodes = new List<Vec3>(); var edges = new Dictionary<string, int>();
            int Node(Vec3 p) { int i = nodes.FindIndex(v => (v - p).Length <= t); if (i >= 0) return i; nodes.Add(p); return nodes.Count - 1; }
            foreach (var source in solid.Faces)
            {
                if (!source.IsPlanar || source.Loops.Count == 0) throw new ArgumentException("Superficie curva o sin contornos.");
                var loops = new List<List<Vec3>>();
                foreach (var loop in source.Loops)
                {
                    if (loop.Edges.Count < 3 || loop.Edges.Any(e => !e.IsLine)) throw new ArgumentException("Arista curva o contorno degenerado.");
                    var points = new List<Vec3>();
                    for (int i = 0; i < loop.Edges.Count; i++)
                    {
                        var e = loop.Edges[i]; var a = e.WorldStartFeet.ToVector(); var b = e.WorldEndFeet.ToVector(); var next = loop.Edges[(i + 1) % loop.Edges.Count].WorldStartFeet.ToVector();
                        if (!a.IsFinite || !b.IsFinite || (b - a).Length <= t || (b - next).Length > t) throw new ArgumentException("Contorno abierto, degenerado o no finito.");
                        int ia = Node(a), ib = Node(b); string key = Math.Min(ia, ib) + ":" + Math.Max(ia, ib); edges[key] = edges.TryGetValue(key, out int count) ? count + 1 : 1; points.Add(a);
                    }
                    loops.Add(points);
                }
                var pointsAll = loops.SelectMany(l => l).ToList(); var start = pointsAll[0]; Vec3 normal = Vec3.Zero;
                for (int i = 1; i < pointsAll.Count - 1; i++) { var n = (pointsAll[i] - start).Cross(pointsAll[i + 1] - start); if (n.Length > t * t) { normal = n.Unit(); break; } }
                if (normal.Length < .9 || pointsAll.Any(p => Math.Abs(normal.Dot(p - start)) > t)) throw new ArgumentException("Cara degenerada o no coplanar.");
                double height = pointsAll.Max(p => p.Z) - pointsAll.Min(p => p.Z);
                if (height > t && (Math.Abs(normal.Z) > 1e-8 || Math.Abs(normal.Z) * height > t)) throw new ArgumentException("Cara realmente inclinada no soportada.");
                // Reject crossed face loops in their own planar basis.
                var u = (pointsAll.First(p => (p - start).Length > t) - start).Unit(); var v = normal.Cross(u);
                foreach (var loop in loops) if (PolygonGeometry.SelfIntersects(loop.Select(p => new Vec3((p - start).Dot(u), (p - start).Dot(v), 0)).ToList(), t)) throw new ArgumentException("Contorno de cara autointersectado.");
                faces.Add(new Face { Loops = loops, Normal = normal });
            }
            if (faces.Count < 4 || edges.Values.Any(count => count != 2)) throw new ArgumentException("La frontera del sólido no es cerrada y manifold.");
            var levels = new List<double>(); foreach (double z in nodes.Select(p => p.Z).OrderBy(z => z)) if (levels.Count == 0 || z - levels.Last() > t) levels.Add(z);
            var result = new List<ConvexPrism>();
            for (int band = 0; band < levels.Count - 1; band++)
            {
                double bottom = levels[band], top = levels[band + 1], z = (bottom + top) / 2; var segments = new List<Segment>();
                foreach (var face in faces.Where(f => Math.Abs(f.Normal.Z) < 1e-8))
                {
                    var intersections = new List<Vec3>();
                    foreach (var loop in face.Loops) for (int i = 0; i < loop.Count; i++) { var a = loop[i]; var b = loop[(i + 1) % loop.Count]; if ((a.Z > z) != (b.Z > z)) { var p = a + (b - a) * ((z - a.Z) / (b.Z - a.Z)); intersections.Add(new Vec3(p.X, p.Y, 0)); } }
                    var tangent = new Vec3(-face.Normal.Y, face.Normal.X, 0); intersections = intersections.OrderBy(p => p.Dot(tangent)).ToList();
                    if (intersections.Count % 2 != 0) throw new ArgumentException("Sección de cara abierta.");
                    for (int i = 0; i < intersections.Count; i += 2) if ((intersections[i + 1] - intersections[i]).Length > t) segments.Add(new Segment(intersections[i], intersections[i + 1]));
                }
                if (segments.Count == 0) continue;
                var sectionNodes = new List<Vec3>(); var degree = new List<int>();
                foreach (var endpoint in segments.SelectMany(s => new[] { s.A, s.B })) { int i = sectionNodes.FindIndex(p => (p - endpoint).Length <= t); if (i < 0) { sectionNodes.Add(endpoint); degree.Add(1); } else degree[i]++; }
                if (degree.Any(d => d != 2)) throw new ArgumentException("Contorno de sección abierto o ambiguo.");
                for (int i = 0; i < segments.Count; i++) for (int j = i + 1; j < segments.Count; j++) if (PolygonGeometry.ProperCross(segments[i].A, segments[i].B, segments[j].A, segments[j].B, t)) throw new ArgumentException("Sección autointersectada.");
                double minX = sectionNodes.Min(p => p.X), maxX = sectionNodes.Max(p => p.X), minY = sectionNodes.Min(p => p.Y), maxY = sectionNodes.Max(p => p.Y);
                var cells = new List<List<Vec3>> { new List<Vec3> { new Vec3(minX, minY, 0), new Vec3(maxX, minY, 0), new Vec3(maxX, maxY, 0), new Vec3(minX, maxY, 0) } };
                foreach (var segment in segments)
                {
                    var d = segment.B - segment.A; var n = new Vec3(-d.Y, d.X, 0).Unit(); double distance = n.Dot(segment.A); var next = new List<List<Vec3>>();
                    foreach (var cell in cells)
                    {
                        double lo = cell.Min(p => n.Dot(p) - distance), hi = cell.Max(p => n.Dot(p) - distance);
                        if (lo < -t && hi > t) { next.Add(PolygonGeometry.Clean(PolygonGeometry.Clip(cell, n, distance), t)); next.Add(PolygonGeometry.Clean(PolygonGeometry.Clip(cell, -n, -distance), t)); } else next.Add(cell);
                    }
                    cells = next.Where(c => c.Count >= 3 && Math.Abs(PolygonGeometry.Area(c)) > t * t).ToList(); if (cells.Count > 250000) throw new ArgumentException("Sección supera 250000 celdas.");
                }
                foreach (var cell in cells)
                {
                    var p = cell.Aggregate(Vec3.Zero, (a, b) => a + b) / cell.Count; int hits = segments.Count(s => (s.A.Y > p.Y) != (s.B.Y > p.Y) && p.X < (s.B.X - s.A.X) * (p.Y - s.A.Y) / (s.B.Y - s.A.Y) + s.A.X);
                    if (hits % 2 == 0) continue; var prism = new ConvexPrism(cell, bottom, top); prism.Validate(t); result.Add(prism); if (result.Count > 4096) throw new ArgumentException("Sólido supera 4096 brushes; divida el ámbito.");
                }
            }
            double actual = result.Sum(p => p.Volume); var bound = DoorMotion.Bounds(nodes); var size = bound.Max - bound.Min;
            if (result.Count == 0 || Math.Abs(actual - solid.OriginalVolumeFeet3) > Math.Max(solid.OriginalVolumeFeet3 * 1e-7, 2 * (size.X * size.Y + size.X * size.Z + size.Y * size.Z) * t)) throw new ArgumentException("La descomposición poligonal no conserva el volumen original.");
            return result;
        }
    }
}
