using System;
using System.Collections.Generic;
using System.Linq;
using Revit2GoldSrc.Core;

namespace Revit2Source
{
    public static class SourceGeometry
    {
        public static ConvexSolid FromBox(Box box, double t)
        {
            box.Validate(t);
            var verts = box.Vertices.ToList();
            var center = box.Frame.ToWorld((box.Min + box.Max) / 2);
            var faces = new List<MeshFace>();
            foreach (var axis in new[]
            {
                box.Frame.U,
                box.Frame.V,
                box.Frame.W
            }

            )
                foreach (int sign in new[]
                {
                    -1,
                    1
                }

                )
                {
                    var n = axis * sign;
                    double d = verts.Max(p => n.Dot(p));
                    var points = verts.Where(p => Math.Abs(n.Dot(p) - d) < t).ToList();
                    faces.Add(new MeshFace(Order(points, n)));
                }

            return new ConvexSolid(faces);
        }

        public static ConvexSolid FromPrism(ConvexPrism prism, double t)
        {
            prism.Validate(t);
            var poly = prism.Polygon.ToList();
            if (PolygonGeometry.Area(poly) < 0)
                poly.Reverse();
            Vec3 P(int i, double z) => new Vec3(poly[i].X, poly[i].Y, z);
            var faces = new List<MeshFace>
            {
                new MeshFace(poly.AsEnumerable().Reverse().Select(p => new Vec3(p.X, p.Y, prism.Bottom)).ToList()),
                new MeshFace(poly.Select(p => new Vec3(p.X, p.Y, prism.Top)).ToList())
            };
            for (int i = 0; i < poly.Count; i++)
                faces.Add(new MeshFace(new[] { P(i, prism.Bottom), P((i + 1) % poly.Count, prism.Bottom), P((i + 1) % poly.Count, prism.Top), P(i, prism.Top) }));
            return new ConvexSolid(faces);
        }

        public static List<Vec3> Order(IReadOnlyList<Vec3> points, Vec3 normal)
        {
            var center = points.Aggregate(Vec3.Zero, (a, b) => a + b) / points.Count;
            var u = (points[0] - center).Unit();
            var v = normal.Cross(u);
            return points.OrderBy(p => Math.Atan2((p - center).Dot(v), (p - center).Dot(u))).ToList();
        }

        public static SourceSolid Build(ConvexSolid solid, double t, Action? checkpoint = null)
        {
            solid.Validate(t, checkpoint);
            if (solid.Faces.Any(f => f.Points.Count > GeometryProfile.Source.MaxFaceVertices)) throw new ArgumentException("Cara Source supera 64 vértices; descomponga el sólido antes de serializar.");
            if (solid.Vertices.Any(p => Math.Abs(p.X) >= 16384 || Math.Abs(p.Y) >= 16384 || Math.Abs(p.Z) >= 16384))
                throw new ArgumentException("Geometría fuera del rango Source conservador ±16384.");
            var result = new SourceSolid();
            foreach (var face in solid.Faces)
            {
                var a = face.Points[0];
                int index = 1;
                while (index < face.Points.Count - 1 && (face.Points[index] - a).Cross(face.Points[index + 1] - a).Length <= t * t)
                    index++;
                if (index >= face.Points.Count - 1)
                    throw new ArgumentException("Cara degenerada.");
                var n = face.Normal;
                var u = Math.Abs(n.Z) > .999 ? Vec3.XAxis : Vec3.ZAxis.Cross(n).Unit();
                var v = n.Cross(u);
                result.Sides.Add(new SourceSide { A = a, B = face.Points[index + 1], C = face.Points[index], U = u, V = v });
            }

            Validate(result, t, checkpoint);
            return result;
        }

