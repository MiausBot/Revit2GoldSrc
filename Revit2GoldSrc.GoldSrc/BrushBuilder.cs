using System;
using System.Collections.Generic;
using System.Linq;
using Revit2GoldSrc.Core;
namespace Revit2GoldSrc.GoldSrc
{
    public sealed class GoldSrcFace
    {
        public Vec3 A { get; }
        public Vec3 B { get; }
        public Vec3 C { get; }
        public string Texture { get; }
        public Vec3 U { get; }
        public Vec3 V { get; }
        public double UShift { get; }
        public double VShift { get; }
        public double UScale { get; }
        public double VScale { get; }
        internal long? SourceMaterialId { get; set; }
        internal string? SourceMaterialKey {get;set;}
        internal BimMaterial? NativeMaterial {get;set;}
        internal bool NativeMaterialTagged {get;set;}
        internal bool NativeMaterialAmbiguous {get;set;}
        internal bool SourceIsPainted { get; set; }
        // GoldSrc compiler convention: (p0-p1) x (p2-p1), outward, interior <= distance.
        public Vec3 Normal => (A - B).Cross(C - B).Unit();
        public double Distance => Normal.Dot(A);
        public GoldSrcFace(Vec3 a, Vec3 b, Vec3 c, string texture, Vec3 u, Vec3 v, double uShift = 0, double vShift = 0, double uScale = 1, double vScale = 1)
        { A = a; B = b; C = c; Texture = texture; U = u; V = v; UShift = uShift; VShift = vShift; UScale = uScale; VScale = vScale; }
    }
    public sealed class GoldSrcBrush
    {
        public IReadOnlyList<GoldSrcFace> Faces { get; }
        public BrushRole Role { get; }
        public GoldSrcBrush(IReadOnlyList<GoldSrcFace> faces, BrushRole role = BrushRole.Solid) { Faces = faces; Role = role; }
    }
    public static class BrushBuilder
    {
        public static GoldSrcBrush FromConvex(ConvexSolid solid,string texture,double tolerance)
        {
            solid.Validate(tolerance);TextureMapper.ValidateName(texture);var faces=new List<GoldSrcFace>();
            foreach(var face in solid.Faces)
            {
                var normal=face.Normal;var a=face.Points[0];Vec3 b=Vec3.Zero,c=Vec3.Zero;bool found=false;
                for(int i=1;i<face.Points.Count-1;i++)if((face.Points[i]-a).Cross(face.Points[i+1]-a).Length>tolerance*tolerance){b=face.Points[i+1];c=face.Points[i];found=true;break;}
                if(!found)throw new ArgumentException("Cara de sólido degenerada.");
                var u=Math.Abs(normal.Z)>.999?Vec3.XAxis:Vec3.ZAxis.Cross(normal).Unit();var v=normal.Cross(u);
                faces.Add(new GoldSrcFace(a,b,c,texture,u,v){NativeMaterial=face.Material,NativeMaterialTagged=true,NativeMaterialAmbiguous=face.MaterialAmbiguous,SourceIsPainted=face.Painted});
            }
            var result=new GoldSrcBrush(faces);ConvexGeometryValidator.Validate(result,tolerance);return result;
        }
        public static GoldSrcBrush FromPrism(ConvexPrism prism,string texture,double tolerance)
        {
            prism.Validate(tolerance);TextureMapper.ValidateName(texture);var faces=new List<GoldSrcFace>();
            var polygon=prism.Polygon;var center=polygon.Aggregate(Vec3.Zero,(a,b)=>a+b)/polygon.Count+new Vec3(0,0,(prism.Bottom+prism.Top)/2);
            void Face(Vec3 a,Vec3 b,Vec3 c)
            {
                if((a-b).Cross(c-b).Dot(center-a)>0){var swap=b;b=c;c=swap;}
                var n=(a-b).Cross(c-b).Unit();var u=Math.Abs(n.Z)>.999?Vec3.XAxis:Vec3.ZAxis.Cross(n).Unit();var v=Math.Abs(n.Z)>.999?-Vec3.YAxis:-Vec3.ZAxis;
                faces.Add(new GoldSrcFace(a,b,c,texture,u,v));
            }
            Vec3 P(int i,double z)=>new Vec3(polygon[i].X,polygon[i].Y,z);
            // Clean convex polygons have noncollinear consecutive triples.
            Face(P(0,prism.Bottom),P(1,prism.Bottom),P(2,prism.Bottom));Face(P(0,prism.Top),P(1,prism.Top),P(2,prism.Top));
            for(int i=0;i<polygon.Count;i++)Face(P(i,prism.Bottom),P((i+1)%polygon.Count,prism.Bottom),P(i,prism.Top));
            var brush=new GoldSrcBrush(faces);var validated=ConvexGeometryValidator.Validate(brush,tolerance);
            if(Math.Abs(validated.Volume-prism.Volume)>Math.Max(prism.Volume*1e-7,tolerance*validated.Vertices.Max(p=>(p-center).Length)*validated.Vertices.Max(p=>(p-center).Length)*12))throw new ArgumentException("El brush no conserva el volumen del prisma.");
            return brush;
        }
        public static GoldSrcBrush FromBox(Box b, string texture, double tolerance)
        {
            b.Validate(tolerance); TextureMapper.ValidateName(texture);
            var faces = new List<GoldSrcFace>(); var mid = b.Frame.ToWorld((b.Min + b.Max) / 2);
            for (int axis = 0; axis < 3; axis++) for (int side = 0; side < 2; side++)
            {
                int j = (axis + 1) % 3, k = (axis + 2) % 3;
                var a = new[] { b.Min.X, b.Min.Y, b.Min.Z }; a[axis] = side == 0 ? b.Min[axis] : b.Max[axis];
                var bb = (double[])a.Clone(); bb[j] = b.Max[j]; var cc = (double[])a.Clone(); cc[k] = b.Max[k];
                Vec3 p = b.Frame.ToWorld(new Vec3(a[0], a[1], a[2]));
                Vec3 q = b.Frame.ToWorld(new Vec3(bb[0], bb[1], bb[2]));
                Vec3 s = b.Frame.ToWorld(new Vec3(cc[0], cc[1], cc[2]));
                if ((p - q).Cross(s - q).Dot(mid - p) > 0) { var swap = q; q = s; s = swap; }
                var normal = (p - q).Cross(s - q).Unit();
                Vec3 u, v;
                if (Math.Abs(normal.Z) > 0.999) { u = b.Frame.U; v = -b.Frame.V; }
                else { u = Vec3.ZAxis.Cross(normal).Unit(); v = -Vec3.ZAxis; }
                faces.Add(new GoldSrcFace(p, q, s, texture, u, v));
            }
            var brush = new GoldSrcBrush(faces); ConvexGeometryValidator.Validate(brush, tolerance); return brush;
        }
    }
    public sealed class ValidatedBrush
    {
        public IReadOnlyList<Vec3> Vertices { get; }
        public double Volume { get; }
        internal ValidatedBrush(IReadOnlyList<Vec3> vertices, double volume) { Vertices = vertices; Volume = volume; }
    }
    public static class ConvexGeometryValidator
    {
        public static ValidatedBrush Validate(GoldSrcBrush brush, double t)
        {
            if (!Vec3.Finite(t) || t <= 0 || brush.Faces.Count < 4 || brush.Faces.Count > 128) throw new ArgumentException("Brush sin suficientes planos o demasiado complejo.");
            var faces = brush.Faces;
            foreach (var f in faces)
            {
                if (!f.A.IsFinite || !f.B.IsFinite || !f.C.IsFinite || !f.U.IsFinite || !f.V.IsFinite || !Vec3.Finite(f.UShift) || !Vec3.Finite(f.VShift) ||
                   !Vec3.Finite(f.UScale) || !Vec3.Finite(f.VScale) || Math.Abs(f.UScale) < 1e-12 || Math.Abs(f.VScale) < 1e-12)
                    throw new ArgumentException("Cara o textura no finita o degenerada.");
                TextureMapper.ValidateName(f.Texture);
                if ((f.A - f.B).Cross(f.C - f.B).Length <= t * t) throw new ArgumentException("Puntos de plano colineales.");
                if (Math.Abs(f.Normal.Dot(f.U.Cross(f.V))) < 1e-8) throw new ArgumentException("Ejes de textura degenerados.");
            }
            var normals = faces.Select(f => f.Normal).ToArray(); var d = faces.Select(f => f.Distance).ToArray();
            for (int i = 0; i < faces.Count; i++) for (int j = i + 1; j < faces.Count; j++)
                if ((normals[i] - normals[j]).Length < 1e-9 && Math.Abs(d[i] - d[j]) <= t) throw new ArgumentException("Planos duplicados.");
            var vertices = new List<Vec3>();
            for (int i = 0; i < faces.Count; i++) for (int j = i + 1; j < faces.Count; j++) for (int k = j + 1; k < faces.Count; k++)
            {
                double det = normals[i].Dot(normals[j].Cross(normals[k])); if (Math.Abs(det) < 1e-10) continue;
                var p = (normals[j].Cross(normals[k]) * d[i] + normals[k].Cross(normals[i]) * d[j] + normals[i].Cross(normals[j]) * d[k]) / det;
                if (p.IsFinite && Enumerable.Range(0, faces.Count).All(a => normals[a].Dot(p) <= d[a] + t) && vertices.All(v => (p - v).Length > t)) vertices.Add(p);
            }
            if (vertices.Count < 4) throw new ArgumentException("Brush vacío, invertido o degenerado.");
            var center = vertices.Aggregate(Vec3.Zero, (sum, p) => sum + p) / vertices.Count;
            var edges = new Dictionary<string, int>(); double volume = 0;
            for (int f = 0; f < faces.Count; f++)
            {
                var indices = Enumerable.Range(0, vertices.Count).Where(i => Math.Abs(normals[f].Dot(vertices[i]) - d[f]) <= t * 2).ToList();
                if (indices.Count < 3) throw new ArgumentException("Plano redundante o sólido abierto.");
                var fc = indices.Aggregate(Vec3.Zero, (sum, i) => sum + vertices[i]) / indices.Count;
                var u = (vertices[indices[0]] - fc).Unit(); var v = normals[f].Cross(u);
                indices = indices.OrderBy(i => Math.Atan2((vertices[i] - fc).Dot(v), (vertices[i] - fc).Dot(u))).ToList();
                for (int i = 0; i < indices.Count; i++)
                {
                    int a = indices[i], b = indices[(i + 1) % indices.Count]; string key = Math.Min(a, b) + ":" + Math.Max(a, b);
                    edges[key] = edges.TryGetValue(key, out int count) ? count + 1 : 1;
                }
                for (int i = 1; i < indices.Count - 1; i++)
                    volume += (vertices[indices[0]] - center).Dot((vertices[indices[i]] - center).Cross(vertices[indices[i + 1]] - center)) / 6;
            }
            if (edges.Values.Any(count => count != 2) || !Vec3.Finite(volume) || volume <= t * t * t) throw new ArgumentException("Brush no cerrado o sin volumen positivo.");
            return new ValidatedBrush(vertices, volume);
        }
        public static bool IntersectsPlayer(GoldSrcBrush b, Vec3 player, double clearance = 0.1)
        {
            // Conservative halfspace test with the GoldSrc standing hull (32 x 32 x 72 HU).
            // False positives are acceptable: no false negative collision placement.
            return b.Faces.All(f => f.Normal.Dot(player) - f.Distance <= Math.Abs(f.Normal.X) * 16 + Math.Abs(f.Normal.Y) * 16 + Math.Abs(f.Normal.Z) * 36 + clearance);
        }
    }
}
