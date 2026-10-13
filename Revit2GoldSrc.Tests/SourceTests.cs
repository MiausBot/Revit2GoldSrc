using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Revit2GoldSrc.Core;
using Revit2Source;
using Revit2Source.Compilation;
using Xunit;

namespace Revit2GoldSrc.Tests
{
    public sealed class L4D2ToolsFactAttribute : FactAttribute
    {
        public L4D2ToolsFactAttribute()
        {
            try
            {
                SourceToolchain.Detect().Validate();
            }
            catch (Exception ex)
            {
                Skip = "L4D2 Authoring Tools unavailable: " + ex.Message;
            }
        }
    }

    public sealed class SourceTests
    {
        private static Box B(Vec3 min, Vec3 max) => new Box(Frame.Identity, min, max);
        private static BimElement Floor() => new BimElement(1, BimCategory.Floor, new[] { B(new Vec3(-12, -12, -1), new Vec3(12, 12, 0)) });
        private static SourceExportResult FloorScene(params SourcePlacement[] placements) => SourceExporter.Prepare(new[] { Floor() }, new SourceOptions { CenterOrigin = false, Configuration = new SourceConfiguration { Placements = placements.ToList() } });
        [Fact]
        public void SourceProjectsHaveNoGoldSrcAssemblyDependency()
        {
            Assert.DoesNotContain(typeof(SourceExporter).Assembly.GetReferencedAssemblies(), a => a.Name == "Revit2GoldSrc.GoldSrc");
            Assert.DoesNotContain(typeof(SourceCompiler).Assembly.GetReferencedAssemblies(), a => a.Name == "Revit2GoldSrc.GoldSrc");
        }

