using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Revit2GoldSrc.Core;

namespace Revit2Source
{
    public static class VtfWriter
    {
        public static PixelImage Resize(PixelImage image, RgbColor color, double weight = 1)
        {
            int Size(int x)
            {
                int n = 16;
                while (n < x && n < 256)
                    n *= 2;
                return n;
            }

            int w = Size(image.Width), h = Size(image.Height);
            var data = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = (y * w + x) * 4, src = (Math.Min(image.Height - 1, (int)((y + .5) * image.Height / h)) * image.Width + Math.Min(image.Width - 1, (int)((x + .5) * image.Width / w))) * 4;
                    double alpha = image.Rgba[src + 3] / 255.0 * weight;
                    data[p] = (byte)Math.Round(image.Rgba[src] * alpha + color.R * (1 - alpha));
                    data[p + 1] = (byte)Math.Round(image.Rgba[src + 1] * alpha + color.G * (1 - alpha));
                    data[p + 2] = (byte)Math.Round(image.Rgba[src + 2] * alpha + color.B * (1 - alpha));
                    data[p + 3] = 255;
                }

            return new PixelImage(w, h, data);
        }

        public static byte[] Write(PixelImage image)
        {
            if (image.Width > 256 || image.Height > 256 || (image.Width & (image.Width - 1)) != 0 || (image.Height & (image.Height - 1)) != 0)
                throw new ArgumentException("VTF requiere potencias de dos hasta 256.");
            var levels = new List<PixelImage>
            {
                image
            };
            while (levels.Last().Width > 1 || levels.Last().Height > 1)
            {
                var old = levels.Last();
                int w = Math.Max(1, old.Width / 2), h = Math.Max(1, old.Height / 2);
                var data = new byte[w * h * 4];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        for (int c = 0; c < 4; c++)
                        {
                            int sum = 0;
                            for (int dy = 0; dy < 2; dy++)
                                for (int dx = 0; dx < 2; dx++)
                                    sum += old.Rgba[(Math.Min(old.Height - 1, y * 2 + dy) * old.Width + Math.Min(old.Width - 1, x * 2 + dx)) * 4 + c];
                            data[(y * w + x) * 4 + c] = (byte)(sum / 4);
                        }

                levels.Add(new PixelImage(w, h, data));
            }

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { (byte)'V', (byte)'T', (byte)'F', 0 });
                writer.Write(7);
                writer.Write(2);
                writer.Write(80);
                writer.Write((ushort)image.Width);
                writer.Write((ushort)image.Height);
                writer.Write(0u);
                writer.Write((ushort)1);
                writer.Write((ushort)0);
                writer.Write(0u);
                writer.Write(.5f);
                writer.Write(.5f);
                writer.Write(.5f);
                writer.Write(0u);
                writer.Write(1f);
                writer.Write(12);
                writer.Write((byte)levels.Count);
                writer.Write(-1);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                while (stream.Position < 80)
                    writer.Write((byte)0);
                foreach (var level in levels.AsEnumerable().Reverse())
                    for (int p = 0; p < level.Rgba.Length; p += 4)
                    {
                        writer.Write(level.Rgba[p + 2]);
                        writer.Write(level.Rgba[p + 1]);
                        writer.Write(level.Rgba[p]);
                        writer.Write((byte)255);
                    }

