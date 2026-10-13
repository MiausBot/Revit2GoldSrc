using System;
using System.Collections.Generic;
using System.Linq;
namespace Revit2GoldSrc.Core
{
    public static class MeshQuality
    {
        public static double Distance(Vec3 p, IReadOnlyList<Vec3> triangle)
        {
            var a = triangle[0]; var b = triangle[1]; var c = triangle[2]; var ab = b - a; var ac = c - a; var n = ab.Cross(ac).Unit(); double height = n.Dot(p - a); var q = p - n * height; var aq = q - a;
            double aa = ab.Dot(ab), bb = ab.Dot(ac), cc = ac.Dot(ac), d = aa * cc - bb * bb; double v = (cc * aq.Dot(ab) - bb * aq.Dot(ac)) / d, w = (aa * aq.Dot(ac) - bb * aq.Dot(ab)) / d;
            if (v >= 0 && w >= 0 && v + w <= 1) return Math.Abs(height);
            double Edge(Vec3 start, Vec3 end) { var delta = end - start; double t = Math.Max(0, Math.Min(1, (p - start).Dot(delta) / delta.Dot(delta))); return (p - start - delta * t).Length; }
            return Math.Min(Edge(a, b), Math.Min(Edge(b, c), Edge(c, a)));
        }
        public static double Distance(Vec3 point, TriangleMesh mesh) => mesh.Triangles.Min(face => Distance(point, face.Points));
    }
}
