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
    public sealed class PrismTests
    {
        public static ExportReport Fixture()
        { using (var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "model_rejected.json"))) return (ExportReport)new DataContractJsonSerializer(typeof(ExportReport)).ReadObject(stream)!; }
        [Fact]
        public void AllElevenCapturedElementsConserveOriginalVolumeAndBounds()
        {
            var report = Fixture(); Assert.Equal(11, report.Diagnostics.Count);
            foreach (var d in report.Diagnostics) foreach (var solid in d.Geometry!)
            {
                IReadOnlyList<ConvexPrism>? prisms = null; var exception = Record.Exception(() => prisms = PlanarSectionDecomposer.Decompose(solid)); Assert.True(exception == null, "Elemento " + d.ElementId + ": " + exception);
                double expected = solid.OriginalVolumeFeet3; Assert.True(Math.Abs(prisms!.Sum(p => p.Volume) - expected) < Math.Max(expected * 1e-7, .002), "Volumen " + d.ElementId);
                var input = solid.Faces.SelectMany(f => f.Loops).SelectMany(l => l.Edges).Select(e => e.WorldStartFeet.ToVector()).ToList(); var output = prisms!.SelectMany(p => p.Vertices).ToList();
                for (int axis = 0; axis < 3; axis++) { Assert.Equal(input.Min(p => p[axis]), output.Min(p => p[axis]), 4); Assert.Equal(input.Max(p => p[axis]), output.Max(p => p[axis]), 4); }
                foreach (var prism in prisms!) { var brush = BrushBuilder.FromPrism(prism.Convert(Vec3.Zero, 12), "TEST", solid.ToleranceFeet * 12); Assert.Equal(prism.Volume * 1728, ConvexGeometryValidator.Validate(brush, solid.ToleranceFeet * 12).Volume, 3); }
            }
        }
        internal static SolidGeometryDiagnostic Capture(IReadOnlyList<IReadOnlyList<Vec3>> faces, double volume, double t = 1e-6)
        {
            var solid = new SolidGeometryDiagnostic { OriginalVolumeFeet3 = volume, ToleranceFeet = t, Frame = new DiagnosticFrame(Frame.Identity) };
            foreach (var face in faces) { var f = new DiagnosticFace { IsPlanar = true }; var loop = new DiagnosticLoop(); for (int i = 0; i < face.Count; i++) loop.Edges.Add(new DiagnosticEdge { IsLine = true, WorldStartFeet = new DiagnosticPoint(face[i]), WorldEndFeet = new DiagnosticPoint(face[(i + 1) % face.Count]) }); f.Loops.Add(loop); solid.Faces.Add(f); }
            return solid;
        }
        internal static SolidGeometryDiagnostic Extrusion(IReadOnlyList<Vec3> polygon, double height)
        {
            var faces = new List<IReadOnlyList<Vec3>> { polygon, polygon.Select(p => p + Vec3.ZAxis * height).ToList() };
            for (int i = 0; i < polygon.Count; i++) { var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count]; faces.Add(new[] { a, b, b + Vec3.ZAxis * height, a + Vec3.ZAxis * height }); }
            return Capture(faces, Math.Abs(PolygonGeometry.Area(polygon)) * height);
        }
        [Theory]
        [InlineData(0)]
        [InlineData(45)]
        [InlineData(90)]
        public void ConcaveRotatedFloorPreservesVoid(double angle)
        {
            var poly = new[] { new Vec3(0, 0, 0), new Vec3(4, 0, 0), new Vec3(4, 1, 0), new Vec3(1, 1, 0), new Vec3(1, 4, 0), new Vec3(0, 4, 0) }.Select(p => DoorMotion.RotateVector(p, angle)).ToList();
            var parts = PlanarSectionDecomposer.Decompose(Extrusion(poly, .2)); Assert.Equal(1.4, parts.Sum(p => p.Volume), 7); var empty = DoorMotion.RotateVector(new Vec3(2, 2, 0), angle); Assert.DoesNotContain(parts, p => PolygonGeometry.Inside(p.Polygon, empty));
        }
        [Fact]
        public void FloorHoleIsPreservedAndCellsDoNotOverlap()
        {
            var outer = new[] { new Vec3(0, 0, 0), new Vec3(4, 0, 0), new Vec3(4, 4, 0), new Vec3(0, 4, 0) };
            var inner = new[] { new Vec3(1, 1, 0), new Vec3(3, 1, 0), new Vec3(3, 3, 0), new Vec3(1, 3, 0) }; double h = .2;
            var s = Extrusion(outer, h); s.OriginalVolumeFeet3 = 2.4;
            DiagnosticLoop Loop(IReadOnlyList<Vec3> p) { var l = new DiagnosticLoop(); for (int j = 0; j < p.Count; j++) l.Edges.Add(new DiagnosticEdge { IsLine = true, WorldStartFeet = new DiagnosticPoint(p[j]), WorldEndFeet = new DiagnosticPoint(p[(j + 1) % p.Count]) }); return l; }
            s.Faces[0].Loops.Add(Loop(inner)); s.Faces[1].Loops.Add(Loop(inner.Select(p => p + Vec3.ZAxis * h).ToList()));
            for (int j = 0; j < inner.Length; j++) { var a = inner[j]; var b = inner[(j + 1) % inner.Length]; var f = new DiagnosticFace { IsPlanar = true }; f.Loops.Add(Loop(new[] { a, b, b + Vec3.ZAxis * h, a + Vec3.ZAxis * h })); s.Faces.Add(f); }
            var parts = PlanarSectionDecomposer.Decompose(s); Assert.Equal(2.4, parts.Sum(p => p.Volume), 8); Assert.DoesNotContain(parts, p => PolygonGeometry.Inside(p.Polygon, new Vec3(2, 2, 0)));
            for (int a = 0; a < parts.Count; a++) for (int b = a + 1; b < parts.Count; b++)
            {
                var intersection = parts[a].Polygon.ToList(); var clip = parts[b].Polygon; double sign = Math.Sign(PolygonGeometry.Area(clip));
                for (int j = 0; j < clip.Count; j++) { var d = clip[(j + 1) % clip.Count] - clip[j]; var n = new Vec3(d.Y, -d.X, 0) * sign; intersection = PolygonGeometry.Clip(intersection, n, n.Dot(clip[j])); }
                Assert.True(Math.Abs(PolygonGeometry.Area(intersection)) < 1e-8);
            }
        }
        [Fact]
        public void PlayerCanStandAcrossDecompositionSeamsButNotOverAHole()
        {
            var floors = new[] { new Box(Frame.Identity, new Vec3(-32, -32, -8), new Vec3(0, 32, 0)), new Box(Frame.Identity, new Vec3(0, -32, -8), new Vec3(32, 32, 0)) }.Select(b => BrushBuilder.FromBox(b, "TEST", 1e-5)).ToList();
            Assert.NotNull(EntityBuilder.SearchSpawn(floors, floors, 1e-5).Position);
            var unsupported = new[] { new Box(Frame.Identity, new Vec3(-32, -32, -8), new Vec3(-8, 32, 0)), new Box(Frame.Identity, new Vec3(8, -32, -8), new Vec3(32, 32, 0)) }.Select(b => BrushBuilder.FromBox(b, "TEST", 1e-5)).ToList();
            Assert.Null(EntityBuilder.SearchSpawn(unsupported, unsupported, 1e-5).Position);
        }
        [Fact]
        public void TriangularMaterialFaceDoesNotBecomeGrayFromItsBoundingRectangle()
        {
            var prism = new ConvexPrism(new[] { new Vec3(0, 0, 0), new Vec3(4, 0, 0), new Vec3(0, 4, 0) }, 0, .2); var brush = BrushBuilder.FromPrism(prism, "TEST", 1e-6); var vertices = ConvexGeometryValidator.Validate(brush, 1e-6).Vertices; var material = new BimMaterial { Id = 10, Name = "paint", Color = new RgbColor(180, 30, 40) };
            var surfaces = brush.Faces.Select(face => { var points = vertices.Where(p => Math.Abs(face.Normal.Dot(p) - face.Distance) < 1e-6).ToList(); var center = points.Aggregate(Vec3.Zero, (a, b) => a + b) / points.Count; var ordered = points.OrderBy(p => Math.Atan2((p - center).Dot(face.V), (p - center).Dot(face.U))).ToList(); return new MaterialSurface(face.Normal, face.Distance, new[] { (IReadOnlyList<Vec3>)ordered }, material, true); }).ToList();
            var element = new BimElement(1, BimCategory.Floor, Array.Empty<Box>(), materialSurfaces: surfaces, prisms: new[] { prism }); var result = Exporter.Prepare(new[] { element }, new ExportOptions { TextureSource = TextureSourceMode.RevitMaterials, CenterOrigin = false, GenerateEntities = false }, new ExportReport());
            Assert.Equal(5, result.Report.Materials.Single(m => m.MaterialId == 10).FacesUsed); Assert.DoesNotContain(result.Report.Diagnostics, d => d.Code == "ambiguous_face_material");
        }
        [Fact]
        public void ClosedPrismRejectsSlopeOpenLoopAndIncorrectVolume()
        {
            var square = new[] { Vec3.Zero, Vec3.XAxis, Vec3.XAxis + Vec3.YAxis, Vec3.YAxis }; var s = Extrusion(square, 1); s.OriginalVolumeFeet3 = 2; Assert.Throws<ArgumentException>(() => PlanarSectionDecomposer.Decompose(s)); s.OriginalVolumeFeet3 = 1; s.Faces[0].Loops[0].Edges[0].WorldEndFeet = new DiagnosticPoint(new Vec3(.5, 0, 0)); Assert.Throws<ArgumentException>(() => PlanarSectionDecomposer.Decompose(s));
            s = Extrusion(square, 1); s.Faces[2].IsPlanar = false; Assert.Throws<ArgumentException>(() => PlanarSectionDecomposer.Decompose(s));
            s = Extrusion(square, 1); foreach (var face in s.Faces) foreach (var edge in face.Loops.SelectMany(l => l.Edges)) { var a = edge.WorldStartFeet.ToVector(); var b = edge.WorldEndFeet.ToVector(); edge.WorldStartFeet = new DiagnosticPoint(new Vec3(a.X, a.Y, a.Z + a.X * .1)); edge.WorldEndFeet = new DiagnosticPoint(new Vec3(b.X, b.Y, b.Z + b.X * .1)); }
            Assert.Throws<ArgumentException>(() => PlanarSectionDecomposer.Decompose(s));
        }
    }
}

