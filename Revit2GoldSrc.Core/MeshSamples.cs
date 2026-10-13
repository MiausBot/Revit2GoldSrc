using System;
using System.Collections.Generic;
using System.Linq;
namespace Revit2GoldSrc.Core
{
    // Deterministic closed fixtures used by the sample generator and cross-runtime geometry tests.
    public static class MeshSamples
    {
        public static TriangleMesh Tube(int sides = 16, double inner = 1, BimMaterial? material = null)
            => TubePath(sides, inner, new[] { Vec3.Zero, new Vec3(0, 0, 5) }, new[] { Vec3.XAxis, Vec3.XAxis }, new[] { Vec3.YAxis, Vec3.YAxis }, material);
        public static TriangleMesh Elbow(int sides = 8, int segments = 4, BimMaterial? material = null)
        {
            var centers = new List<Vec3>(); var us = new List<Vec3>(); var vs = new List<Vec3>();
            for (int i = 0; i <= segments; i++) { double a = i * Math.PI / 2 / segments; var u = new Vec3(Math.Cos(a), Math.Sin(a), 0); centers.Add(u * 6); us.Add(u); vs.Add(-Vec3.ZAxis); }
            return TubePath(sides, 0, centers, us, vs, material);
        }
        private static TriangleMesh TubePath(int sides, double inner, IReadOnlyList<Vec3> centers, IReadOnlyList<Vec3> us, IReadOnlyList<Vec3> vs, BimMaterial? material)
        {
            var faces = new List<MeshFace>(); Vec3 Point(int station, int j, double radius) => centers[station] + (us[station] * Math.Cos(j * 2 * Math.PI / sides) + vs[station] * Math.Sin(j * 2 * Math.PI / sides)) * radius;
            void Quad(Vec3 a, Vec3 b, Vec3 c, Vec3 d) { faces.Add(new MeshFace(new[] { a, b, c }, material)); faces.Add(new MeshFace(new[] { a, c, d }, material)); }
            for (int s = 0; s < centers.Count - 1; s++) for (int i = 0; i < sides; i++) { Quad(Point(s, i, 2), Point(s, i + 1, 2), Point(s + 1, i + 1, 2), Point(s + 1, i, 2)); if (inner > 0) Quad(Point(s, i + 1, inner), Point(s, i, inner), Point(s + 1, i, inner), Point(s + 1, i + 1, inner)); }
            for (int i = 0; i < sides; i++)
            {
                int last = centers.Count - 1;
                if (inner > 0) { Quad(Point(0, i + 1, 2), Point(0, i, 2), Point(0, i, inner), Point(0, i + 1, inner)); Quad(Point(last, i, 2), Point(last, i + 1, 2), Point(last, i + 1, inner), Point(last, i, inner)); }
                else { faces.Add(new MeshFace(new[] { centers[0], Point(0, i + 1, 2), Point(0, i, 2) }, material)); faces.Add(new MeshFace(new[] { centers[last], Point(last, i, 2), Point(last, i + 1, 2) }, material)); }
            }
            return new TriangleMesh(faces);
        }
    }
}
