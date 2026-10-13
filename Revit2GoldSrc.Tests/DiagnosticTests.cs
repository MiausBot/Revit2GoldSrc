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
    public sealed class DiagnosticTests
    {
        private const double T = 1e-5;
        private static IReadOnlyList<OrthogonalFace> Boundary() => GeometryTests.Boundary(new[] { new Box(Frame.Identity, Vec3.Zero, new Vec3(4, .15, 3)) });
        [Fact]
        public void DistinguishesDiagonalShortAndInvalidContoursWithIndicesAndWorldCoordinates()
        {
            var faces = Boundary().ToList();
            var diagonal = faces[0].Loops[0].ToList(); diagonal.Insert(1, (diagonal[0] + diagonal[2]) / 2);
            faces[0] = new OrthogonalFace(new[] { (IReadOnlyList<Vec3>)diagonal });
            var shortLoop = faces[1].Loops[0].ToList(); shortLoop.Insert(1, shortLoop[0] + new Vec3(0, T / 4, 0));
            faces[1] = new OrthogonalFace(new[] { (IReadOnlyList<Vec3>)shortLoop });
            faces[2] = new OrthogonalFace(new[] { (IReadOnlyList<Vec3>)faces[2].Loops[0].Take(3).ToList() });
            var frame = new Frame(new Vec3(100, 200, 30), Vec3.YAxis, -Vec3.XAxis, Vec3.ZAxis);
            var issues = GeometryBoundaryValidator.Inspect(frame, faces, T);
            Assert.Contains(issues, i => i.Code == "diagonal_edge" && i.FaceIndex == 0 && i.LoopIndex == 0 && i.EdgeIndex.HasValue);
            var shortEdge = Assert.Single(issues, i => i.Code == "short_edge");
            Assert.Equal(1, shortEdge.FaceIndex); Assert.Equal(0, shortEdge.EdgeIndex);
            Assert.Contains(issues, i => i.Code == "invalid_contour" && i.FaceIndex == 2);
            var issue = issues.First(i => i.Code == "diagonal_edge");
            Assert.True((frame.ToWorld(issue.LocalStartFeet!.ToVector()) - issue.WorldStartFeet!.ToVector()).Length < T);
            var error = Assert.Throws<GeometryValidationException>(() => GeometryDecomposer.Decompose(frame, faces, 1.8, T));
            Assert.Equal(issues.Count, error.Issues.Count);
        }
        private static SolidGeometryDiagnostic Snapshot(IReadOnlyList<OrthogonalFace> faces, int index)
        {
            var frame = new Frame(new Vec3(10, 20, 30), Vec3.XAxis, Vec3.YAxis, Vec3.ZAxis);
            var result = new SolidGeometryDiagnostic { SolidIndex = index, OriginalVolumeFeet3 = 1.8, ToleranceFeet = T, Frame = new DiagnosticFrame(frame) };
            foreach (var face in faces)
            {
                var f = new DiagnosticFace { IsPlanar = true, SurfaceType = "PlanarFace" }; result.Faces.Add(f);
                foreach (var loop in face.Loops)
                {
                    var l = new DiagnosticLoop(); f.Loops.Add(l);
                    for (int i = 0; i < loop.Count; i++)
                    {
                        var a = loop[i]; var b = loop[(i + 1) % loop.Count];
                        l.Edges.Add(new DiagnosticEdge { LocalStartFeet = new DiagnosticPoint(a), LocalEndFeet = new DiagnosticPoint(b), WorldStartFeet = new DiagnosticPoint(frame.ToWorld(a)), WorldEndFeet = new DiagnosticPoint(frame.ToWorld(b)) });
                    }
                }
            }
            return result;
        }
        [Fact]
        public void JsonPreservesAllOmittedElementsSolidsAndCanReplayWithoutRevit()
        {
            var faces = Boundary().ToList(); var loop = faces[0].Loops[0].ToList(); loop.Insert(1, (loop[0] + loop[2]) / 2); faces[0] = new OrthogonalFace(new[] { (IReadOnlyList<Vec3>)loop });
            var rejected = Snapshot(faces, 1); rejected.Issues.AddRange(GeometryBoundaryValidator.Inspect(rejected.Frame!.ToFrame(), faces, T));
            var report = new ExportReport { ElementsFound = 2, ElementsSkipped = 2 };
            report.Diagnostics.Add(new Diagnostic(5000000000, "Rejected") { Category = "Wall", Code = "geometry_rejected", Geometry = new List<SolidGeometryDiagnostic> { Snapshot(Boundary(), 0), rejected } });
            report.Diagnostics.Add(new Diagnostic(5000000001, "Rejected again") { Category = "Floor", Code = "geometry_rejected", Geometry = new List<SolidGeometryDiagnostic> { rejected } });
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(ExportReport)); serializer.WriteObject(stream, report); stream.Position = 0;
                var copy = (ExportReport)serializer.ReadObject(stream)!; Assert.Equal(2, copy.DiagnosticSchemaVersion); Assert.Equal(2, copy.Diagnostics.Count);
                var solids = copy.Diagnostics[0].Geometry!; Assert.Equal(2, solids.Count); Assert.Equal(5000000000, copy.Diagnostics[0].ElementId);
                Assert.Equal(1.8, Assert.Single(solids[0].Replay()).Volume, 8);
                var failure = Assert.Throws<GeometryValidationException>(() => solids[1].Replay());
                Assert.Equal(rejected.Issues.Count, failure.Issues.Count); Assert.Equal(rejected.Issues[0].EdgeIndex, failure.Issues[0].EdgeIndex);
                Assert.Equal(6, solids[1].Faces.Count); Assert.Equal(1, solids[1].SolidIndex);
            }
        }
        [Fact]
        public void CurvedBoundarySamplesAreDiagnosticOnlyAndNeverReplayedAsBrushes()
        {
            var snapshot = Snapshot(Boundary(), 0); var edge = snapshot.Faces[0].Loops[0].Edges[0]; edge.IsLine = false; edge.CurveType = "Arc";
            edge.WorldSamplesFeet = new List<DiagnosticPoint> { edge.WorldStartFeet, new DiagnosticPoint(new Vec3(11, 22, 30)), edge.WorldEndFeet };
            Assert.Throws<ArgumentException>(() => snapshot.Replay());
            snapshot.Frame = null; Assert.Throws<ArgumentException>(() => snapshot.Replay());
        }
        [Fact]
        public void SpawnFailureReasonsAreDistinct()
        {
            GoldSrcBrush B(double width, double z, double height) => BrushBuilder.FromBox(new Box(Frame.Identity, new Vec3(0, 0, z), new Vec3(width, width, z + height)), "TEST", T);
            Assert.Equal("spawn_no_compatible_floor", EntityBuilder.SearchSpawn(Array.Empty<GoldSrcBrush>(), Array.Empty<GoldSrcBrush>(), T).Code);
            var tiny = B(20, 0, 16); Assert.Equal("spawn_insufficient_space", EntityBuilder.SearchSpawn(new[] { tiny }, new[] { tiny }, T).Code);
            var floor = B(200, 0, 16); var roof = B(200, 70, 16);
            var limited = EntityBuilder.SearchSpawn(new[] { floor }, new[] { floor, roof }, T, 1); Assert.Equal("spawn_candidate_limit", limited.Code); Assert.Equal(1, limited.CandidatesChecked);
            var success = EntityBuilder.SearchSpawn(new[] { floor }, new[] { floor }, T); Assert.True(success.Position.HasValue); Assert.Equal("spawn_found", success.Code);
        }
        [Fact]
        public void DiagnosticOnlyPublicationPreservesPreviousMapAndAllWarnings()
        {
            string dir = Path.Combine(Path.GetTempPath(), "R2GS-diagnostic-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "edificio_diagnostico.map"); File.WriteAllText(path, "previous map");
                var report = new ExportReport { ElementsFound = 11, ElementsSkipped = 11 };
                for (int i = 0; i < 11; i++) report.Diagnostics.Add(new Diagnostic(i, "Omitido") { Geometry = new List<SolidGeometryDiagnostic> { Snapshot(Boundary(), 0) } });
                Exporter.PublishDiagnostics(report, path); Assert.Equal("previous map", File.ReadAllText(path));
                using (var stream = File.OpenRead(Path.Combine(dir, "edificio_diagnostico_export_report.json")))
                {
                    var saved = (ExportReport)new DataContractJsonSerializer(typeof(ExportReport)).ReadObject(stream)!;
                    Assert.False(saved.MapPublished); Assert.Equal(11, saved.Diagnostics.Count); Assert.Equal(11, saved.ElementsSkipped);
                }
                Assert.Empty(Directory.GetFiles(dir, "*.tmp")); Assert.Empty(Directory.GetFiles(dir, "*.bak"));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
