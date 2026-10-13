using System;
using System.Collections.Generic;
using System.Linq;
using Revit2GoldSrc.Core;
using Revit2GoldSrc.GoldSrc;
using Xunit;
namespace Revit2GoldSrc.Tests
{
    public sealed class FamilyTests
    {
        internal static TriangleMesh Pipe(int sides = 16, double inner = 1, BimMaterial? material = null) => MeshSamples.Tube(sides, inner, material);
        [Fact]
        public void DecompositionCanCancelWhileWeldingAndScanningEdges()
        {
            var mesh = Pipe(128, 0); int calls = 0;
            Assert.Throws<OperationCanceledException>(() => ConvexMeshDecomposer.Decompose(mesh, 1e-6,
                () => { if (++calls == 8) throw new OperationCanceledException(); }));
            Assert.Equal(8, calls);
        }
        [Fact]
        public void DecompositionStopsAtItsTimeBudget()
        {
            Assert.Throws<ConvexMeshDecomposer.BudgetExceededException>(() =>
                ConvexMeshDecomposer.Decompose(Pipe(), 1e-6, maxDuration: TimeSpan.Zero));
        }
        [Fact]
        public void CancellationInsideConvexValidationIsNotSwallowed()
        {
            var solid = ConvexMeshDecomposer.Decompose(Pipe(16, 0), 1e-6)[0]; int calls = 0;
            Assert.Throws<OperationCanceledException>(() => solid.Validate(1e-6,
                () => { if (++calls == 2) throw new OperationCanceledException(); }));
            Assert.Equal(2, calls);
        }
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void CylinderAndHollowPipePreserveMeshVolumeAndCavity(double inner)
        {
            var mesh = Pipe(16, inner); var parts = ConvexMeshDecomposer.Decompose(mesh, 1e-6); Assert.Equal(Math.Abs(ConvexMeshDecomposer.Volume(mesh.Triangles)), parts.Sum(p => p.Volume), 6);
            foreach (var part in parts) { var brush = BrushBuilder.FromConvex(part, "TEST", 1e-6); Assert.Equal(part.Volume, ConvexGeometryValidator.Validate(brush, 1e-6).Volume, 5); if (inner > 0) Assert.False(part.Faces.All(f => f.Normal.Dot(new Vec3(0, 0, 2.5) - f.Points[0]) <= 1e-6)); }
        }
        [Fact]
        public void ElbowClosedMeshIsDecomposedWithoutLosingVolume()
        { var mesh = MeshSamples.Elbow(); var parts = ConvexMeshDecomposer.Decompose(mesh, 1e-6); Assert.Equal(Math.Abs(ConvexMeshDecomposer.Volume(mesh.Triangles)), parts.Sum(s => s.Volume), 5); Assert.True(parts.Count > 1); Assert.All(parts, p => p.Validate(1e-6)); }
        [Fact]
        public void DecorativeFurnitureDoesNotBlockPlayerAndIsGroupedByDocumentAndCategory()
        {
            var floor = new BimElement(1, BimCategory.Floor, new[] { new Box(Frame.Identity, new Vec3(-32, -32, -8), new Vec3(32, 32, 0)) });
            BimElement Furniture(long id, string doc) => new BimElement(id, BimCategory.Wall, new[] { new Box(Frame.Identity, new Vec3(-32, -32, 0), new Vec3(32, 32, 80)) }) { Collision = false, CategoryKey = "Furniture", Origin = new ElementOrigin { Document = doc, ElementId = id } };
            var result = Exporter.Prepare(new[] { floor, Furniture(2, "a"), Furniture(3, "a"), Furniture(2, "b") }, new ExportOptions { TextureSource = TextureSourceMode.RevitMaterials, CenterOrigin = false, UnitsPerFoot = 1 }, new ExportReport());
            var decorative = result.Map.Entities.Where(e => e.Properties["classname"] == "func_illusionary").ToList(); Assert.Equal(2, decorative.Count); Assert.Equal("-1", decorative[0].Properties["skin"]); Assert.Equal(3, decorative.Sum(e => e.Brushes.Count)); Assert.Contains(result.Map.Entities, e => e.Properties["classname"] == "info_player_start"); Assert.Equal(4, result.Report.ElementsConverted);
        }
        [Fact]
        public void AdjacentConvexPiecesMergeWithTheirMaterial()
        {
            var material = new BimMaterial { Id = 1, Color = new RgbColor(80, 90, 100) };
            ConvexSolid Box(double lo, double hi)
            {
                var brush = BrushBuilder.FromBox(new Box(Frame.Identity, new Vec3(lo, -1, 0), new Vec3(hi, 1, 2)), "TEST", 1e-6); var vertices = ConvexGeometryValidator.Validate(brush, 1e-6).Vertices;
                return new ConvexSolid(brush.Faces.Select(face =>
                {
                    var points = vertices.Where(p => Math.Abs(face.Normal.Dot(p) - face.Distance) < 1e-6).ToList(); var center = points.Aggregate(Vec3.Zero, (a, b) => a + b) / points.Count; var u = (points[0] - center).Unit(); var v = face.Normal.Cross(u);
                    return new MeshFace(points.OrderBy(p => Math.Atan2((p - center).Dot(v), (p - center).Dot(u))).ToList(), material);
                }).ToList());
            }
            var combined = ConvexMeshDecomposer.MergeCompatible(new[] { Box(-1, 0), Box(0, 1) }, 1e-6); var solid = Assert.Single(combined); Assert.Equal(8, solid.Volume, 6); Assert.Equal(6, solid.Faces.Count); Assert.All(solid.Faces, f => Assert.Same(material, f.Material));
        }
        [Fact]
        public void SourceIdentitySeparatesRepeatedLinksAndLongIds()
        { var a = new ElementOrigin { Document = "doc", LinkInstances = new List<long> { 1, 2 }, ElementId = 5000000000 }; var b = new ElementOrigin { Document = "doc", LinkInstances = new List<long> { 1, 3 }, ElementId = 5000000000 }; Assert.NotEqual(a.Key, b.Key); }
        [Theory]
        [InlineData(32)]
        [InlineData(64)]
        public void HighPolygonCapsAreSplitForVhlt(int sides)
        { var mesh = Pipe(sides, 0); var parts = ConvexMeshDecomposer.Decompose(mesh, 1e-6); Assert.True(parts.Count > 1); Assert.All(parts, p => Assert.All(p.Faces, f => Assert.True(f.Points.Count < 32))); Assert.Equal(Math.Abs(ConvexMeshDecomposer.Volume(mesh.Triangles)), parts.Sum(s => s.Volume), 5); }
        [Fact]
        public void TiltedConcaveEquipmentPreservesVoid()
        {
            var faces = new List<MeshFace>(); var z = Vec3.ZAxis * 2;
            void Quad(Vec3 a, Vec3 b, Vec3 c, Vec3 d) { faces.Add(new MeshFace(new[] { a, b, c })); faces.Add(new MeshFace(new[] { a, c, d })); }
            var poly = new[] { Vec3.Zero, new Vec3(4, 0, 0), new Vec3(4, 1, 0), new Vec3(1, 1, 0), new Vec3(1, 4, 0), new Vec3(0, 4, 0) };
            for (int j = 0; j < poly.Length; j++) Quad(poly[j], poly[(j + 1) % poly.Length], poly[(j + 1) % poly.Length] + z, poly[j] + z);
            foreach (var rectangle in new[] { new[] { Vec3.Zero, new Vec3(4, 0, 0), new Vec3(4, 1, 0), new Vec3(0, 1, 0) }, new[] { new Vec3(0, 1, 0), new Vec3(1, 1, 0), new Vec3(1, 4, 0), new Vec3(0, 4, 0) } })
            { Quad(rectangle[3], rectangle[2], rectangle[1], rectangle[0]); Quad(rectangle[0] + z, rectangle[1] + z, rectangle[2] + z, rectangle[3] + z); }
            Vec3 Rotate(Vec3 p) => new Vec3(p.X, p.Y * Math.Cos(.4) - p.Z * Math.Sin(.4), p.Y * Math.Sin(.4) + p.Z * Math.Cos(.4));
            var mesh = new TriangleMesh(faces.Select(f => new MeshFace(f.Points.Select(Rotate).ToList())).ToList()); var parts = ConvexMeshDecomposer.Decompose(mesh, 1e-6); Assert.Equal(14, parts.Sum(p => p.Volume), 5); var empty = Rotate(new Vec3(2, 2, 1)); Assert.DoesNotContain(parts, p => p.Faces.All(f => f.Normal.Dot(empty - f.Points[0]) <= 1e-6));
        }
        [Fact]
        public void SurfaceSamplingDistanceDetectsOffSurfacePoints()
        { var triangle = new[] { Vec3.Zero, Vec3.XAxis, Vec3.YAxis }; Assert.Equal(1, MeshQuality.Distance(new Vec3(.2, .2, 1), triangle), 8); Assert.Equal(Math.Sqrt(2), MeshQuality.Distance(new Vec3(2, 0, 1), triangle), 8); }
        [Fact]
        public void HighDetailHasSmallerVolumeError()
        { double expected = Math.PI * 4 * 5; double low = Math.Abs(ConvexMeshDecomposer.Volume(Pipe(16, 0).Triangles) / expected - 1), high = Math.Abs(ConvexMeshDecomposer.Volume(Pipe(32, 0).Triangles) / expected - 1); Assert.InRange(low, 0, new QualitySettings(GeometryQuality.Balanced).VolumeError); Assert.InRange(high, 0, new QualitySettings(GeometryQuality.High).VolumeError); Assert.True(high < low); }
        [Fact]
        public void OpenOrNonFiniteMeshIsRejected()
        { var mesh = Pipe(16, 0); Assert.Throws<ArgumentException>(() => ConvexMeshDecomposer.Decompose(new TriangleMesh(mesh.Triangles.Skip(1).ToList()), 1e-6)); var faces = mesh.Triangles.ToList(); faces[0] = new MeshFace(new[] { new Vec3(double.NaN, 0, 0), Vec3.XAxis, Vec3.YAxis }); Assert.Throws<ArgumentException>(() => ConvexMeshDecomposer.Decompose(new TriangleMesh(faces), 1e-6)); }
        [Theory]
        [InlineData(0, false)]
        [InlineData(45, false)]
        [InlineData(90, true)]
        public void MeshRotationsAndReflectionsPreserveVolume(double angle, bool mirrored)
        { var mesh = Pipe(16, 0); var transformed = new TriangleMesh(mesh.Triangles.Select(f => new MeshFace(f.Points.Select(p => DoorMotion.RotateVector(new Vec3(mirrored ? -p.X : p.X, p.Y, p.Z), angle) + new Vec3(100, 30, -20)).ToList())).ToList()); var parts = ConvexMeshDecomposer.Decompose(transformed, 1e-6); Assert.Equal(Math.Abs(ConvexMeshDecomposer.Volume(mesh.Triangles)), parts.Sum(p => p.Volume), 5); }
        [Fact]
        public void DifferentDocumentsCanUseTheSameMaterialId()
        {
            var a = new BimMaterial { Id = 15, SourceDocument = "a", Color = new RgbColor(200, 20, 20) }; var b = new BimMaterial { Id = 15, SourceDocument = "b", Color = new RgbColor(20, 20, 200) };
            var left = ConvexMeshDecomposer.Decompose(Pipe(16, 0, a), 1e-6); var right = ConvexMeshDecomposer.Decompose(Pipe(16, 0, b), 1e-6).Select(p => new ConvexSolid(p.Faces.Select(f => new MeshFace(f.Points.Select(v => v + new Vec3(8, 0, 0)).ToList(), f.Material)).ToList())).ToList();
            var result = Exporter.Prepare(new[] { new BimElement(1, BimCategory.Wall, Array.Empty<Box>(), convexSolids: left), new BimElement(2, BimCategory.Wall, Array.Empty<Box>(), convexSolids: right) }, new ExportOptions { TextureSource = TextureSourceMode.RevitMaterials, CenterOrigin = false, GenerateEntities = false }, new ExportReport());
            Assert.Equal(2, result.Report.Materials.Count(m => m.MaterialId == 15)); Assert.Equal(2, result.Report.Materials.Where(m => m.MaterialId == 15).Select(m => m.TextureName).Distinct().Count());
        }
    }
}
