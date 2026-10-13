using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Revit2GoldSrc.Core;
namespace Revit2GoldSrc.GoldSrc
{
    public sealed class WadTexture
    {
        public string Name { get; internal set; }
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<byte[]> Mips { get; }
        public byte[] Palette { get; }
        public string ContentHash { get; }
        internal WadTexture(int width, int height, IReadOnlyList<byte[]> mips, byte[] palette)
        {
            Width = width; Height = height; Mips = mips; Palette = palette;
            using (var data = new MemoryStream()) using (var writer = new BinaryWriter(data))
            {
                writer.Write(width); writer.Write(height); writer.Write(palette); foreach (var mip in mips) writer.Write(mip); writer.Flush();
                using (var sha = SHA256.Create()) ContentHash = BitConverter.ToString(sha.ComputeHash(data.ToArray())).Replace("-", "");
            }
            Name = "R2G_" + ContentHash.Substring(0, 11);
        }
    }
    public static class TextureImageConverter
    {
        private sealed class ColorBin
        {
            public int Key; public int Count;
            public int Channel(int channel) => (Key >> (16 - channel * 8)) & 255;
        }
        public static WadTexture Convert(PixelImage source, RgbColor background, double imageWeight = 1)
        {
            if (!Vec3.Finite(imageWeight) || imageWeight < 0 || imageWeight > 1) throw new ArgumentException("Peso de imagen inválido.");
            double factor = Math.Min(1, 256.0 / Math.Max(source.Width, source.Height));
            int w = Math.Max(16, Math.Min(256, (int)Math.Round(source.Width * factor / 16) * 16));
            int h = Math.Max(16, Math.Min(256, (int)Math.Round(source.Height * factor / 16) * 16));
            var rgb = new byte[w * h * 3];
            // Bilinear resampling. Alpha is flattened against the material color; no transparent brush content is created.
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                double sx = Math.Max(0, Math.Min(source.Width - 1, (x + .5) * source.Width / w - .5));
                double sy = Math.Max(0, Math.Min(source.Height - 1, (y + .5) * source.Height / h - .5));
                int x0 = (int)sx, y0 = (int)sy, x1 = Math.Min(x0 + 1, source.Width - 1), y1 = Math.Min(y0 + 1, source.Height - 1);
                double fx = sx - x0, fy = sy - y0;
                for (int c = 0; c < 3; c++)
                {
                    double Sample(int xx, int yy)
                    {
                        int i = (yy * source.Width + xx) * 4; double alpha = source.Rgba[i + 3] / 255.0 * imageWeight;
                        byte bg = c == 0 ? background.R : c == 1 ? background.G : background.B;
                        return source.Rgba[i + c] * alpha + bg * (1 - alpha);
                    }
                    rgb[(y * w + x) * 3 + c] = (byte)Math.Round((Sample(x0, y0) * (1 - fx) + Sample(x1, y0) * fx) * (1 - fy) + (Sample(x0, y1) * (1 - fx) + Sample(x1, y1) * fx) * fy);
                }
            }
            var histogram = new Dictionary<int, int>();
            for (int i = 0; i < rgb.Length; i += 3) { int key = (rgb[i] << 16) | (rgb[i + 1] << 8) | rgb[i + 2]; histogram[key] = histogram.TryGetValue(key, out int n) ? n + 1 : 1; }
            var boxes = new List<List<ColorBin>> { histogram.OrderBy(p => p.Key).Select(p => new ColorBin { Key = p.Key, Count = p.Value }).ToList() };
            int Range(List<ColorBin> box, int channel) => box.Max(b => b.Channel(channel)) - box.Min(b => b.Channel(channel));
            while (boxes.Count < 256)
            {
                var split = boxes.Where(b => b.Count > 1).OrderByDescending(b => Enumerable.Range(0, 3).Max(c => Range(b, c))).ThenByDescending(b => b.Sum(v => v.Count)).FirstOrDefault();
                if (split == null) break;
                int axis = Enumerable.Range(0, 3).OrderByDescending(c => Range(split, c)).First();
                var sorted = split.OrderBy(b => b.Channel(axis)).ThenBy(b => b.Key).ToList(); int half = (sorted.Sum(b => b.Count) + 1) / 2, total = 0, cut = 0;
                while (cut < sorted.Count - 1 && total < half) { total += sorted[cut].Count; cut++; }
                boxes.Remove(split); boxes.Add(sorted.Take(cut).ToList()); boxes.Add(sorted.Skip(cut).ToList());
            }
            var palette = new byte[768];
            for (int i = 0; i < boxes.Count; i++) for (int c = 0; c < 3; c++) palette[i * 3 + c] = (byte)Math.Round(boxes[i].Sum(b => (long)b.Channel(c) * b.Count) / (double)boxes[i].Sum(b => b.Count));
            // Fill unused entries deterministically, including the exact neutral/solid color case.
            for (int i = boxes.Count; i < 256; i++) for (int c = 0; c < 3; c++) palette[i * 3 + c] = palette[c];
            var lookup = new Dictionary<int, byte>();
            byte Index(byte r, byte g, byte b)
            {
                int key = (r << 16) | (g << 8) | b; if (lookup.TryGetValue(key, out byte cached)) return cached;
                int best = 0, distance = int.MaxValue;
                for (int i = 0; i < boxes.Count; i++)
                {
                    int dr = r - palette[i * 3], dg = g - palette[i * 3 + 1], db = b - palette[i * 3 + 2], d = dr * dr + dg * dg + db * db;
                    if (d < distance) { distance = d; best = i; }
                }
                lookup[key] = (byte)best; return (byte)best;
            }
            var mips = new List<byte[]>(); int mw = w, mh = h; var pixels = rgb;
            for (int level = 0; level < 4; level++)
            {
                var indices = new byte[mw * mh]; for (int i = 0; i < indices.Length; i++) indices[i] = Index(pixels[i * 3], pixels[i * 3 + 1], pixels[i * 3 + 2]); mips.Add(indices);
                if (level == 3) break;
                var next = new byte[mw / 2 * mh / 2 * 3];
                for (int y = 0; y < mh / 2; y++) for (int x = 0; x < mw / 2; x++) for (int c = 0; c < 3; c++)
                    next[(y * (mw / 2) + x) * 3 + c] = (byte)((pixels[((y * 2) * mw + x * 2) * 3 + c] + pixels[((y * 2) * mw + x * 2 + 1) * 3 + c] + pixels[((y * 2 + 1) * mw + x * 2) * 3 + c] + pixels[((y * 2 + 1) * mw + x * 2 + 1) * 3 + c] + 2) / 4);
                pixels = next; mw /= 2; mh /= 2;
            }
            return new WadTexture(w, h, mips, palette);
        }
    }
    public static class WadWriter
    {
        public static byte[] Write(IReadOnlyList<WadTexture> textures) => WriteInternal(textures,false);
        internal static byte[] WriteTools(IReadOnlyList<WadTexture> textures) => WriteInternal(textures,true);
        private static byte[] WriteInternal(IReadOnlyList<WadTexture> textures,bool tools)
        {
            if (textures.Count == 0) throw new ArgumentException("WAD sin texturas.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var texture in textures)
            {
                if(tools) { if(texture.Name!="ORIGIN" && texture.Name!="AAATRIGGER") throw new ArgumentException("Textura técnica no admitida."); }
                else TextureMapper.ValidateSolidTexture(texture.Name);
                if (!names.Add(texture.Name) || texture.Width % 16 != 0 || texture.Height % 16 != 0 || texture.Width > 256 || texture.Height > 256 || texture.Width < 16 || texture.Height < 16 || texture.Palette.Length != 768 || texture.Mips.Count != 4)
                    throw new ArgumentException("Textura WAD inválida o nombre duplicado.");
                for (int m = 0; m < 4; m++) if (texture.Mips[m].Length != (texture.Width >> m) * (texture.Height >> m)) throw new ArgumentException("Mip inválido.");
            }
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("WAD3")); writer.Write(textures.Count); writer.Write(0);
                var entries = new List<Tuple<int, int, string>>();
                foreach (var texture in textures)
                {
                    int start = checked((int)stream.Position); Name(writer, texture.Name); writer.Write(texture.Width); writer.Write(texture.Height);
                    int offset = 40; foreach (var mip in texture.Mips) { writer.Write(offset); offset = checked(offset + mip.Length); }
                    foreach (var mip in texture.Mips) writer.Write(mip); writer.Write((ushort)256); writer.Write(texture.Palette);
                    entries.Add(Tuple.Create(start, checked((int)stream.Position - start), texture.Name));
                }
                int directory = checked((int)stream.Position);
                foreach (var entry in entries)
                { writer.Write(entry.Item1); writer.Write(entry.Item2); writer.Write(entry.Item2); writer.Write((byte)0x43); writer.Write((byte)0); writer.Write((short)0); Name(writer, entry.Item3); }
                stream.Position = 8; writer.Write(directory); writer.Flush(); return stream.ToArray();
            }
        }
        private static void Name(BinaryWriter writer, string name)
        { var bytes = new byte[16]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0); writer.Write(bytes); }
    }
}
