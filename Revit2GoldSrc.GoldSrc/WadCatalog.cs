using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
namespace Revit2GoldSrc.GoldSrc
{
    public static class TextureMapper
    {
        public static void ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, @"^[A-Za-z0-9_+{}!~.\-]{1,15}$")) throw new ArgumentException("Nombre de textura WAD inválido: " + name);
        }
        public static void ValidateSolidTexture(string name)
        {
            ValidateName(name); string n = name.ToLowerInvariant();
            if (n == "sky" || n == "clip" || n == "origin" || n == "hint" || n == "skip" || n == "aaatrigger" || n.StartsWith("!", StringComparison.Ordinal))
                throw new ArgumentException("El MVP exige una textura sólida normal, sin contenido especial: " + name);
        }
    }
    public sealed class WadCatalog
    {
        public IReadOnlyCollection<string> Textures { get; }
        private WadCatalog(IReadOnlyCollection<string> textures) { Textures = textures; }
        public void Require(string name)
        {
            TextureMapper.ValidateSolidTexture(name);
            foreach (string existing in Textures) if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)) return;
            throw new ArgumentException("La textura no existe en el WAD: " + name);
        }
        public static WadCatalog Read(string path)
        {
            using (var stream = File.OpenRead(path)) return Read(stream);
        }
        public static WadCatalog Read(Stream stream)
        {
            if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("El lector WAD requiere un stream legible y posicionable.");
            stream.Position = 0;
            using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
            {
                if (stream.Length < 12 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAD3") throw new InvalidDataException("Se requiere un archivo WAD3 de GoldSrc.");
                int count = reader.ReadInt32(), dir = reader.ReadInt32();
                if (count < 0 || count > 100000 || dir < 12 || (long)dir + count * 32L > stream.Length) throw new InvalidDataException("Directorio WAD fuera de rango.");
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < count; i++)
                {
                    stream.Position = dir + i * 32L; int offset = reader.ReadInt32(), size = reader.ReadInt32(), unpacked = reader.ReadInt32();
                    byte type = reader.ReadByte(), compression = reader.ReadByte(); reader.ReadInt16(); string name = Name(reader.ReadBytes(16));
                    if (offset < 12 || size < 0 || (long)offset + size > stream.Length || ((long)offset + size > dir && offset < dir + count * 32L)) throw new InvalidDataException("Lump WAD fuera de rango o superpuesto al directorio.");
                    if (type != 0x43) continue;
                    if (compression != 0 || size != unpacked || size < 40) throw new InvalidDataException("Textura WAD comprimida o truncada.");
                    TextureMapper.ValidateName(name); stream.Position = offset; string innerName = Name(reader.ReadBytes(16));
                    uint width = reader.ReadUInt32(), height = reader.ReadUInt32(); var mips = new uint[4]; for (int m = 0; m < 4; m++) mips[m] = reader.ReadUInt32();
                    if (!string.Equals(name, innerName, StringComparison.OrdinalIgnoreCase) || width == 0 || height == 0 || width % 16 != 0 || height % 16 != 0)
                        throw new InvalidDataException("Cabecera miptex inválida.");
                    long end = 40;
                    for (int m = 0; m < 4; m++)
                    {
                        ulong length = (ulong)(width >> m) * (height >> m);
                        if (mips[m] < end || (ulong)mips[m] + length > (ulong)size) throw new InvalidDataException("Mip WAD fuera de rango.");
                        end = (long)((ulong)mips[m] + length);
                    }
                    if (end + 2 > size) throw new InvalidDataException("Paleta WAD ausente.");
                    stream.Position = offset + end; ushort colors = reader.ReadUInt16();
                    if (colors != 256 || end + 2 + colors * 3L > size) throw new InvalidDataException("Paleta WAD inválida.");
                    if (!names.Add(name)) throw new InvalidDataException("Nombre de textura WAD duplicado.");
                }
                if (names.Count == 0) throw new InvalidDataException("El WAD no contiene texturas miptex.");
                return new WadCatalog(new List<string>(names));
            }
        }
        private static string Name(byte[] bytes)
        {
            int n = Array.IndexOf(bytes, (byte)0); if (n < 0) throw new InvalidDataException("Nombre WAD sin terminador.");
            for (int i = 0; i < n; i++) if (bytes[i] > 127) throw new InvalidDataException("Nombre WAD no ASCII.");
            return Encoding.ASCII.GetString(bytes, 0, n);
        }
    }
}
