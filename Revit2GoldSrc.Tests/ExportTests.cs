using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using Revit2GoldSrc.Core;
using Revit2GoldSrc.GoldSrc;
using Xunit;
namespace Revit2GoldSrc.Tests
{
    public sealed class ExportTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "Revit2GoldSrc-tests-" + Guid.NewGuid().ToString("N"));
        private string Wad => Path.Combine(dir, "test.wad");
        public ExportTests() { Directory.CreateDirectory(dir); WriteWad(Wad); }
        public void Dispose() { Directory.Delete(dir, true); }
        internal static void WriteWad(string path)
        {
            using (var memory = new MemoryStream()) using (var texture = new BinaryWriter(memory))
            {
                Name(texture, "TEST"); texture.Write(16); texture.Write(16); foreach (int offset in new[] { 40, 296, 360, 376 }) texture.Write(offset);
                texture.Write(new byte[340]); texture.Write((ushort)256); texture.Write(new byte[768]); texture.Flush(); var lump = memory.ToArray();
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    writer.Write(new[] { (byte)'W', (byte)'A', (byte)'D', (byte)'3' }); writer.Write(1); writer.Write(12 + lump.Length); writer.Write(lump);
                    writer.Write(12); writer.Write(lump.Length); writer.Write(lump.Length); writer.Write((byte)0x43); writer.Write((byte)0); writer.Write((short)0); Name(writer, "TEST");
                }
            }
        }
        private static void Name(BinaryWriter writer, string name) { var bytes = new byte[16]; System.Text.Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0); writer.Write(bytes); }
        private ExportOptions Options(bool enclosed = false)
        {
            var o = new ExportOptions { WadPath = Wad, TestEnclosure = enclosed, EnclosureTexture = "TEST" }; foreach (BimCategory c in Enum.GetValues(typeof(BimCategory))) o.Textures.Add(c, "TEST"); return o;
        }
        private static Box Box(double x, double y, double z, double dx, double dy, double dz) => new Box(Frame.Identity, new Vec3(x, y, z), new Vec3(x + dx, y + dy, z + dz));
        private static List<BimElement> Room()
        {
            double f = 1 / .3048;
            Box M(double x, double y, double z, double dx, double dy, double dz) => Box(x * f, y * f, z * f, dx * f, dy * f, dz * f);
            return new List<BimElement>{
    new BimElement(1,BimCategory.Floor,new[]{M(0,0,-.2,4,5,.2)}),
    new BimElement(2,BimCategory.Wall,new[]{M(-.15,0,0,.15,5,3)}),new BimElement(3,BimCategory.Wall,new[]{M(4,0,0,.15,5,3)}),
    new BimElement(4,BimCategory.Wall,new[]{M(-.15,-.15,0,4.3,.15,3)}),new BimElement(5,BimCategory.Wall,new[]{M(-.15,5,0,4.3,.15,3)})};
        }
        [Fact]
        public void RoomPipelineProducesValidMapReportAndSafeSpawn()
        {
            var room = Room(); var report = new ExportReport { ElementsFound = room.Count, RevitVersion = "2024" }; var options = Options(true); var result = Exporter.Prepare(room, options, report);
            Assert.Equal(5, report.ElementsConverted); Assert.Equal(0, report.ElementsSkipped); Assert.Equal(11, report.Brushes); Assert.Equal(66, report.Faces); Assert.Equal(3, report.Entities);
            var world = result.Map.Entities[0]; var floor = ConvexGeometryValidator.Validate(world.Brushes[0], 1e-4);
            Assert.Equal(Coordinates.MetersToGoldSrc(4), floor.Vertices.Max(p => p.X) - floor.Vertices.Min(p => p.X), 8);
            Assert.Equal(Coordinates.MetersToGoldSrc(5), floor.Vertices.Max(p => p.Y) - floor.Vertices.Min(p => p.Y), 8);
            string spawnString = result.Map.Entities.Single(e => e.Properties["classname"] == "info_player_start").Properties["origin"];
            var values = spawnString.Split(' ').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray(); var spawn = new Vec3(values[0], values[1], values[2]);
            Assert.All(world.Brushes, b => Assert.False(ConvexGeometryValidator.IntersectsPlayer(b, spawn)));
            string target = Path.Combine(dir, "room.map"); Exporter.Publish(result, target); var parsed = MapValidator.Parse(File.ReadAllText(target), 1e-4); Assert.Equal(3, parsed.Entities.Count);
            using (var stream = File.OpenRead(Path.Combine(dir, "room_export_report.json")))
            {
                var saved = (ExportReport)new DataContractJsonSerializer(typeof(ExportReport)).ReadObject(stream)!; Assert.Equal(11, saved.Brushes); Assert.Equal("2024", saved.RevitVersion); Assert.Equal(target, saved.MapPath);
            }
            Assert.Empty(Directory.GetFiles(dir, "*.tmp")); Assert.Empty(Directory.GetFiles(dir, "*.bak"));
        }
        [Fact]
        public void InvalidElementIsOmittedAtomicallyWith64BitId()
        {
            const long id = 5000000000; var room = Room(); room.Add(new BimElement(id, BimCategory.Column, new[] { Box(0, 0, 0, 1, 1, 1), Box(1, 1, 1, 0, 1, 1) }));
            var report = new ExportReport { ElementsFound = room.Count }; var result = Exporter.Prepare(room, Options(), report);
            Assert.Equal(5, report.ElementsConverted); Assert.Equal(1, report.ElementsSkipped); Assert.Equal(5, report.Brushes); Assert.Contains(report.Diagnostics, d => d.ElementId == id);
            Assert.Equal(5, result.Map.Entities[0].Brushes.Count);
        }
        [Fact]
        public void NonFiniteAndRemoteInvalidElementsDoNotChangeValidOrigin()
        {
            var elements = Room();
            var baseline = Exporter.Prepare(elements, Options(), new ExportReport { ElementsFound = elements.Count });
            elements.Add(new BimElement(100, BimCategory.Column, new[] { Box(double.NaN, 0, 0, 1, 1, 1) }));
            elements.Add(new BimElement(101, BimCategory.Column, new[] { Box(1e10, 0, 0, 0, 1, 1) }));
            var report = new ExportReport();
            var result = Exporter.Prepare(elements, Options(), report);
            Assert.Equal(baseline.Report.OriginFeet, result.Report.OriginFeet);
            Assert.Equal(7, report.ElementsFound);
            Assert.Equal(2, report.ElementsSkipped);
            Assert.Contains(report.Diagnostics, d => d.ElementId == 100);
            Assert.Contains(report.Diagnostics, d => d.ElementId == 101);
        }
        [Fact]
        public void NoSpaceProducesWarningWithoutPlayerOrLight()
        {
            var elements = new[] { new BimElement(1, BimCategory.Floor, new[] { Box(0, 0, 0, 1, 1, .1) }) }; var report = new ExportReport { ElementsFound = 1 };
            var result = Exporter.Prepare(elements, Options(), report); Assert.Single(result.Map.Entities); Assert.Contains(report.Diagnostics, d => d.ElementId == null && d.Message.Contains("32"));
        }
        [Fact]
        public void MissingTexturesAndSpecialContentsAreRejected()
        {
            var options = Options(); options.Textures[BimCategory.Wall] = "MISSING";
            Assert.Throws<ArgumentException>(() => Exporter.Prepare(Room(), options, new ExportReport { ElementsFound = 5 }));
            var catalog = WadCatalog.Read(Wad); Assert.Contains("TEST", catalog.Textures); catalog.Require("test"); Assert.Throws<ArgumentException>(() => catalog.Require("UNKNOWN"));
            foreach (string n in new[] { "sky", "clip", "origin", "hint", "skip", "aaatrigger", "!water", "name with space", "too_long_texture_name" }) Assert.Throws<ArgumentException>(() => TextureMapper.ValidateSolidTexture(n));
        }
        [Theory]
        [InlineData(8)]
        [InlineData(12)]
        [InlineData(32)]
        [InlineData(36)]
        public void MalformedWadFieldsAreRejected(int offset)
        {
            // Invalid directory, out of bounds lump, overflowing dimension, and mip offset respectively.
            var bytes = File.ReadAllBytes(Wad);
            if (offset == 12) BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, bytes.Length - 32);
            else BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes, offset);
            File.WriteAllBytes(Wad, bytes); Assert.ThrowsAny<Exception>(() => WadCatalog.Read(Wad));
        }
        [Fact]
        public void WrongWadVersionAndTruncatedPaletteFail()
        {
            var bytes = File.ReadAllBytes(Wad); bytes[3] = (byte)'2'; File.WriteAllBytes(Wad, bytes); Assert.Throws<InvalidDataException>(() => WadCatalog.Read(Wad));
            WriteWad(Wad); bytes = File.ReadAllBytes(Wad); BitConverter.GetBytes((ushort)255).CopyTo(bytes, 12 + 380); File.WriteAllBytes(Wad, bytes); Assert.Throws<InvalidDataException>(() => WadCatalog.Read(Wad));
        }
        [Fact]
        public void PublicationRollbackPreservesExistingMapIfReportCannotBeWritten()
        {
            var result = Exporter.Prepare(Room(), Options(), new ExportReport { ElementsFound = 5 }); string target = Path.Combine(dir, "room.map"); File.WriteAllText(target, "original content");
            Directory.CreateDirectory(Path.Combine(dir, "room_export_report.json"));
            Assert.ThrowsAny<IOException>(() => Exporter.Publish(result, target)); Assert.Equal("original content", File.ReadAllText(target)); Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        [Fact]
        public void ReexportReplacesBothFilesAndRejectsNonMapTarget()
        {
            var result = Exporter.Prepare(Room(), Options(), new ExportReport { ElementsFound = 5 }); string target = Path.Combine(dir, "room.map");
            File.WriteAllText(target, "old"); File.WriteAllText(Path.Combine(dir, "room_export_report.json"), "old report"); Exporter.Publish(result, target);
            Assert.NotEqual("old", File.ReadAllText(target)); Assert.DoesNotContain("old report", File.ReadAllText(Path.Combine(dir, "room_export_report.json")));
            Assert.Throws<ArgumentException>(() => Exporter.Publish(result, Path.Combine(dir, "room.txt"))); Assert.False(File.Exists(Path.Combine(dir, "room.txt")));
        }
        [Fact]
        public void CancellationDoesNotPublishPartialResults()
        {
            Assert.Throws<OperationCanceledException>(() => Exporter.Prepare(Room(), Options(), new ExportReport { ElementsFound = 5 }, _ => throw new OperationCanceledException()));
            Assert.Empty(Directory.GetFiles(dir, "*.map"));
        }
        [Fact]
        public void StrictMapLexerRejectsDanglingQuoteAndUnknownPropertySyntax()
        {
            string text = Exporter.Prepare(Room(), Options(), new ExportReport { ElementsFound = 5 }).Text;
            Assert.Throws<InvalidDataException>(() => MapValidator.Parse(text + "\"", 1e-4));
            Assert.Throws<InvalidDataException>(() => MapValidator.Parse(text.Replace("\"mapversion\" \"220\"", "\"mapversion\" 220"), 1e-4));
        }
    }
}