        [Theory]
        [InlineData("es-PE")]
        [InlineData("de-DE")]
        [InlineData("en-US")]
        public void VmfRoundTripPreservesRoomGeometryEntitiesAndCulture(string name)
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                var result = SourceSample.Room();
                var copy = Vmf.Read(Vmf.Write(result.Map));
                Assert.Equal(9, copy.World.Solids.Count);
                Assert.Equal(18, copy.Entities.Count);
                Assert.Equal(4, copy.Entities.Count(e => e.Properties["classname"] == "info_survivor_position"));
                var door = Assert.Single(copy.Entities, e => e.Properties["classname"] == "func_door_rotating");
                Assert.Single(door.Solids);
                Assert.Equal("90", door.Properties["distance"]);
                Assert.Equal("272", door.Properties["spawnflags"]);
                Assert.DoesNotContain("wad", Vmf.Write(copy));
                Assert.DoesNotContain("ORIGIN", Vmf.Write(copy));
                Assert.Equal(2, copy.Entities.Single(e => e.Properties["classname"] == "info_director").Connections.Count);
            }
            finally
            {
                CultureInfo.CurrentCulture = old;
            }
        }

        [Fact]
        public void KeyValuesPreservesRepeatedOutputsQuotesAndPaths()
        {
            var node = new KvNode("connections").Add("OnMapSpawn", "a,Open,,0,-1").Add("OnMapSpawn", "a,Close,,1,-1");
            var text = KeyValues.Write(new[] { node, new KvNode("path", "C:\\data\\a\"b") });
            var nodes = KeyValues.Parse(text);
            Assert.Equal(2, nodes[0].Children.Count);
            Assert.Equal("C:\\data\\a\"b", nodes[1].Value);
            Assert.Throws<FormatException>(() => KeyValues.Parse("world { \"x\""));
        }

        [Fact]
        public void VmfRejectsDuplicateIdsAndGoldSrcProperties()
        {
            var doc = SourceSample.Room().Map;
            doc.Entities[0].Id = doc.World.Id;
            Assert.Throws<ArgumentException>(() => Vmf.Validate(doc, .001));
            Vmf.AssignIds(doc);
            doc.World.Properties["wad"] = "test.wad";
            Assert.Throws<ArgumentException>(() => Vmf.Validate(doc, .001));
            doc.World.Properties.Remove("wad");
            doc.Entities.Add(new SourceEntity("monster_zombie"));
            Vmf.AssignIds(doc);
            Assert.Throws<ArgumentException>(() => Vmf.Validate(doc, .001));
        }

        [Fact]
        public void VmfRejectsInvalidAxesMissingConnectionsAndCoordinates()
        {
            var doc = SourceSample.Room().Map;
            var side = doc.World.Solids[0].Sides[0];
            side.UScale = double.NaN;
            Assert.Throws<ArgumentException>(() => Vmf.Validate(doc, .001));
            side.UScale = 1;
            doc.Entities.Single(e => e.Properties["classname"] == "info_director").Connections[0].Target = "missing";
            Assert.Throws<ArgumentException>(() => Vmf.Validate(doc, .001));
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(45, false)]
        [InlineData(90, true)]
        public void RotatedAndReflectedBoxesHavePositiveSourceVolume(double degrees, bool reflect)
        {
            var frame = new Frame(new Vec3(10, 20, 30), DoorMotion.RotateVector(Vec3.XAxis, degrees) * (reflect ? -1 : 1), DoorMotion.RotateVector(Vec3.YAxis, degrees), Vec3.ZAxis);
            var box = new Box(frame, new Vec3(-1, -2, -3), new Vec3(1, 2, 3));
            var mesh = SourceGeometry.FromBox(box, 1e-6);
            var brush = SourceGeometry.Build(mesh, 1e-6);
            Assert.Equal(48, SourceGeometry.Validate(brush, 1e-6).Volume, 6);
        }

        [Fact]
        public void PrismAndSlopedConvexGeometryPreserveVolume()
        {
            var prism = new ConvexPrism(new[] { Vec3.Zero, new Vec3(3, 0, 0), new Vec3(2, 2, 0), new Vec3(0, 1, 0) }, 0, 2);
            var mesh = SourceGeometry.FromPrism(prism, 1e-6);
            Assert.Equal(prism.Volume, SourceGeometry.Validate(SourceGeometry.Build(mesh, 1e-6), 1e-6).Volume, 6);
            var tilted = new ConvexSolid(mesh.Faces.Select(f => new MeshFace(f.Points.Select(p => new Vec3(p.X, p.Y * .8 - p.Z * .6, p.Y * .6 + p.Z * .8)).ToList())).ToList());
            Assert.Equal(mesh.Volume, SourceGeometry.Validate(SourceGeometry.Build(tilted, 1e-6), 1e-6).Volume, 6);
        }

        [Fact]
        public void SourceProfileAvoidsGoldSrcHighPolygonCapSplit()
        {
            var mesh = MeshSamples.Tube(64, 0);
            var source = ConvexMeshDecomposer.Decompose(mesh, 1e-6, profile: GeometryProfile.Source);
            Assert.Single(source);
            Assert.Contains(source[0].Faces, f => f.Points.Count == 64);
            var gold = ConvexMeshDecomposer.Decompose(mesh, 1e-6);
            Assert.True(gold.Count > 1);
            Assert.Equal(source.Sum(s => s.Volume), gold.Sum(s => s.Volume), 5);
        }

        [Theory]
        [InlineData(16, 16)]
        [InlineData(32, 64)]
        [InlineData(256, 128)]
        public void VtfHeaderPixelsAndCompleteMipmapsAreValid(int width, int height)
        {
            var image = new PixelImage(width, height, Enumerable.Range(0, width * height * 4).Select(i => (byte)(i % 4 == 3 ? 255 : i % 4 == 0 ? 220 : 70)).ToArray());
            var bytes = VtfWriter.Write(image);
            VtfWriter.Validate(bytes);
            Assert.Equal(7, BitConverter.ToInt32(bytes, 4));
            Assert.Equal(2, BitConverter.ToInt32(bytes, 8));
            Assert.Equal(12, BitConverter.ToInt32(bytes, 52));
            Assert.Equal((byte)70, bytes[bytes.Length - 4]);
            Assert.Equal((byte)220, bytes[bytes.Length - 2]);
            Assert.Throws<ArgumentException>(() => VtfWriter.Validate(bytes.Take(bytes.Length - 1).ToArray()));
        }

        [Fact]
        public void LargePrismFacesAreDividedBeforeSourceSerialization()
        {
            var polygon=Enumerable.Range(0,126).Select(i=>new Vec3(12*Math.Cos(i*2*Math.PI/126),12*Math.Sin(i*2*Math.PI/126),0)).ToArray();
            var prism=new ConvexPrism(polygon,-1,0);var floor=new BimElement(1,BimCategory.Floor,Array.Empty<Box>(),prisms:new[]{prism});
            var result=SourceExporter.Prepare(new[]{floor},new SourceOptions{CenterOrigin=false});
            Assert.True(result.Map.World.Solids.Count>1);
            Assert.Equal(prism.Volume*Math.Pow(12,3),result.Map.World.Solids.Sum(s=>SourceGeometry.Validate(s,.0001).Volume),4);
            Assert.All(result.Map.World.Solids,s=>Assert.All(SourceGeometry.Validate(s,.0001).Faces,f=>Assert.InRange(f.Points.Count,3,64)));
        }
        [Fact]
        public void SurvivorSupportMustCoverCenterAndFootprintCorners()
        {
            var prism=new ConvexPrism(new[]{Vec3.Zero,new Vec3(16,0,0),new Vec3(0,16,0)},-1,0);
            var floor=new BimElement(1,BimCategory.Floor,Array.Empty<Box>(),prisms:new[]{prism});var config=new SourceConfiguration();config.Placements.Add(new SourcePlacement{Kind="Survivor",X=7.5,Y=7.5,Z=0});
            Assert.Throws<ArgumentException>(()=>SourceExporter.Prepare(new[]{floor},new SourceOptions{CenterOrigin=false,Configuration=config}));
        }
        [Fact]
        public void VtfResizingRetainsColorWithoutPaletteQuantization()
        {
            var image = VtfWriter.Resize(PixelImage.Solid(new RgbColor(101, 132, 217)), RgbColor.Neutral);
            Assert.Equal(new byte[] { 101, 132, 217, 255 }, image.Rgba.Take(4));
            var bytes = VtfWriter.Write(image);
            Assert.Equal(new byte[] { 217, 132, 101, 255 }, bytes.Skip(bytes.Length - image.Rgba.Length).Take(4));
        }

        [Fact]
        public void EquivalentImagesDeduplicateAcrossDocuments()
        {
            BimElement Item(long id, string doc, double x)
            {
                var m = new BimMaterial
                {
                    Id = 1,
                    SourceDocument = doc,
                    Color = new RgbColor(123, 124, 125)
                };
                var box = B(new Vec3(x, 0, 0), new Vec3(x + 1, 1, 1));
                return new BimElement(id, BimCategory.Column, Array.Empty<Box>(), convexSolids: new[] { new ConvexSolid(SourceGeometry.FromBox(box, 1e-6).Faces.Select(f => new MeshFace(f.Points, m)).ToList()) });
            }

            var result = SourceExporter.Prepare(new[] { Floor(), Item(2, "one", 2), Item(3, "two", 5) }, new SourceOptions { CenterOrigin = false });
            Assert.Equal(2, result.Report.Materials.Count(m => m.MaterialId == 1));
            Assert.Single(result.Report.Materials.Where(m => m.MaterialId == 1).Select(m => m.TextureName).Distinct());
        }

        [Fact]
        public void TexturePhysicalSizeTracksExportScale()
        {
            var m = new BimMaterial
            {
                Id = 4,
                WidthFeet = 2,
                HeightFeet = 3,
                RotationDegrees = 30,
                OffsetXFeet = .25
            };
            var original = SourceGeometry.FromBox(B(Vec3.Zero, new Vec3(1, 1, 1)), 1e-6);
            var item = new BimElement(2, BimCategory.Column, Array.Empty<Box>(), convexSolids: new[] { new ConvexSolid(original.Faces.Select(f => new MeshFace(f.Points, m)).ToList()) });
            var a = SourceExporter.Prepare(new[] { Floor(), item }, new SourceOptions { CenterOrigin = false, UnitsPerFoot = 12 });
            var b = SourceExporter.Prepare(new[] { Floor(), item }, new SourceOptions { CenterOrigin = false, UnitsPerFoot = 24 });
            var sideA = a.Map.World.Solids[1].Sides[0];
            var sideB = b.Map.World.Solids[1].Sides[0];
            Assert.Equal(2, sideB.UScale / sideA.UScale, 8);
            Assert.Equal(sideA.UShift, sideB.UShift, 8);
            Assert.Equal(m.WidthFeet * 12 / 16, sideA.UScale, 8);
        }

        [Fact]
        public void AmbiguousAndMissingMaterialSubstitutionsAreReported()
        {
            var m = new BimMaterial
            {
                Id = 5,
                Name = "Missing"
            };
            m.Substitutions.Add("Imagen ausente");
            var box = B(new Vec3(3, 0, 0), new Vec3(4, 1, 1));
            var mesh = SourceGeometry.FromBox(box, 1e-6);
            var item = new BimElement(2, BimCategory.Column, Array.Empty<Box>(), convexSolids: new[] { new ConvexSolid(mesh.Faces.Select((f, i) => new MeshFace(f.Points, m, painted: i == 0, materialAmbiguous: i == 1)).ToList()) });
            var result = SourceExporter.Prepare(new[] { Floor(), item }, new SourceOptions { CenterOrigin = false });
            Assert.Contains(result.Report.Diagnostics, d => d.Code == "source_material_ambiguous");
            Assert.Contains(result.Report.Diagnostics, d => d.Code == "source_material_substitution");
            Assert.Equal(1, result.Report.Materials.Single(i => i.MaterialId == 5).PaintedFacesUsed);
        }

        [Fact]
        public void SurvivorPlacementsMustHaveSupportClearanceAndDistinctOrder()
        {
            Assert.Throws<ArgumentException>(() => FloorScene(new SourcePlacement { Kind = "Survivor", X = 0, Y = 0, Z = 2 }));
            Assert.Throws<ArgumentException>(() => FloorScene(new SourcePlacement { Kind = "Survivor", X = 0, Y = 0, Z = 0, SurvivorOrder = 1 }, new SourcePlacement { Kind = "Survivor", X = 5, Y = 5, Z = 0, SurvivorOrder = 1 }));
            var result = FloorScene();
            Assert.Equal(4, result.Map.Entities.Count(e => e.Properties["classname"] == "info_survivor_position"));
        }

        [Theory]
        [InlineData("Static")]
        [InlineData("Rotating")]
        [InlineData("Sliding")]
        public void DoorKindsUseSourcePropertiesAndAreNotDuplicated(string kind)
        {
            var frame = new Frame(new Vec3(0, 0, .1), Vec3.XAxis, Vec3.YAxis, Vec3.ZAxis);
            var leaf = new Box(frame, new Vec3(-1, -.05, 0), new Vec3(1, .05, 7));
            var settings = new DoorSettings
            {
                Animated = kind != "Static",
                Kind = (DoorKind)Enum.Parse(typeof(DoorKind), kind),
                Hinge = HingeSide.Left,
                SlideDirection = SlideDirection.Right,
                SlideDistanceFeet = 3,
                Wait = -1
            };
            var door = new DoorDefinition(7, frame, settings, new[] { new DoorLeaf(leaf, HingeSide.Left) });
            var result = SourceExporter.Prepare(new[] { Floor() }, new SourceOptions { CenterOrigin = false }, doors: new[] { door });
            if (kind == "Static")
                Assert.Equal(2, result.Map.World.Solids.Count);
            else
            {
                Assert.Single(result.Map.World.Solids);
                var entity = Assert.Single(result.Map.Entities, e => e.Properties["classname"].StartsWith("func_door"));
                Assert.Equal("-1", entity.Properties["wait"]);
                if (kind == "Sliding")
                {
                    Assert.Equal("0 0 0", entity.Properties["movedir"]);
                    double lip = double.Parse(entity.Properties["lip"], CultureInfo.InvariantCulture);
                    Assert.Equal(36, 22 - lip, 8);
                }
                else
                {
                    Assert.Equal(-12, Vmf.Vector(entity.Properties["origin"]).X, 8);
                    Assert.Equal(1.2, Vmf.Vector(entity.Properties["origin"]).Z, 8);
                    Assert.Equal("90", entity.Properties["distance"]);
                }
            }
        }

        [Fact]
        public void DoorBlockedOrUnknownSweepIsOmittedWithoutFillingOpening()
        {
            var frame = new Frame(new Vec3(0, 0, .1), Vec3.XAxis, Vec3.YAxis, Vec3.ZAxis);
            var door = new DoorDefinition(7, frame, new DoorSettings { Animated = true, Hinge = HingeSide.Left }, new[] { new DoorLeaf(new Box(frame, new Vec3(-1, -.05, 0), new Vec3(1, .05, 7))) });
            var result = SourceExporter.Prepare(new[] { Floor() }, new SourceOptions { CenterOrigin = false, UncertainObstaclesFeet = new[] { B(new Vec3(-.5, .2, .1), new Vec3(.5, 1, 3)) } }, doors: new[] { door });
            Assert.DoesNotContain(result.Map.Entities, e => e.Properties["classname"] == "func_door_rotating");
            Assert.Contains(result.Report.Diagnostics, d => d.Code == "source_door_rejected");
            Assert.Single(result.Map.World.Solids);
        }

        [Fact]
        public void CheckpointWithoutVerifiedModelIsRejectedExplicitly()
        {
            var result = FloorScene(new SourcePlacement { Kind = "Checkpoint", OpeningWidthFeet = 3, OpeningHeightFeet = 7 });
            Assert.DoesNotContain(result.Map.Entities, e => e.Properties["classname"] == "prop_door_rotating_checkpoint");
            Assert.Contains(result.Report.Diagnostics, d => d.Code == "source_checkpoint_rejected");
        }

        [Fact]
        public void SourceGeometryValidationCanBeCancelledInsidePlaneAnalysis()
        {
            var brush = SourceGeometry.Build(ConvexMeshDecomposer.Decompose(MeshSamples.Tube(64, 0), 1e-6, profile: GeometryProfile.Source)[0], 1e-6);
            int checks = 0;
            Assert.Throws<OperationCanceledException>(() => SourceGeometry.Validate(brush, 1e-6, () =>
            {
                if (++checks == 2)
                    throw new OperationCanceledException();
            }));
            Assert.Equal(2, checks);
        }

        [Fact]
        public void MissingPublishedMaterialIsRejectedBeforeWritingAnything()
        {
            var result = SourceSample.Room();
            result.Assets.Clear();
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                Assert.Throws<ArgumentException>(() => SourcePublication.Publish(result, Path.Combine(folder, "room.vmf")));
                Assert.False(Directory.Exists(folder));
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void SteppedStaircaseExportsActualVolumesAndPositions()
        {
            var steps = Enumerable.Range(0, 6).Select(i => B(new Vec3(2, i * .9, 0), new Vec3(4, (i + 1) * .9, (i + 1) * .5))).ToList();
            var stair = new BimElement(2, BimCategory.Wall, steps)
            {
                CategoryName = "Stairs"
            };
            var result = SourceExporter.Prepare(new[] { Floor(), stair }, new SourceOptions { CenterOrigin = false });
            Assert.Equal(7, result.Map.World.Solids.Count);
            Assert.Equal(steps.Sum(s => s.Volume) * Math.Pow(12, 3), result.Map.World.Solids.Skip(1).Sum(s => SourceGeometry.Validate(s, .0001).Volume), 4);
        }

        [L4D2ToolsFact]
        public void CheckpointEntityUsesOnlyInstalledFgdProperties()
        {
            var fgd = FgdCatalog.Read(SourceToolchain.Detect().Fgd);
            var config = new SourceConfiguration();
            config.Placements.Add(new SourcePlacement { Kind = "Checkpoint", OpeningWidthFeet = 40.0 / 12, OpeningHeightFeet = 79.9 / 12 });
            var result = SourceExporter.Prepare(new[] { Floor() }, new SourceOptions { CenterOrigin = false, Fgd = fgd, Configuration = config, ResolveModel = model => new ModelBounds(new Vec3(0, 0, .1), new Vec3(2, 40, 80)) });
            Assert.Single(result.Map.Entities, e => e.Properties["classname"] == "prop_door_rotating_checkpoint");
            Assert.Contains(result.Report.Doors, d => d.Kind == "Checkpoint" && d.Status == "Dynamic");
        }

        [L4D2ToolsFact]
        public void FgdRejectsUnknownSpawnflagBits()
        {
            var fgd = FgdCatalog.Read(SourceToolchain.Detect().Fgd);
            var result = SourceSample.Room(fgd);
            result.Map.Entities.Single(e => e.Properties["classname"] == "func_door_rotating").Properties["spawnflags"] = "1073741824";
            Assert.Throws<ArgumentException>(() => fgd.Validate(result.Map));
        }

        [Fact]
        public void AtomicPublicationRestoresOldFilesOnFailure()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(Path.Combine(folder, "old.txt"), "old");
                Assert.Throws<IOException>(() => SourcePublication.AtomicFiles(folder, new Dictionary<string, byte[]> { { "old.txt", new byte[] { 1 } }, { "new.txt", new byte[] { 2 } } }, count =>
                {
                    if (count == 2)
                        throw new IOException("injected");
                }));
                Assert.Equal("old", File.ReadAllText(Path.Combine(folder, "old.txt")));
                Assert.False(File.Exists(Path.Combine(folder, "new.txt")));
                Assert.Throws<ArgumentException>(() => SourcePublication.AtomicFiles(folder, new Dictionary<string, byte[]> { { "../escape", new byte[] { 1 } } }));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void ConfigurationRoundTripsLongIdsAndPlacementValues()
        {
            var config = new SourceConfiguration
            {
                Document = "doc"
            };
            config.Doors.Add(new SourceDoorConfig { ElementId = 5000000000, Document = "doc", Wait = -1 });
            config.Placements.Add(new SourcePlacement { Kind = "Survivor", X = 12.3, Y = 2, Z = 1 });
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string path = Path.Combine(folder, "config.json");
                File.WriteAllBytes(path, SourcePublication.Json(config));
                var copy = SourcePublication.ReadJson<SourceConfiguration>(path);
                Assert.Equal(5000000000, copy.Doors[0].ElementId);
                Assert.Equal(12.3, copy.Placements[0].X);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void CompilerArgumentsPreserveSpacesAndTrailingSlashes()
        {
            Assert.Equal("\"a b\"", SourceCompiler.Quote("a b"));
            Assert.Equal("\"a\\\\\"", SourceCompiler.Quote("a\\"));
            Assert.Throws<ArgumentException>(() => SourceCompiler.Quote("bad\nvalue"));
        }

        [Fact]
        public void BspInspectorRejectsGoldSrcAndTruncation()
        {
            Assert.Throws<ArgumentException>(() => SourceBsp.Inspect(new byte[100]));
            var bytes = new byte[1040];
            Array.Copy(BitConverter.GetBytes(30), bytes, 4);
            Assert.Throws<ArgumentException>(() => SourceBsp.Inspect(bytes));
        }

        [L4D2ToolsFact]
        public void InstalledFgdHandlesInheritedClassesCaseAndRejectsForeignProperties()
        {
            var fgd = FgdCatalog.Read(SourceToolchain.Detect().Fgd);
            var result = SourceSample.Room(fgd);
            Assert.True(result.Report.FgdValidated);
            result.Map.Entities.First(e => e.Properties["classname"] == "light").Properties["wad"] = "bad";
            Assert.Throws<ArgumentException>(() => fgd.Validate(result.Map));
        }

        [L4D2ToolsFact]
        public void OfficialCompilersProduceSource21RoomWithTwoModels()
        {
            var tools = SourceToolchain.Detect();
            string folder = Path.Combine(Path.GetTempPath(), "Source test " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string vmf = Path.Combine(folder, "revit_l4d2_room.vmf");
                SourcePublication.Publish(SourceSample.Room(FgdCatalog.Read(tools.Fgd)), vmf);
                var compiled = SourceCompiler.CompileAsync(vmf, tools).GetAwaiter().GetResult();
                Assert.True(compiled.Published);
                Assert.Equal(new[] { "VBSP", "VVIS", "VRAD" }, compiled.Stages.Select(s => s.Name));
                var bsp = SourceBsp.Inspect(File.ReadAllBytes(compiled.BspPath));
                Assert.Equal(21, bsp.Version);
                Assert.Equal(2, bsp.Models);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [L4D2ToolsFact]
        public void OfficialVbspAcceptsLargeCapAfterSourceProfileDivision()
        {
            var tools=SourceToolchain.Detect();string folder=Path.Combine(Path.GetTempPath(),"Source cap "+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            try
            {
                var scene=SourceSample.Room(FgdCatalog.Read(tools.Fgd));var floor=DoorMotion.Bounds(SourceGeometry.Validate(scene.Map.World.Solids[0],.0001).Vertices);
                var mesh=MeshSamples.Tube(126,0);var moved=new TriangleMesh(mesh.Triangles.Select(f=>new MeshFace(f.Points.Select(p=>new Vec3(p.X*2+floor.Min.X+12,p.Y*2+floor.Min.Y+12,p.Z*3+floor.Max.Z)).ToArray())).ToArray());
                var pieces=ConvexMeshDecomposer.Decompose(moved,.0001,profile:GeometryProfile.Source);Assert.True(pieces.Count>1);
                foreach(var piece in pieces)scene.Map.World.Solids.Add(SourceGeometry.Build(piece,.0001));Vmf.AssignIds(scene.Map);
                string file=Path.Combine(folder,"revit_l4d2_room.vmf");SourcePublication.Publish(scene,file);var built=SourceCompiler.CompileAsync(file,tools).GetAwaiter().GetResult();Assert.True(built.Published);
            }
            finally{Directory.Delete(folder,true);}
        }
        [L4D2ToolsFact]
        public void CompilerLeakStopsAndPreservesExistingBsp()
        {
            var tools = SourceToolchain.Detect();
            string folder = Path.Combine(Path.GetTempPath(), "Source leak " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string vmf = Path.Combine(folder, "revit_l4d2_room.vmf");
                var scene = SourceSample.Room(FgdCatalog.Read(tools.Fgd));
                scene.Map.World.Solids.RemoveAt(1);
                Vmf.AssignIds(scene.Map);
                SourcePublication.Publish(scene, vmf);
                string bsp = Path.ChangeExtension(vmf, ".bsp");
                File.WriteAllText(bsp, "previous");
                Assert.Throws<InvalidOperationException>(() => SourceCompiler.CompileAsync(vmf, tools).GetAwaiter().GetResult());
                Assert.Equal("previous", File.ReadAllText(bsp));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private sealed class CancelProgress : IProgress<string>
        {
            private readonly CancellationTokenSource source;
            public CancelProgress(CancellationTokenSource source)
            {
                this.source = source;
            }

            public void Report(string value)
            {
                if (value.StartsWith("VVIS"))
                    source.Cancel();
            }
        }

        [L4D2ToolsFact]
        public void CompilationCancellationPreservesPreviousBsp()
        {
            var tools = SourceToolchain.Detect();
            string folder = Path.Combine(Path.GetTempPath(), "Source cancel " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string vmf = Path.Combine(folder, "revit_l4d2_room.vmf");
                SourcePublication.Publish(SourceSample.Room(FgdCatalog.Read(tools.Fgd)), vmf);
                File.WriteAllText(Path.ChangeExtension(vmf, ".bsp"), "previous");
                using (var cts = new CancellationTokenSource())
                {
                    Assert.Throws<OperationCanceledException>(() => SourceCompiler.CompileAsync(vmf, tools, new CancelProgress(cts), cts.Token).GetAwaiter().GetResult());
                }

                Assert.Equal("previous", File.ReadAllText(Path.ChangeExtension(vmf, ".bsp")));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [L4D2ToolsFact]
        public void CheckpointModelBoundsCanBeReadFromInstalledVpk()
        {
            var tools = SourceToolchain.Detect();
            var assets = new GameAssets(tools.GameRoot);
            var bounds = assets.DoorBounds("models/props_doors/checkpoint_door_01.mdl");
            Assert.NotNull(bounds);
            Assert.True(bounds!.Max.Z > bounds.Min.Z);
            Assert.Throws<ArgumentException>(() => assets.DoorBounds("models/arbitrary.mdl"));
        }
    }
}