                var result = stream.ToArray();
                Validate(result);
                return result;
            }
        }

        public static void Validate(byte[] data)
        {
            if (data.Length < 84)
                throw new ArgumentException("VTF truncado.");
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                if (reader.ReadUInt32() != 0x00465456 || reader.ReadUInt32() != 7 || reader.ReadUInt32() != 2 || reader.ReadUInt32() != 80)
                    throw new ArgumentException("Cabecera VTF inválida.");
                int w = reader.ReadUInt16(), h = reader.ReadUInt16();
                reader.BaseStream.Position = 52;
                if (reader.ReadInt32() != 12)
                    throw new ArgumentException("Formato VTF inesperado.");
                int levels = reader.ReadByte(), expected = 1, total = 0;
                for (int x = w, y = h;; x = Math.Max(1, x / 2), y = Math.Max(1, y / 2))
                {
                    total += x * y * 4;
                    if (x == 1 && y == 1)
                        break;
                    expected++;
                }

                if (w < 1 || h < 1 || w > 256 || h > 256 || (w & (w - 1)) != 0 || (h & (h - 1)) != 0 || levels != expected || data.Length != 80 + total)
                    throw new ArgumentException("Mipmaps VTF inválidos.");
            }
        }
    }

    internal sealed class SourceMaterials
    {
        private readonly Dictionary<string, Tuple<BimMaterial, string, PixelImage, MaterialExportInfo>> registered = new Dictionary<string, Tuple<BimMaterial, string, PixelImage, MaterialExportInfo>>();
        private readonly Dictionary<string, byte[]> content = new Dictionary<string, byte[]>();
        private readonly string folder;
        private readonly SourceReport report;
        private readonly BimMaterial neutral = new BimMaterial
        {
            Id = long.MinValue,
            Name = "Neutro / caras internas",
            Color = RgbColor.Neutral
        };
        public SourceMaterials(string map, SourceReport report)
        {
            folder = "revit2source/" + map + "/";
            this.report = report;
        }

        private Tuple<BimMaterial, string, PixelImage, MaterialExportInfo> Register(BimMaterial material)
        {
            if (registered.TryGetValue(material.SourceKey, out var result))
                return result;
            material.Validate();
            var image = VtfWriter.Resize(material.Image ?? PixelImage.Solid(material.Color), material.Color, material.ImageWeight);
            var bytes = VtfWriter.Write(image);
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            string path = folder + "m_" + hash.Substring(0, 24);
            if (!content.ContainsKey(path))
            {
                content.Add(path, bytes);
            }

            var info = new MaterialExportInfo
            {
                MaterialId = material.Id,
                SourceDocument = material.SourceDocument,
                MaterialName = material.Name,
                TextureName = path,
                UsesImage = material.Image != null,
                ImagePath = material.ImagePath,
                ImageWeight = material.ImageWeight,
                WidthFeet = material.WidthFeet,
                HeightFeet = material.HeightFeet,
                RotationDegrees = material.RotationDegrees,
                OffsetXFeet = material.OffsetXFeet,
                OffsetYFeet = material.OffsetYFeet,
                Substitutions = material.Substitutions.ToList()
            };
            report.Materials.Add(info);
            foreach (var reason in info.Substitutions)
                report.Diagnostics.Add(new Diagnostic(null, material.Name + ": " + reason) { Code = "source_material_substitution" });
            result = Tuple.Create(material, path, image, info);
            registered.Add(material.SourceKey, result);
            return result;
        }

        public void Apply(SourceSolid solid, ConvexSolid original, BimElement element, Vec3 origin, double scale, double tolerance, Action? checkpoint = null)
        {
            for (int i = 0; i < solid.Sides.Count; i++)
            {
                checkpoint?.Invoke();
                var side = solid.Sides[i];
                var face = original.Faces[i];
                BimMaterial? material = face.Material;
                bool ambiguous = face.MaterialAmbiguous, painted = face.Painted;
                if (material == null && !ambiguous)
                {
                    var match = Match(face, element.MaterialSurfaces, tolerance, checkpoint);
                    material = match.Item1;
                    painted = match.Item2;
                    ambiguous = match.Item3;
                }

                if (ambiguous)
                    report.Diagnostics.Add(new Diagnostic(element.Id, "Asignación ambigua: material neutro.") { Code = "source_material_ambiguous" });
                var value = Register(ambiguous ? neutral : material ?? neutral);
                value.Item4.FacesUsed++;
                if (painted)
                    value.Item4.PaintedFacesUsed++;
                side.Material = value.Item2;
                side.SourceMaterialKey = value.Item1.SourceKey;
                side.Painted = painted;
                var m = value.Item1;
                double angle = m.RotationDegrees * Math.PI / 180;
                var u = side.U * Math.Cos(angle) + side.V * Math.Sin(angle);
                var v = side.U * (-Math.Sin(angle)) + side.V * Math.Cos(angle);
                side.U = u;
                side.V = v;
                side.UScale = m.WidthFeet * scale / value.Item3.Width;
                side.VScale = m.HeightFeet * scale / value.Item3.Height;
                var anchor = Coordinates.TransformCoordinates(element.MaterialFrame?.Origin ?? Vec3.Zero, origin, scale);
                side.UShift = (-u.Dot(anchor) - m.OffsetXFeet * scale) / side.UScale;
                side.VShift = (-v.Dot(anchor) - m.OffsetYFeet * scale) / side.VScale;
            }
        }

        private static Tuple<BimMaterial?, bool, bool> Match(MeshFace face, IReadOnlyList<MaterialSurface> surfaces, double t, Action? checkpoint)
        {
            var n = face.Normal;
            var coplanar = surfaces.Where(s => s.Normal.Dot(n) > 1 - 1e-8 && Math.Abs(s.DistanceFeet - n.Dot(face.Points[0])) <= t).ToList();
            if (coplanar.Count == 0)
                return Tuple.Create<BimMaterial?, bool, bool>(null, false, false);
            var u = Math.Abs(n.Z) > .999 ? Vec3.XAxis : Vec3.ZAxis.Cross(n).Unit();
            var v = n.Cross(u);
            Vec3 P(Vec3 p) => new Vec3(p.Dot(u), p.Dot(v), 0);
            var cells = new List<List<Vec3>>
            {
                face.Points.Select(P).ToList()
            };
            foreach (var loop in coplanar.SelectMany(s => s.WorldLoopsFeet))
                for (int i = 0; i < loop.Count; i++)
                {
                    checkpoint?.Invoke();
                    var a = P(loop[i]);
                    var b = P(loop[(i + 1) % loop.Count]);
                    if ((b - a).Length <= t)
                        continue;
                    var axis = new Vec3(-(b - a).Y, (b - a).X, 0).Unit();
                    double d = axis.Dot(a);
                    var next = new List<List<Vec3>>();
                    foreach (var cell in cells)
                    {
                        checkpoint?.Invoke();
                        if (cell.Min(p => axis.Dot(p) - d) < -t && cell.Max(p => axis.Dot(p) - d) > t)
                        {
                            next.Add(PolygonGeometry.Clean(PolygonGeometry.Clip(cell, axis, d), t));
                            next.Add(PolygonGeometry.Clean(PolygonGeometry.Clip(cell, -axis, -d), t));
                        }
                        else
                            next.Add(cell);
                    }

                    cells = next.Where(c => c.Count >= 3 && Math.Abs(PolygonGeometry.Area(c)) > t * t).ToList();
                    if (cells.Count > 20000)
                        return Tuple.Create<BimMaterial?, bool, bool>(null, false, true);
                }

            BimMaterial? material = null;
            bool painted = false;
            foreach (var cell in cells)
            {
                checkpoint?.Invoke();
                var center = cell.Aggregate(Vec3.Zero, (a, b) => a + b) / cell.Count;
                var cover = coplanar.Where(s =>
                {
                    bool inside = false;
                    foreach (var loop in s.WorldLoopsFeet)
                        if (PolygonGeometry.Inside(loop.Select(P).ToList(), center))
                            inside = !inside;
                    return inside;
                }).ToList();
                if (cover.Count == 0 || cover.Select(s => s.Material.SourceKey).Distinct().Count() != 1 || (material != null && material.SourceKey != cover[0].Material.SourceKey))
                    return Tuple.Create<BimMaterial?, bool, bool>(null, false, true);
                material = cover[0].Material;
                painted |= cover.Any(s => s.IsPainted);
            }

            return Tuple.Create(material, painted, false);
        }

        public void CompleteReport(SourceDocument map)
        {
            foreach (var value in registered.Values)
            {
                value.Item4.FacesUsed = 0;
                value.Item4.PaintedFacesUsed = 0;
            }

            foreach (var side in map.World.Solids.Concat(map.Entities.SelectMany(e => e.Solids)).SelectMany(s => s.Sides))
                if (side.SourceMaterialKey != null && registered.TryGetValue(side.SourceMaterialKey, out var value))
                {
                    value.Item4.FacesUsed++;
                    if (side.Painted)
                        value.Item4.PaintedFacesUsed++;
                }

            report.Materials = registered.Values.Select(v => v.Item4).Where(m => m.FacesUsed > 0).ToList();
        }

        public Dictionary<string, byte[]> Assets()
        {
            var result = new Dictionary<string, byte[]>();
            foreach (var asset in content)
            {
                result["materials/" + asset.Key + ".vtf"] = asset.Value;
                var root = new KvNode("LightmappedGeneric").Add("$basetexture", asset.Key).Add("$surfaceprop", "concrete");
                result["materials/" + asset.Key + ".vmt"] = Encoding.UTF8.GetBytes(KeyValues.Write(new[] { root }));
            }

            return result;
        }
    }
}
