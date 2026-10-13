using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Revit2GoldSrc.Core;
using Revit2Source;

namespace Revit2Source.Compilation
{
    public sealed class GameAssets
    {
        private sealed class Entry
        {
            public string Archive = "";
            public long Offset;
            public int Length;
            public byte[] Preload = Array.Empty<byte>();
        }

        private readonly string root;
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        public GameAssets(string gameRoot)
        {
            root = gameRoot;
            var gameinfo = Path.Combine(root, "left4dead2", "gameinfo.txt");
            if (!File.Exists(gameinfo))
                return;
            var paths = KeyValues.Parse(File.ReadAllText(gameinfo)).Single().Block("FileSystem")?.Block("SearchPaths");
            foreach (var path in paths?.Children ?? new List<KvNode>())
            {
                string value = (path.Value ?? "").Replace("|gameinfo_path|", Path.Combine(root, "left4dead2") + "/");
                string folder = Path.IsPathRooted(value) ? value : Path.Combine(root, value);
                if (!Directory.Exists(folder))
                    continue;
                foreach (string vpk in Directory.GetFiles(folder, "*_dir.vpk").OrderBy(p => p))
                    Index(vpk);
            }
        }

        private void Index(string file)
        {
            using (var r = new BinaryReader(File.OpenRead(file)))
            {
                if (r.ReadUInt32() != 0x55aa1234)
                    throw new ArgumentException("VPK inválido.");
                uint version = r.ReadUInt32(), tree = r.ReadUInt32();
                if (version != 1 && version != 2)
                    throw new ArgumentException("Versión VPK no soportada.");
                int header = version == 2 ? 28 : 12;
                if (version == 2)
                    r.BaseStream.Position = 28;
                long end = header + (long)tree;
                string Z()
                {
                    var bytes = new List<byte>();
                    while (r.BaseStream.Position < end)
                    {
                        byte b = r.ReadByte();
                        if (b == 0)
                            return Encoding.UTF8.GetString(bytes.ToArray());
                        bytes.Add(b);
                        if (bytes.Count > 4096)
                            break;
                    }

                    throw new ArgumentException("Árbol VPK truncado.");
                }

                while (true)
                {
                    string ext = Z();
                    if (ext.Length == 0)
                        break;
                    while (true)
                    {
                        string path = Z();
                        if (path.Length == 0)
                            break;
                        while (true)
                        {
                            string name = Z();
                            if (name.Length == 0)
                                break;
                            r.ReadUInt32();
                            int preload = r.ReadUInt16(), archive = r.ReadUInt16();
                            uint offset = r.ReadUInt32();
                            int length = checked((int)r.ReadUInt32());
                            if (r.ReadUInt16() != 0xffff)
                                throw new ArgumentException("Entrada VPK inválida.");
                            var bytes = r.ReadBytes(preload);
                            if (bytes.Length != preload)
                                throw new ArgumentException("Preload truncado.");
                            string key = (path == " " ? "" : path + "/") + name + (ext == " " ? "" : "." + ext);
                            string archiveFile = archive == 0x7fff ? file : file.Substring(0, file.Length - 7) + archive.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + ".vpk";
                            if (!entries.ContainsKey(key))
                                entries.Add(key, new Entry { Archive = archiveFile, Offset = archive == 0x7fff ? header + tree + (long)offset : offset, Length = length, Preload = bytes });
                        }
                    }
                }
            }
        }

        public byte[]? Read(string relative)
        {
            SourceGeometry.ValidateMaterial(relative);
            if (!entries.TryGetValue(relative, out var entry))
            {
                foreach (var folder in new[]
                {
                    "update",
                    "left4dead2_dlc3",
                    "left4dead2_dlc2",
                    "left4dead2_dlc1",
                    "left4dead2",
                    "hl2"
                }

                )
                {
                    string file = Path.Combine(root, folder, relative.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(file))
                        return File.ReadAllBytes(file);
                }

                return null;
            }

            if (entry.Length + entry.Preload.Length > 32 * 1024 * 1024)
                throw new ArgumentException("Recurso demasiado grande.");
            using (var f = File.OpenRead(entry.Archive))
            {
                if (entry.Offset < 0 || entry.Offset + entry.Length > f.Length)
                    throw new ArgumentException("Recurso VPK fuera de límites.");
                var result = new byte[entry.Preload.Length + entry.Length];
                Array.Copy(entry.Preload, result, entry.Preload.Length);
                f.Position = entry.Offset;
                int count = 0;
                while (count < entry.Length)
                {
                    int read = f.Read(result, entry.Preload.Length + count, entry.Length - count);
                    if (read == 0)
                        throw new EndOfStreamException();
                    count += read;
                }

                return result;
            }
        }

        public ModelBounds? DoorBounds(string relative)
        {
            if (!relative.StartsWith("models/props_doors/checkpoint_door_", StringComparison.OrdinalIgnoreCase) || !relative.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use un modelo oficial de puerta checkpoint.");
            var bytes = Read(relative);
            if (bytes == null)
                return null;
            if (bytes.Length < 152 || BitConverter.ToInt32(bytes, 0) != 0x54534449)
                throw new ArgumentException("Modelo MDL inválido.");
            Vec3 V(int offset) => new Vec3(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4), BitConverter.ToSingle(bytes, offset + 8));
            var min = V(104);
            var max = V(116);
            if (!min.IsFinite || !max.IsFinite || max.X <= min.X || max.Y <= min.Y || max.Z <= min.Z || Math.Max((max - min).X, (max - min).Y) > 256 || (max - min).Z > 256)
                throw new ArgumentException("Límites MDL no verificables.");
            return new ModelBounds(min, max);
        }
    }
}
