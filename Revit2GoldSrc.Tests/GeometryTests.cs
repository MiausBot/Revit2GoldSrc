using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Revit2GoldSrc.Core;
using Revit2GoldSrc.GoldSrc;
using Xunit;
namespace Revit2GoldSrc.Tests
{
    public sealed class GeometryTests
    {
        private const double T = 1e-6;
        private static Box Cube(double x = 0, double y = 0, double z = 0, double dx = 4, double dy = 0.15, double dz = 3) => new Box(Frame.Identity, new Vec3(x, y, z), new Vec3(x + dx, y + dy, z + dz));
        [Fact]
        public void UnitsAndOriginPreserveDimensions()
        {
            Assert.Equal(12, Coordinates.RevitFeetToGoldSrc(1)); Assert.Equal(39.37007874015748, Coordinates.MetersToGoldSrc(1), 10);
            var converted = Coordinates.Convert(Cube(1000000, 2000000, 3000000), new Vec3(1000000, 2000000, 3000000), 12);
            Assert.Equal(48, converted.Max.X - converted.Min.X); Assert.Equal(1.8, converted.Max.Y - converted.Min.Y, 7);
            Assert.Equal(0, converted.Vertices.Min(p => p.X));
        }
        [Theory]
        [InlineData(0)]
        [InlineData(45)]
        [InlineData(90)]
        public void RotatedBoxHasClosedOutwardPlanesAndVolume(double degrees)
        {
            double a = degrees * Math.PI / 180; var frame = new Frame(new Vec3(10, 20, 30), new Vec3(Math.Cos(a), Math.Sin(a), 0), new Vec3(-Math.Sin(a), Math.Cos(a), 0), Vec3.ZAxis);
            var box = new Box(frame, Vec3.Zero, new Vec3(4, 0.15, 3)); var brush = BrushBuilder.FromBox(box, "TEST", T); var valid = ConvexGeometryValidator.Validate(brush, T);
            Assert.Equal(8, valid.Vertices.Count); Assert.Equal(6, brush.Faces.Count); Assert.Equal(1.8, valid.Volume, 8);
            var mid = frame.ToWorld(new Vec3(2, .075, 1.5)); Assert.All(brush.Faces, f => Assert.True(f.Normal.Dot(mid) < f.Distance));
            Assert.All(valid.Vertices, v => Assert.Contains(box.Vertices, p => (p - v).Length < T));
            Assert.Equal(4, (frame.ToWorld(new Vec3(4, 0, 0)) - frame.Origin).Length, 8);
        }
        [Fact]
        public void ReflectedFramePreservesPlaneOrientation()
        {
            var box = new Box(new Frame(Vec3.Zero, Vec3.XAxis, -Vec3.YAxis, Vec3.ZAxis), Vec3.Zero, new Vec3(2, 3, 4));
            Assert.Equal(24, ConvexGeometryValidator.Validate(BrushBuilder.FromBox(box, "TEST", T), T).Volume, 8);
        }
        [Fact]
        public void FloorAndColumnRemainOneBrush()
        {
            foreach (var box in new[] { Cube(dx: 4, dy: 5, dz: .2), Cube(dx: .3, dy: .4, dz: 3) })
            {
                var decomposed = GeometryDecomposer.Decompose(box.Frame, Boundary(new[] { box }), box.Volume, T); Assert.Single(decomposed);
                Assert.Equal(box.Volume, ConvexGeometryValidator.Validate(BrushBuilder.FromBox(decomposed[0], "TEST", T), T).Volume, 8);
            }
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DoorAndWindowRemainEmptyAndConserveVolume(bool window)
        {
            double sill = window ? 1 : 0, top = window ? 2.2 : 2.1;
            var parts = new List<Box> { Cube(dx: 1.4), Cube(x: 2.3, dx: 1.7), Cube(x: 1.4, z: top, dx: .9, dz: 3 - top) };
            if (window) parts.Add(Cube(x: 1.4, dx: .9, dz: sill));
            var result = GeometryDecomposer.Decompose(Frame.Identity, Boundary(parts), 4 * .15 * 3 - .9 * .15 * (top - sill), T);
            Assert.Equal(window ? 4 : 3, result.Count); Assert.Equal(parts.Sum(b => b.Volume), result.Sum(b => b.Volume), 8);
            var voidPoint = new Vec3(1.85, .075, (sill + top) / 2);
            Assert.All(result, b => Assert.False(Contains(b, voidPoint)));
            foreach (var p in new[] { new Vec3(.7, .075, 1.5), new Vec3(3, .075, 1.5), new Vec3(1.85, .075, 2.7) }) Assert.Contains(result, b => Contains(b, p));
            if (window) Assert.Contains(result, b => Contains(b, new Vec3(1.85, .075, .5)));
        }
        [Fact]
        public void RotatedOffCenterDoorKeepsItsWorldPositionAndOpening()
        {
            double a = Math.PI / 4;
            var frame = new Frame(new Vec3(1000, 2000, 30), new Vec3(Math.Cos(a), Math.Sin(a), 0), new Vec3(-Math.Sin(a), Math.Cos(a), 0), Vec3.ZAxis);
            var parts = new[] { Cube(dx: .5), Cube(x: 1.4, dx: 2.6), Cube(x: .5, z: 2.1, dx: .9, dz: .9) };
            var boxes = GeometryDecomposer.Decompose(frame, Boundary(parts), 4 * .15 * 3 - .9 * .15 * 2.1, T);
            Assert.Equal(3, boxes.Count);
            var opening = frame.ToWorld(new Vec3(.95, .075, 1));
            foreach (var box in boxes)
            {
                var brush = BrushBuilder.FromBox(box, "TEST", T);
                Assert.Contains(brush.Faces, f => f.Normal.Dot(opening) > f.Distance + T);
            }
            var expected = frame.ToWorld(new Vec3(0, 0, 0));
            Assert.Contains(boxes.SelectMany(b => b.Vertices), p => (p - expected).Length < T);
        }
        [Fact]
        public void FaceLoopWithHoleIsClassifiedByParity()
        {
            // Actual window boundary with holes in the two long wall faces, not separate test boxes.
            var faces = Boundary(new[] { Cube() }).ToList();
            var holeFront = new[] { new Vec3(1.4, 0, 1), new Vec3(2.3, 0, 1), new Vec3(2.3, 0, 2.2), new Vec3(1.4, 0, 2.2) };
            var holeBack = holeFront.Select(p => p + new Vec3(0, .15, 0)).ToArray();
            for (int i = 0; i < faces.Count; i++)
            {
                var loop = faces[i].Loops[0];
                if (loop.All(p => p.Y == 0)) faces[i] = new OrthogonalFace(new IReadOnlyList<Vec3>[] { loop, holeFront });
                if (loop.All(p => p.Y == .15)) faces[i] = new OrthogonalFace(new IReadOnlyList<Vec3>[] { loop, holeBack });
            }
            faces.Add(new OrthogonalFace(new[] { (IReadOnlyList<Vec3>)new[] { holeFront[0], holeBack[0], holeBack[3], holeFront[3] } }));
            faces.Add(new OrthogonalFace(new[] { (IReadOnlyList<Vec3>)new[] { holeFront[1], holeBack[1], holeBack[2], holeFront[2] } }));
            faces.Add(new OrthogonalFace(new[] { (IReadOnlyList<Vec3>)new[] { holeFront[0], holeBack[0], holeBack[1], holeFront[1] } }));
            faces.Add(new OrthogonalFace(new[] { (IReadOnlyList<Vec3>)new[] { holeFront[3], holeBack[3], holeBack[2], holeFront[2] } }));
            var result = GeometryDecomposer.Decompose(Frame.Identity, faces, 4 * .15 * 3 - .9 * .15 * 1.2, T);
            Assert.Equal(4, result.Count); Assert.All(result, b => Assert.False(Contains(b, new Vec3(1.85, .075, 1.5))));
        }
        [Fact]
        public void DecomposerRejectsVolumeMismatchAndDiagonalEdges()
        {
            Assert.Throws<ArgumentException>(() => GeometryDecomposer.Decompose(Frame.Identity, Boundary(new[] { Cube() }), 100, T));
            var bad = Boundary(new[] { Cube() }).ToList(); var loop = bad[0].Loops[0].ToArray(); loop[0] += new Vec3(0, .05, .05); bad[0] = new OrthogonalFace(new[] { (IReadOnlyList<Vec3>)loop });
            Assert.Throws<GeometryValidationException>(() => GeometryDecomposer.Decompose(Frame.Identity, bad, 1.8, T));
        }
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void InvalidThicknessIsRejected(double height)
        { Assert.Throws<ArgumentException>(() => BrushBuilder.FromBox(Cube(dz: height), "TEST", T)); }
        [Fact]
        public void NonFiniteCoordinatesAndToleranceAreRejected()
        {
            Assert.Throws<ArgumentException>(() => Coordinates.TransformCoordinates(new Vec3(double.NaN, 0, 0), Vec3.Zero));
            Assert.Throws<ArgumentException>(() => Coordinates.TransformCoordinates(Vec3.Zero, Vec3.Zero, 0));
            Assert.Throws<ArgumentException>(() => BrushBuilder.FromBox(Cube(), "TEST", double.NaN));
        }
        [Fact]
        public void ReferenceCubeHasCompilerCompatibleNormals()
        {
            var map = MapValidator.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference_cube.map")), T);
            var valid = ConvexGeometryValidator.Validate(map.Entities[0].Brushes[0], T);
            Assert.Equal(64 * 64 * 64, valid.Volume, 7); Assert.Equal(8, valid.Vertices.Count);
            Assert.All(map.Entities[0].Brushes[0].Faces, f => Assert.True(f.Normal.Dot(new Vec3(32, 32, 32)) < f.Distance));
        }
        [Fact]
        public void MissingPlaneInvertedPlaneDuplicatePlaneAndNanFail()
        {
            var b = BrushBuilder.FromBox(Cube(), "TEST", T);
            Assert.Throws<ArgumentException>(() => ConvexGeometryValidator.Validate(new GoldSrcBrush(b.Faces.Take(5).ToList()), T));
            var faces = b.Faces.ToList(); var f = faces[0]; faces[0] = new GoldSrcFace(f.A, f.C, f.B, f.Texture, f.U, f.V);
            Assert.Throws<ArgumentException>(() => ConvexGeometryValidator.Validate(new GoldSrcBrush(faces), T));
            Assert.Throws<ArgumentException>(() => ConvexGeometryValidator.Validate(new GoldSrcBrush(b.Faces.Concat(new[] { b.Faces[0] }).ToList()), T));
            string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference_cube.map"));
            Assert.Throws<InvalidDataException>(() => MapValidator.Parse(text.Replace("64", "NaN"), T));
            Assert.Throws<InvalidDataException>(() => MapValidator.Parse(text.Substring(0, text.LastIndexOf('}')), T));
            Assert.Throws<InvalidDataException>(() => MapValidator.Parse(text + "garbage", T));
        }
        [Theory]
        [InlineData("es-PE")]
        [InlineData("de-DE")]
        public void MapRoundTripUsesInvariantNumbers(string culture)
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); var map = new MapDocument(); var world = new MapEntity("worldspawn"); world.Properties.Add("mapversion", "220"); world.Brushes.Add(BrushBuilder.FromBox(Cube(), "TEST", T)); map.Entities.Add(world);
                string text = MapSerializer.Serialize(map, T); Assert.DoesNotContain("0,15", text); var parsed = MapValidator.Parse(text, T);
                Assert.Equal(1.8, ConvexGeometryValidator.Validate(parsed.Entities[0].Brushes[0], T).Volume, 8);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
        [Fact]
        public void PlayerRequiresStandingHullClearanceAndSupport()
        {
            var floor = BrushBuilder.FromBox(Cube(dx: 200, dy: 200, dz: 16), "TEST", T);
            var spawn = EntityBuilder.FindSpawn(new[] { floor }, new[] { floor }, T); Assert.True(spawn.HasValue); Assert.Equal(53, spawn!.Value.Z);
            var roof = BrushBuilder.FromBox(Cube(z: 70, dx: 200, dy: 200, dz: 16), "TEST", T);
            Assert.Null(EntityBuilder.FindSpawn(new[] { floor }, new[] { floor, roof }, T));
            var tiny = BrushBuilder.FromBox(Cube(dx: 20, dy: 20, dz: 16), "TEST", T); Assert.Null(EntityBuilder.FindSpawn(new[] { tiny }, new[] { tiny }, T));
        }
        [Fact]
        public void EnclosureSurroundsArchitectureWithoutIntersection()
        {
            var box = Cube(dx: 200, dy: 200, dz: 120); var shell = EntityBuilder.Enclosure(box.Vertices.ToList(), "TEST", T); Assert.Equal(6, shell.Count);
            var center = new Vec3(100, 100, 60); Assert.All(shell, b => Assert.False(ConvexGeometryValidator.IntersectsPlayer(b, center)));
        }
        private static bool Contains(Box b, Vec3 p) => Enumerable.Range(0, 3).All(a => p[a] > b.Min[a] && p[a] < b.Max[a]);
        internal static IReadOnlyList<OrthogonalFace> Boundary(IEnumerable<Box> boxes)
        {
            // Independent mesh builder. Retain boundaries of disjoint touching boxes: ray parity handles internal face pairs.
            var faces = new List<OrthogonalFace>(); foreach (var b in boxes)
                for (int a = 0; a < 3; a++) for (int s = 0; s < 2; s++)
                {
                    int j = (a + 1) % 3, k = (a + 2) % 3; var loop = new List<Vec3>();
                    foreach (var pair in new[] { new[] { 0, 0 }, new[] { 1, 0 }, new[] { 1, 1 }, new[] { 0, 1 } })
                    {
                        var coords = new[] { b.Min.X, b.Min.Y, b.Min.Z }; coords[a] = s == 0 ? b.Min[a] : b.Max[a]; coords[j] = pair[0] == 0 ? b.Min[j] : b.Max[j]; coords[k] = pair[1] == 0 ? b.Min[k] : b.Max[k]; loop.Add(new Vec3(coords[0], coords[1], coords[2]));
                    }
                    faces.Add(new OrthogonalFace(new[] { (IReadOnlyList<Vec3>)loop }));
                }
            return faces;
        }
    }
}