        public static ConvexSolid Validate(SourceSolid solid, double t, Action? checkpoint = null)
        {
            if (t <= 0 || !Vec3.Finite(t) || solid.Sides.Count < 4 || solid.Sides.Count > 128)
                throw new ArgumentException("Sólido Source inválido.");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            int work = 0;
            void Check()
            {
                if ((work++ & 255) != 0)
                    return;
                checkpoint?.Invoke();
                if (timer.Elapsed.TotalSeconds > 30)
                    throw new ArgumentException("Validación de planos Source supera presupuesto de 30 segundos.");
            }

            Check();
            var sides = solid.Sides;
            foreach (var s in sides)
            {
                if (!s.A.IsFinite || !s.B.IsFinite || !s.C.IsFinite || !s.U.IsFinite || !s.V.IsFinite || !Vec3.Finite(s.UShift) || !Vec3.Finite(s.VShift) || !Vec3.Finite(s.UScale) || !Vec3.Finite(s.VScale) || s.UScale <= 0 || s.VScale <= 0 || s.LightmapScale < 1 || Math.Abs(s.Normal.Dot(s.U.Cross(s.V))) < 1e-8)
                    throw new ArgumentException("Plano o mapeado inválido.");
                ValidateMaterial(s.Material);
            }

            var normals = sides.Select(s => s.Normal).ToList();
            var distances = sides.Select(s => s.Distance).ToList();
            var vertices = new List<Vec3>();
            for (int i = 0; i < sides.Count; i++)
                for (int j = i + 1; j < sides.Count; j++)
                {
                    if ((normals[i] - normals[j]).Length < 1e-9 && Math.Abs(distances[i] - distances[j]) <= t)
                        throw new ArgumentException("Plano duplicado.");
                    for (int k = j + 1; k < sides.Count; k++)
                    {
                        Check();
                        double det = normals[i].Dot(normals[j].Cross(normals[k]));
                        if (Math.Abs(det) < 1e-10)
                            continue;
                        var p = (normals[j].Cross(normals[k]) * distances[i] + normals[k].Cross(normals[i]) * distances[j] + normals[i].Cross(normals[j]) * distances[k]) / det;
                        if (p.IsFinite && Enumerable.Range(0, sides.Count).All(f => normals[f].Dot(p) <= distances[f] + t) && vertices.All(v => (p - v).Length > t))
                            vertices.Add(p);
                    }
                }

            if (vertices.Count < 4)
                throw new ArgumentException("Sólido vacío o invertido.");
            var faces = new List<MeshFace>();
            var edges = new Dictionary<string, int>();
            for (int i = 0; i < sides.Count; i++)
            {
                var indices = Enumerable.Range(0, vertices.Count).Where(j => Math.Abs(normals[i].Dot(vertices[j]) - distances[i]) <= t * 2).ToList();
                if (indices.Count > GeometryProfile.Source.MaxFaceVertices) throw new ArgumentException("Cara Source supera 64 vértices.");
                if (indices.Count < 3)
                    throw new ArgumentException("Plano redundante o sólido abierto.");
                var ordered = Order(indices.Select(j => vertices[j]).ToList(), normals[i]);
                faces.Add(new MeshFace(ordered));
                for (int j = 0; j < ordered.Count; j++)
                {
                    int a = vertices.IndexOf(ordered[j]), b = vertices.IndexOf(ordered[(j + 1) % ordered.Count]);
                    string key = Math.Min(a, b) + ":" + Math.Max(a, b);
                    edges[key] = edges.TryGetValue(key, out int count) ? count + 1 : 1;
                }
            }

            if (edges.Values.Any(v => v != 2))
                throw new ArgumentException("Sólido no cerrado.");
            var result = new ConvexSolid(faces);
            result.Validate(t, Check);
            return result;
        }

        public static void ValidateMaterial(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("/") || name.Contains("..") || name.Any(c => !(char.IsLetterOrDigit(c) || c == '_' || c == '/' || c == '-' || c == '.')))
                throw new ArgumentException("Ruta de material inválida: " + name);
            if (name.Equals("origin", StringComparison.OrdinalIgnoreCase) || name.Equals("aaatrigger", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Textura exclusiva de GoldSrc.");
        }

        public static bool IntersectsHull(SourceSolid solid, Vec3 feet, double t = 0)
        {
            var center = feet + new Vec3(0, 0, 36);
            return solid.Sides.All(s => s.Normal.Dot(center) - s.Distance < Math.Abs(s.Normal.X) * 16 + Math.Abs(s.Normal.Y) * 16 + Math.Abs(s.Normal.Z) * 36 + t);
        }

        public static bool Supported(SourceSolid solid, Vec3 feet)
        {
            foreach (var s in solid.Sides.Where(s => s.Normal.Z > .7))
            {
                double z = (s.Distance - s.Normal.X * feet.X - s.Normal.Y * feet.Y) / s.Normal.Z;
                if (Math.Abs(z - feet.Z) < .5 && solid.Sides.All(f => f.Normal.Dot(new Vec3(feet.X, feet.Y, z)) - f.Distance <= .01))
                    return true;
            }

            return false;
        }
    }
}
