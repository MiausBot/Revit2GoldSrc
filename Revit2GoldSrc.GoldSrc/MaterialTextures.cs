using System;
using System.Collections.Generic;
using System.Linq;
using Revit2GoldSrc.Core;
namespace Revit2GoldSrc.GoldSrc
{
    internal sealed class MaterialTextureSet
    {
        private readonly Dictionary<string, WadTexture> textures = new Dictionary<string, WadTexture>();
        private readonly Dictionary<string, Tuple<BimMaterial, WadTexture, MaterialExportInfo>> materials = new Dictionary<string, Tuple<BimMaterial, WadTexture, MaterialExportInfo>>();
        private readonly ExportReport report;
        private readonly BimMaterial neutral = new BimMaterial { Id = long.MinValue, Name = "Neutro / caras internas", Color = RgbColor.Neutral };
        public string NeutralName { get; }
        public MaterialTextureSet(ExportReport report) { this.report = report; NeutralName = Register(neutral).Item2.Name; }
        public IReadOnlyList<WadTexture> Textures => textures.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        private Tuple<BimMaterial, WadTexture, MaterialExportInfo> Register(BimMaterial material)
        {
            if (materials.TryGetValue(material.SourceKey, out var found)) return found;
            material.Validate(); var texture = TextureImageConverter.Convert(material.Image ?? PixelImage.Solid(material.Color), material.Color, material.ImageWeight);
            if (textures.TryGetValue(texture.ContentHash, out var duplicate)) texture = duplicate;
            else
            {
                string prefix = texture.Name; int collision = 0;
                while (textures.Values.Any(t => string.Equals(t.Name, texture.Name, StringComparison.OrdinalIgnoreCase)))
                    texture.Name = prefix.Substring(0, 10) + (++collision).ToString("D5", System.Globalization.CultureInfo.InvariantCulture);
                textures.Add(texture.ContentHash, texture);
            }
            var info = new MaterialExportInfo
            {
                MaterialId = material.Id,
                SourceDocument=material.SourceDocument,
                MaterialName = material.Name,
                TextureName = texture.Name,
                ImagePath = material.ImagePath,
                UsesImage = material.Image != null,
                WidthFeet = material.WidthFeet,
                HeightFeet = material.HeightFeet,
                RotationDegrees = material.RotationDegrees,
                OffsetXFeet = material.OffsetXFeet,
                OffsetYFeet = material.OffsetYFeet,
                ImageWeight = material.ImageWeight,
                Substitutions = material.Substitutions.ToList()
            };
            foreach (var reason in material.Substitutions) report.Diagnostics.Add(new Diagnostic(null, "Material " + material.Name + ": " + reason) { Code = "material_substitution" });
            found = Tuple.Create(material, texture, info); materials.Add(material.SourceKey, found); return found;
        }
        public void CompleteReport(MapDocument map)
        {
            foreach (var item in materials.Values) { item.Item3.FacesUsed = 0; item.Item3.PaintedFacesUsed = 0; }
            foreach (var face in map.Entities.SelectMany(e => e.Brushes).Where(b=>b.Role==BrushRole.Solid).SelectMany(b => b.Faces))
            {
                var info = materials[face.SourceMaterialKey ?? ("|"+(face.SourceMaterialId ?? neutral.Id))].Item3;
                info.FacesUsed++; if (face.SourceIsPainted) info.PaintedFacesUsed++;
            }
            report.Materials = materials.Values.Select(v => v.Item3).Where(i => i.FacesUsed > 0).OrderBy(m => m.MaterialId).ToList();
        }
        public GoldSrcBrush Apply(GoldSrcBrush brush, BimElement element, Vec3 origin, double scale, double tolerance)
        {
            var vertices = ConvexGeometryValidator.Validate(brush, tolerance * scale).Vertices;
            var result = new List<GoldSrcFace>();
            foreach (var face in brush.Faces)
            {
                var match = face.NativeMaterialTagged ? Tuple.Create<BimMaterial?,bool,bool>(face.NativeMaterial,face.SourceIsPainted,face.NativeMaterialAmbiguous) : Match(face, vertices, element.MaterialSurfaces, origin, scale, tolerance);
                if (match.Item3) report.Diagnostics.Add(new Diagnostic(element.Id, "Cara con materiales ambiguos o cobertura parcial: se usó gris neutro.") { Code = "ambiguous_face_material", Category = element.Category.ToString() });
                var registered = Register(match.Item1 ?? neutral); var material = registered.Item1; var texture = registered.Item2;
                var mapped = WithMapping(face, texture.Name, texture.Width, texture.Height, material, element.MaterialFrame?.Origin ?? Vec3.Zero, origin, scale);
                mapped.SourceMaterialId = material.Id; mapped.SourceMaterialKey=material.SourceKey; mapped.SourceIsPainted = match.Item2;
                result.Add(mapped);
            }
            return new GoldSrcBrush(result);
        }
        internal static GoldSrcFace WithMapping(GoldSrcFace face, string name, int width, int height, BimMaterial material, Vec3 anchorFeet, Vec3 exportOriginFeet, double unitsPerFoot)
        {
            material.Validate(); double angle = material.RotationDegrees * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
            var u = face.U * c + face.V * s; var v = face.U * (-s) + face.V * c;
            double sx = material.WidthFeet * unitsPerFoot / width, sy = material.HeightFeet * unitsPerFoot / height;
            var anchor = Coordinates.TransformCoordinates(anchorFeet, exportOriginFeet, unitsPerFoot);
            double shiftU = -u.Dot(anchor) / sx - material.OffsetXFeet * unitsPerFoot / sx;
            double shiftV = -v.Dot(anchor) / sy - material.OffsetYFeet * unitsPerFoot / sy;
            return new GoldSrcFace(face.A, face.B, face.C, name, u, v, shiftU, shiftV, sx, sy);
        }
        private static Tuple<BimMaterial?, bool, bool> Match(GoldSrcFace face, IReadOnlyList<Vec3> vertices, IReadOnlyList<MaterialSurface> surfaces, Vec3 origin, double scale, double tolerance)
        {
            var normal = face.Normal; double distance = normal.Dot(face.A / scale + origin);
            var coplanar = surfaces.Where(s => s.Normal.Dot(normal) > 1 - 1e-8 && Math.Abs(s.DistanceFeet - distance) <= tolerance).ToList();
            if (coplanar.Count == 0) return Tuple.Create<BimMaterial?, bool, bool>(null, false, false);
            var corners = vertices.Where(v => Math.Abs(normal.Dot(v) - face.Distance) <= tolerance * scale * 2).Select(v => v / scale + origin).ToList();
            double xmin = corners.Min(v => v.Dot(face.U)), xmax = corners.Max(v => v.Dot(face.U)), ymin = corners.Min(v => v.Dot(face.V)), ymax = corners.Max(v => v.Dot(face.V));
            var anchor=corners.Aggregate(Vec3.Zero,(a,b)=>a+b)/corners.Count;
            var projected=corners.Select(p=>new Vec3(p.Dot(face.U),p.Dot(face.V),0)).OrderBy(p=>Math.Atan2(p.Y-anchor.Dot(face.V),p.X-anchor.Dot(face.U))).ToList();
            if(Math.Abs(Math.Abs(PolygonGeometry.Area(projected))-(xmax-xmin)*(ymax-ymin))>tolerance*Math.Max(xmax-xmin,ymax-ymin)*4)
                return MatchPolygon(projected,coplanar,face.U,face.V,tolerance);
            List<double> Grid(int axis, double lo, double hi)
            {
                var values = new[] { lo, hi }.Concat(coplanar.SelectMany(s => s.WorldLoopsFeet).SelectMany(l => l).Select(p => p.Dot(axis == 0 ? face.U : face.V)).Where(v => v > lo + tolerance && v < hi - tolerance)).OrderBy(v => v);
                var unique = new List<double>(); foreach (var val in values) if (unique.Count == 0 || val - unique[unique.Count - 1] > tolerance) unique.Add(val); return unique;
            }
            var xs = Grid(0, xmin, xmax); var ys = Grid(1, ymin, ymax);
            if ((long)xs.Count * ys.Count > 250000) return Tuple.Create<BimMaterial?, bool, bool>(null, false, true);
            BimMaterial? selected = null; bool painted = false, uncovered = false, any = false;
            for (int x = 0; x < xs.Count - 1; x++) for (int y = 0; y < ys.Count - 1; y++)
            {
                double px = (xs[x] + xs[x + 1]) / 2, py = (ys[y] + ys[y + 1]) / 2;
                var covering = coplanar.Where(surface => Contains(surface, face.U, face.V, px, py)).ToList();
                if (covering.Count == 0) { uncovered = true; continue; }
                any = true; var ids = covering.Select(s => s.Material.SourceKey).Distinct().ToList();
                if (ids.Count != 1 || (selected != null && selected.SourceKey != ids[0])) return Tuple.Create<BimMaterial?, bool, bool>(null, false, true);
                selected = covering[0].Material; painted |= covering.Any(s => s.IsPainted);
            }
            if (!any) return Tuple.Create<BimMaterial?, bool, bool>(null, false, false);
            return uncovered ? Tuple.Create<BimMaterial?, bool, bool>(null, false, true) : Tuple.Create(selected, painted, false);
        }
        private static Tuple<BimMaterial?,bool,bool> MatchPolygon(List<Vec3> polygon,IReadOnlyList<MaterialSurface> surfaces,Vec3 u,Vec3 v,double tolerance)
        {
            var cells=new List<List<Vec3>>{polygon};
            foreach(var loop in surfaces.SelectMany(s=>s.WorldLoopsFeet))for(int i=0;i<loop.Count;i++)
            {
                var a=new Vec3(loop[i].Dot(u),loop[i].Dot(v),0);var b=new Vec3(loop[(i+1)%loop.Count].Dot(u),loop[(i+1)%loop.Count].Dot(v),0);if((b-a).Length<=tolerance)continue;
                var n=new Vec3(-(b-a).Y,(b-a).X,0).Unit();double d=n.Dot(a);var next=new List<List<Vec3>>();
                foreach(var cell in cells)
                {
                    if(cell.Min(p=>n.Dot(p)-d)<-tolerance&&cell.Max(p=>n.Dot(p)-d)>tolerance){next.Add(PolygonGeometry.Clean(PolygonGeometry.Clip(cell,n,d),tolerance));next.Add(PolygonGeometry.Clean(PolygonGeometry.Clip(cell,-n,-d),tolerance));}else next.Add(cell);
                }
                cells=next.Where(c=>c.Count>=3&&Math.Abs(PolygonGeometry.Area(c))>tolerance*tolerance).ToList();if(cells.Count>250000)return Tuple.Create<BimMaterial?,bool,bool>(null,false,true);
            }
            BimMaterial? selected=null;bool painted=false;
            foreach(var cell in cells)
            {
                var center=cell.Aggregate(Vec3.Zero,(a,b)=>a+b)/cell.Count;var cover=surfaces.Where(s=>Contains(s,u,v,center.X,center.Y)).ToList();
                if(cover.Count==0||cover.Select(s=>s.Material.SourceKey).Distinct().Count()!=1||(selected!=null&&selected.SourceKey!=cover[0].Material.SourceKey))return Tuple.Create<BimMaterial?,bool,bool>(null,false,true);
                selected=cover[0].Material;painted|=cover.Any(s=>s.IsPainted);
            }
            return Tuple.Create(selected,painted,false);
        }
        private static bool Contains(MaterialSurface surface, Vec3 u, Vec3 v, double x, double y)
        {
            bool inside = false;
            foreach (var loop in surface.WorldLoopsFeet) for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                double ax = loop[i].Dot(u), ay = loop[i].Dot(v), bx = loop[j].Dot(u), by = loop[j].Dot(v);
                if ((ay > y) != (by > y) && x < (bx - ax) * (y - ay) / (by - ay) + ax) inside = !inside;
            }
            return inside;
        }
    }
}
