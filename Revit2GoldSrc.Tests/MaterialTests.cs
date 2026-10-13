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
    public sealed class MaterialTests
    {
        private static Box Box() => new Box(Frame.Identity, Vec3.Zero, new Vec3(10, 10, .2));
        private static BimMaterial Material(long id = 1) => new BimMaterial { Id = id, Name = "Baldosa", Color = new RgbColor(31, 93, 177), WidthFeet = .3 / .3048, HeightFeet = .6 / .3048 };
        private static IReadOnlyList<MaterialSurface> Surfaces(Box box, BimMaterial material, bool painted = false)
        {
            var center = box.Frame.ToWorld((box.Min + box.Max) / 2);
            return GeometryTests.Boundary(new[] { box }).Select(f =>
            {
                var loop = f.Loops[0].Select(box.Frame.ToWorld).ToList(); var n = (loop[1] - loop[0]).Cross(loop[2] - loop[0]).Unit();
                if (n.Dot(center - loop[0]) > 0) n = -n;
                return new MaterialSurface(n, n.Dot(loop[0]), new[] { (IReadOnlyList<Vec3>)loop }, material, painted);
            }).ToList();
        }
        private static ExportResult Prepare(Box box, IReadOnlyList<MaterialSurface> surfaces, double scale = 12, IReadOnlyList<Box>? boxes = null, Frame? materialFrame = null)
        {
            var element = new BimElement(7, BimCategory.Floor, boxes ?? new[] { box }, materialSurfaces: surfaces, materialFrame: materialFrame ?? box.Frame);
            return Exporter.Prepare(new[] { element }, new ExportOptions { TextureSource = TextureSourceMode.RevitMaterials, UnitsPerFoot = scale, GenerateEntities = false }, new ExportReport { ElementsFound = 1 });
        }
        [Fact]
        public void ColorMaterialProducesExactPaletteWadAndPerFaceAssignment()
        {
            var box = Box(); var material = Material(); var result = Prepare(box, Surfaces(box, material));
            Assert.NotNull(result.GeneratedWad); var entry = Assert.Single(result.Report.Materials, m => m.MaterialId == material.Id);
            Assert.Equal(6, entry.FacesUsed); Assert.False(entry.UsesImage);
            Assert.All(result.Map.Entities[0].Brushes[0].Faces, f => Assert.Equal(entry.TextureName, f.Texture));
            using (var stream = new MemoryStream(result.GeneratedWad!)) { var catalog = WadCatalog.Read(stream); catalog.Require(entry.TextureName); }
            var texture = TextureImageConverter.Convert(PixelImage.Solid(material.Color), material.Color);
            Assert.Equal(16, texture.Width); Assert.Equal(16, texture.Height); Assert.Equal(768, texture.Palette.Length);
            int index = texture.Mips[0][0]; Assert.Equal(material.Color.R, texture.Palette[index * 3]); Assert.Equal(material.Color.G, texture.Palette[index * 3 + 1]); Assert.Equal(material.Color.B, texture.Palette[index * 3 + 2]);
        }
        [Fact]
        public void PaintedMaterialOverridesTheAssignedSourceFace()
        {
            var box = Box(); var concrete = Material(1); var paint = Material(2); paint.Name = "Pintura"; paint.Color = new RgbColor(220, 10, 10);
            var surfaces = Surfaces(box, concrete).ToList(); var top = surfaces.FindIndex(s => s.Normal.Z > .9); var original = surfaces[top];
            surfaces[top] = new MaterialSurface(original.Normal, original.DistanceFeet, original.WorldLoopsFeet, paint, true);
            var result = Prepare(box, surfaces); var paintInfo = Assert.Single(result.Report.Materials, m => m.MaterialId == 2);
            Assert.Equal(1, paintInfo.FacesUsed); Assert.Equal(1, paintInfo.PaintedFacesUsed);
            Assert.Equal(paintInfo.TextureName, result.Map.Entities[0].Brushes[0].Faces.Single(f => f.Normal.Z > .9).Texture);
        }
        [Theory]
        [InlineData(12)]
        [InlineData(24)]
        public void TilePhysicalSizeRotationAndOffsetArePreservedAtDifferentExportScales(double scale)
        {
            double angle = Math.PI / 4; var frame = new Frame(new Vec3(1000, 2000, 30), new Vec3(Math.Cos(angle), Math.Sin(angle), 0), new Vec3(-Math.Sin(angle), Math.Cos(angle), 0), Vec3.ZAxis);
            var box = new Box(frame, Vec3.Zero, new Vec3(10, 10, .2)); var material = Material(); material.RotationDegrees = 90; material.OffsetXFeet = .1; material.OffsetYFeet = .2;
            var result = Prepare(box, Surfaces(box, material), scale); var top = result.Map.Entities[0].Brushes[0].Faces.Single(f => f.Normal.Z > .9);
            var texture = TextureImageConverter.Convert(PixelImage.Solid(material.Color), material.Color);
            Assert.Equal(texture.Width, material.WidthFeet * scale / top.UScale, 8); Assert.Equal(texture.Height, material.HeightFeet * scale / top.VScale, 8);
            Assert.True((top.U + frame.V).Length < 1e-8); Assert.True((top.V + frame.U).Length < 1e-8);
            var origin = Coordinates.NormalizeCoordinates(box.Vertices); var anchor = Coordinates.TransformCoordinates(frame.Origin, origin, scale);
            Assert.Equal(-material.OffsetXFeet * scale / top.UScale, top.U.Dot(anchor) / top.UScale + top.UShift, 8);
            Assert.Equal(-material.OffsetYFeet * scale / top.VScale, top.V.Dot(anchor) / top.VScale + top.VShift, 8);
        }
        [Fact]
        public void AdjacentBrushesHaveContinuousTextureCoordinatesAndNeutralInternalFaces()
        {
            var box = Box(); var material = Material(); var left = new Box(Frame.Identity, Vec3.Zero, new Vec3(5, 10, .2)); var right = new Box(Frame.Identity, new Vec3(5, 0, 0), new Vec3(10, 10, .2));
            var result = Prepare(box, Surfaces(box, material), boxes: new[] { left, right }); var brushes = result.Map.Entities[0].Brushes;
            var a = brushes[0].Faces.Single(f => f.Normal.Z > .9); var b = brushes[1].Faces.Single(f => f.Normal.Z > .9);
            Assert.Equal(a.Texture, b.Texture); Assert.Equal(a.UShift, b.UShift, 9); Assert.Equal(a.VShift, b.VShift, 9); Assert.Equal(a.UScale, b.UScale);
            var neutral = Assert.Single(result.Report.Materials, m => m.MaterialId == long.MinValue); Assert.Equal(2, neutral.FacesUsed);
            Assert.DoesNotContain(result.Report.Diagnostics, d => d.Code == "ambiguous_face_material");
        }
        [Fact]
        public void AmbiguousMaterialPatchesAndHolesAreNotAssignedByCentroidAlone()
        {
            var box = Box(); var a = Material(1); var b = Material(2); b.Color = new RgbColor(1, 2, 3); var surfaces = Surfaces(box, a).Where(s => s.Normal.Z < .9).ToList();
            IReadOnlyList<Vec3> Loop(double x0, double x1, double y0 = 0, double y1 = 10) => new[] { new Vec3(x0, y0, .2), new Vec3(x1, y0, .2), new Vec3(x1, y1, .2), new Vec3(x0, y1, .2) };
            surfaces.Add(new MaterialSurface(Vec3.ZAxis, .2, new[] { Loop(0, 5) }, a)); surfaces.Add(new MaterialSurface(Vec3.ZAxis, .2, new[] { Loop(5, 10) }, b));
            var result = Prepare(box, surfaces); var top = result.Map.Entities[0].Brushes[0].Faces.Single(f => f.Normal.Z > .9);
            Assert.Equal(result.Report.Materials.Single(m => m.MaterialId == long.MinValue).TextureName, top.Texture);
            Assert.Contains(result.Report.Diagnostics, d => d.Code == "ambiguous_face_material");
            surfaces.RemoveRange(surfaces.Count - 2, 2); surfaces.Add(new MaterialSurface(Vec3.ZAxis, .2, new[] { Loop(0, 10), Loop(1, 2, 1, 2) }, a));
            Assert.Contains(Prepare(box, surfaces).Report.Diagnostics, d => d.Code == "ambiguous_face_material");
        }
        [Fact]
        public void MissingImageFallsBackToMaterialColorAndReportsTheSubstitution()
        {
            var box = Box(); var material = Material(); material.ImagePath = "missing.png"; material.Substitutions.Add("Imagen ausente: missing.png. Se utilizó el color del material.");
            var result = Prepare(box, Surfaces(box, material)); var info = result.Report.Materials.Single(m => m.MaterialId == 1);
            Assert.False(info.UsesImage); Assert.Contains(result.Report.Diagnostics, d => d.Code == "material_substitution" && d.Message.Contains("missing.png"));
            Assert.Equal(1, result.Report.ElementsConverted);
        }
        [Fact]
        public void EquivalentImagesDeduplicateEvenWithDifferentMaterialNamesAndIds()
        {
            var box = Box(); var a = Material(1); var b = Material(2); b.Name = a.Name;
            var rgba = new byte[32 * 32 * 4]; for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = (byte)(i % 255); rgba[i + 1] = 120; rgba[i + 2] = 30; rgba[i + 3] = 255; }
            a.Image = new PixelImage(32, 32, rgba); b.Image = new PixelImage(32, 32, (byte[])rgba.Clone());
            var surfaces = Surfaces(box, a).ToList(); var bottom = surfaces.FindIndex(s => s.Normal.Z < -.9); var original = surfaces[bottom]; surfaces[bottom] = new MaterialSurface(original.Normal, original.DistanceFeet, original.WorldLoopsFeet, b);
            var result = Prepare(box, surfaces); Assert.Equal(result.Report.Materials.Single(m => m.MaterialId == 1).TextureName, result.Report.Materials.Single(m => m.MaterialId == 2).TextureName);
            b.Image = PixelImage.Solid(new RgbColor(1, 2, 3)); var distinct = Prepare(box, surfaces);
            Assert.NotEqual(distinct.Report.Materials.Single(m => m.MaterialId == 1).TextureName, distinct.Report.Materials.Single(m => m.MaterialId == 2).TextureName);
        }
        [Theory]
        [InlineData(17, 31)]
        [InlineData(300, 500)]
        [InlineData(1024, 256)]
        public void WadImagesHaveBoundedDimensionsFourMipsAndAreReadable(int width, int height)
        {
            var rgba = new byte[width * height * 4]; for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = (byte)(i / 4 % 251); rgba[i + 1] = (byte)(i / width % 253); rgba[i + 2] = 80; rgba[i + 3] = 255; }
            var texture = TextureImageConverter.Convert(new PixelImage(width, height, rgba), RgbColor.Neutral);
            Assert.InRange(texture.Width, 16, 256); Assert.InRange(texture.Height, 16, 256); Assert.Equal(0, texture.Width % 16); Assert.Equal(0, texture.Height % 16);
            Assert.Equal(4, texture.Mips.Count); for (int m = 0; m < 4; m++) Assert.Equal((texture.Width >> m) * (texture.Height >> m), texture.Mips[m].Length);
            Assert.InRange(texture.Name.Length, 1, 15); var bytes = WadWriter.Write(new[] { texture }); using (var stream = new MemoryStream(bytes)) Assert.Contains(texture.Name, WadCatalog.Read(stream).Textures);
            Assert.Equal(bytes, WadWriter.Write(new[] { TextureImageConverter.Convert(new PixelImage(width, height, rgba), RgbColor.Neutral) }));
        }
        [Fact]
        public void TransparentPixelsAndImageFadeFlattenAgainstMaterialColor()
        {
            var source = new PixelImage(1, 1, new byte[] { 255, 0, 0, 0 }); var color = new RgbColor(50, 70, 90); var texture = TextureImageConverter.Convert(source, color);
            Assert.Equal(new byte[] { 50, 70, 90 }, texture.Palette.Take(3));
            var opaque = new PixelImage(1, 1, new byte[] { 250, 0, 0, 255 }); var faded = TextureImageConverter.Convert(opaque, color, 0);
            Assert.Equal(texture.ContentHash, faded.ContentHash);
        }
        [Fact]
        public void DuplicateTextureNamesAndInvalidMaterialScalesAreRejected()
        {
            var texture = TextureImageConverter.Convert(PixelImage.Solid(RgbColor.Neutral), RgbColor.Neutral);
            Assert.Throws<ArgumentException>(() => WadWriter.Write(new[] { texture, texture }));
            var material = Material(); material.WidthFeet = 0; Assert.Throws<ArgumentException>(() => Prepare(Box(), Surfaces(Box(), material)));
        }
        [Fact]
        public void ResolvesMovedImagesInConfiguredFoldersAndLeavesMissingPathsUnresolved()
        {
            string dir = Path.Combine(Path.GetTempPath(), "R2GS-paths-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(dir, "Mats"));
            try
            {
                string image = Path.Combine(dir, "Mats", "floor.png"); File.WriteAllBytes(image, new byte[] { 1 });
                Assert.Equal(image, MaterialImagePaths.Resolve(Path.Combine("Mats", "floor.png"), new[] { dir }));
                Assert.Equal(image, MaterialImagePaths.Resolve(Path.Combine(dir, "old", "floor.png"), new[] { Path.Combine(dir, "Mats") }));
                Assert.Equal(image, MaterialImagePaths.Resolve("missing.png|" + image, new[] { dir }));
                Assert.Null(MaterialImagePaths.Resolve("no.png", new[] { dir }));
            }
            finally { Directory.Delete(dir, true); }
        }
        [Fact]
        public void PublishesMapWadReportAndRestoresAllOldFilesIfReportInstallationFails()
        {
            string dir = Path.Combine(Path.GetTempPath(), "R2GS-materials-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                string map = Path.Combine(dir, "building.map"), wad = Path.Combine(dir, "building_materiales.wad"), report = Path.Combine(dir, "building_export_report.json");
                var result = Prepare(Box(), Surfaces(Box(), Material())); File.WriteAllText(map, "old map"); File.WriteAllText(wad, "old wad"); Directory.CreateDirectory(report);
                Assert.ThrowsAny<IOException>(() => Exporter.Publish(result, map)); Assert.Equal("old map", File.ReadAllText(map)); Assert.Equal("old wad", File.ReadAllText(wad)); Assert.False(result.Report.MapPublished);
                Directory.Delete(report); Exporter.Publish(result, map); var parsed = MapValidator.Parse(File.ReadAllText(map), 1e-4);
                Assert.Equal(wad.Replace('\\', '/'), parsed.Entities[0].Properties["wad"]); var catalog = WadCatalog.Read(wad); foreach (var f in parsed.Entities[0].Brushes.SelectMany(b => b.Faces)) catalog.Require(f.Texture);
                using (var stream = File.OpenRead(report))
                {
                    var saved = (ExportReport)new DataContractJsonSerializer(typeof(ExportReport)).ReadObject(stream)!;
                    Assert.True(saved.MapPublished); Assert.Equal(wad, saved.GeneratedWadPath); Assert.Equal("RevitMaterials", saved.TextureSource); Assert.NotEmpty(saved.Materials);
                }
                Assert.Empty(Directory.GetFiles(dir, "*.tmp")); Assert.Empty(Directory.GetFiles(dir, "*.bak"));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
