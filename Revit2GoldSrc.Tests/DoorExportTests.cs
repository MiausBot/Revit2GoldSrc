using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Revit2GoldSrc.Core;
using Revit2GoldSrc.GoldSrc;
using Revit2GoldSrc.Compilation;
using Xunit;
namespace Revit2GoldSrc.Tests
{
    public sealed class DoorExportTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "r2g-doors-" + Guid.NewGuid().ToString("N"));
        public DoorExportTests() { Directory.CreateDirectory(dir); ExportTests.WriteWad(Path.Combine(dir, "test.wad")); }
        public void Dispose() { Directory.Delete(dir, true); }
        private ExportOptions Options(bool materials = false)
        { var o = new ExportOptions { WadPath = Path.Combine(dir, "test.wad"), UnitsPerFoot = 1, CenterOrigin = false, TextureSource = materials ? TextureSourceMode.RevitMaterials : TextureSourceMode.ExistingWad }; o.Textures.Add(BimCategory.Floor, "TEST"); o.Doors.Texture = "TEST"; return o; }
        private static BimElement Floor() => new BimElement(3, BimCategory.Floor, new[] { new Box(Frame.Identity, new Vec3(-128, -128, -8), new Vec3(128, 128, 0)) });
        private static DoorDefinition Door(IReadOnlyList<MaterialSurface>? surfaces = null, double x = 0) => new DoorDefinition(100, new Frame(new Vec3(x, 0, 0), Vec3.XAxis, Vec3.YAxis, Vec3.ZAxis), new DoorSettings { Animated = true, Hinge = HingeSide.Left }, new[] { new DoorLeaf(new Box(new Frame(new Vec3(x, 0, 0), Vec3.XAxis, Vec3.YAxis, Vec3.ZAxis), new Vec3(-18, -1, 0), new Vec3(18, 1, 80)), materials: surfaces) });
        [Fact]
        public void DisabledExporterIsByteIdenticalWithDoorInput()
        { var a = Exporter.Prepare(new[] { Floor() }, Options(), new ExportReport()); var b = Exporter.Prepare(new[] { Floor() }, Options(), new ExportReport(), doors: new[] { Door() }); Assert.Equal(a.Text, b.Text); Assert.Equal(a.Report.Brushes, b.Report.Brushes); Assert.Null(b.GeneratedToolsWad); }
        [Fact]
        public void PublishDoorWadAndMapAreIdempotentAndRoundTrip()
        { var o = Options(); o.Doors.Enabled = true; var result = Exporter.Prepare(new[] { Floor() }, o, new ExportReport(), doors: new[] { Door() }); string path = Path.Combine(dir, "doors.map"); Exporter.Publish(result, path); string first = File.ReadAllText(path); Exporter.Publish(result, path); Assert.Equal(first, File.ReadAllText(path)); Assert.Equal(2, result.Map.Entities[0].Properties["wad"].Split(';').Length); var parsed = MapValidator.Parse(first, .0001); Assert.Equal(1, parsed.Entities.SelectMany(e => e.Brushes).Count(b => b.Role == BrushRole.Origin)); Assert.Equal(3, result.Report.Brushes); Assert.True(File.Exists(result.Report.ToolsWadPath)); }
        [Fact]
        public void FailedPublishRestoresMapAndBothWads()
        {
            var o = Options(true); o.Doors.Enabled = true; var result = Exporter.Prepare(new[] { Floor() }, o, new ExportReport(), doors: new[] { Door() }); string path = Path.Combine(dir, "door.map"); File.WriteAllText(path, "old map"); File.WriteAllText(Path.Combine(dir, "door_materiales.wad"), "old material"); File.WriteAllText(Path.Combine(dir, "door_tools.wad"), "old tool"); Directory.CreateDirectory(Path.Combine(dir, "door_export_report.json"));
            Assert.ThrowsAny<IOException>(() => Exporter.Publish(result, path)); Assert.Equal("old map", File.ReadAllText(path)); Assert.Equal("old material", File.ReadAllText(Path.Combine(dir, "door_materiales.wad"))); Assert.Equal("old tool", File.ReadAllText(Path.Combine(dir, "door_tools.wad"))); Assert.False(result.Report.MapPublished);
        }
        [Fact]
        public void PaintedDoorMaterialIsPreservedAndToolsDoNotPolluteReport()
        {
            var door = Door(); var material = new BimMaterial { Id = 55, Name = "door paint", Color = new RgbColor(200, 20, 10) }; var brush = BrushBuilder.FromBox(door.Leaves[0].GeometryFeet, "TEST", .0001); var vertices = ConvexGeometryValidator.Validate(brush, .0001).Vertices;
            var surfaces = brush.Faces.Select(face =>
            {
                var points = vertices.Where(v => Math.Abs(face.Normal.Dot(v) - face.Distance) < .0001).ToList(); var center = points.Aggregate(Vec3.Zero, (a, b) => a + b) / points.Count; var u = (points[0] - center).Unit(); var v = face.Normal.Cross(u); var loop = points.OrderBy(p => Math.Atan2((p - center).Dot(v), (p - center).Dot(u))).ToList(); return new MaterialSurface(face.Normal, face.Distance, new[] { (IReadOnlyList<Vec3>)loop }, material, true);
            }).ToList(); var o = Options(true); o.Doors.Enabled = true; var result = Exporter.Prepare(new[] { Floor() }, o, new ExportReport(), doors: new[] { Door(surfaces) }); var info = result.Report.Materials.Single(m => m.MaterialId == 55); Assert.Equal(6, info.FacesUsed); Assert.Equal(6, info.PaintedFacesUsed); Assert.Equal(12, result.Report.Materials.Sum(m => m.FacesUsed)); using (var stream = new MemoryStream(result.GeneratedWad!)) Assert.DoesNotContain("ORIGIN", WadCatalog.Read(stream).Textures);
        }
        [Fact]
        public void EnclosureIncludesEntireSlidingTravel()
        {
            var o = Options(); o.Doors.Enabled = true; o.TestEnclosure = true; o.EnclosureTexture = "TEST";
            var frame = Frame.Identity; var door = new DoorDefinition(1, frame, new DoorSettings { Animated = true, Kind = DoorKind.Sliding, SlideDirection = SlideDirection.Right, SlideDistanceFeet = 500 }, new[] { new DoorLeaf(new Box(frame, new Vec3(-18, -1, 0), new Vec3(18, 1, 80))) });
            var result = Exporter.Prepare(new[] { Floor() }, o, new ExportReport(), doors: new[] { door }); double max = result.Map.Entities[0].Brushes.SelectMany(b => ConvexGeometryValidator.Validate(b, .0001).Vertices).Max(p => p.X); Assert.True(max > 518 + 128);
        }
        [Fact]
        public void ClosedLeafIsIncludedInPlayerCollisionCheck()
        {
            var o = Options(); o.Doors.Enabled = true; var floor = new BimElement(1, BimCategory.Floor, new[] { new Box(Frame.Identity, new Vec3(-16, -16, -8), new Vec3(16, 16, 0)) });
            var result = Exporter.Prepare(new[] { floor }, o, new ExportReport(), doors: new[] { Door() }); Assert.DoesNotContain(result.Map.Entities, e => e.Properties["classname"] == "info_player_start");
        }
        [Fact]
        public async Task VhltCompilesBothDoorsAndPreservesPriorBspOnFailure()
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures"); var map = MapValidator.Parse(File.ReadAllText(Path.Combine(fixture, "doors_demo.map")), .0001); map.Entities[0].Properties["wad"] = string.Join(";", new[] { "doors_demo.wad", "doors_demo_tools.wad" }.Select(f => Path.Combine(fixture, f).Replace('\\', '/')));
            string path = Path.Combine(dir, "doors.map"); File.WriteAllText(path, MapSerializer.Serialize(map, .0001)); var result = await BspCompiler.CompileAsync(path, CompilerOptions.Detect()); Assert.True(result.Published); Assert.Equal(new[] { "CSG", "BSP", "VIS", "RAD" }, result.Stages.Select(s => s.Stage)); Assert.All(result.Stages, s => Assert.Equal(0, s.ExitCode)); var inspection = BspInspector.Read(result.BspPath); Assert.Equal(30, inspection.Version); Assert.Equal(3, inspection.Models); Assert.Contains("\"origin\" \"-82 1 40\"", inspection.Entities); Assert.Contains("\"lip\" \"-4\"", inspection.Entities); Assert.Contains("\"info_player_start\"", inspection.Entities); Assert.Contains("\"light\"", inspection.Entities);
            var previous = File.ReadAllBytes(result.BspPath);
            // A syntactically valid MAP with its roof removed must stop before publishing a leaking BSP.
            var leaking = MapValidator.Parse(File.ReadAllText(path), .0001); leaking.Entities[0].Brushes.RemoveAll(b => ConvexGeometryValidator.Validate(b, .0001).Vertices.All(v => v.Z >= 128)); File.WriteAllText(path, MapSerializer.Serialize(leaking, .0001));
            await Assert.ThrowsAnyAsync<Exception>(() => BspCompiler.CompileAsync(path, CompilerOptions.Detect())); Assert.Equal(previous, File.ReadAllBytes(result.BspPath));
            var paths = CompilerOptions.Detect(); paths.Rad = Path.Combine(dir, "missing.exe"); await Assert.ThrowsAsync<ArgumentException>(() => BspCompiler.CompileAsync(path, paths)); Assert.Equal(previous, File.ReadAllBytes(result.BspPath));
            using (var cancellation = new CancellationTokenSource()) { cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BspCompiler.CompileAsync(path, CompilerOptions.Detect(), cancellation: cancellation.Token)); }
            Assert.Equal(previous, File.ReadAllBytes(result.BspPath));
        }
        [Fact] public void RejectsSourceOrTruncatedBsp() { string path = Path.Combine(dir, "bad.bsp"); File.WriteAllBytes(path, new byte[124]); Assert.Throws<InvalidDataException>(() => BspInspector.Read(path)); }
    }
}
