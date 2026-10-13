using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
namespace Revit2GoldSrc.Core
{
    [DataContract]
    public sealed class DiagnosticPoint
    {
        [DataMember] public double X { get; set; }
        [DataMember] public double Y { get; set; }
        [DataMember] public double Z { get; set; }
        public DiagnosticPoint() { }
        public DiagnosticPoint(Vec3 point) { X = point.X; Y = point.Y; Z = point.Z; }
        public Vec3 ToVector() => new Vec3(X, Y, Z);
    }
    [DataContract]
    public sealed class DiagnosticFrame
    {
        [DataMember] public DiagnosticPoint Origin { get; set; } = new DiagnosticPoint();
        [DataMember] public DiagnosticPoint U { get; set; } = new DiagnosticPoint(Vec3.XAxis);
        [DataMember] public DiagnosticPoint V { get; set; } = new DiagnosticPoint(Vec3.YAxis);
        [DataMember] public DiagnosticPoint W { get; set; } = new DiagnosticPoint(Vec3.ZAxis);
        public DiagnosticFrame() { }
        public DiagnosticFrame(Frame frame)
        { Origin = new DiagnosticPoint(frame.Origin); U = new DiagnosticPoint(frame.U); V = new DiagnosticPoint(frame.V); W = new DiagnosticPoint(frame.W); }
        public Frame ToFrame() => new Frame(Origin.ToVector(), U.ToVector(), V.ToVector(), W.ToVector());
    }
    [DataContract]
    public sealed class BoundaryIssue
    {
        [DataMember] public string Code { get; set; } = "";
        [DataMember] public string Message { get; set; } = "";
        [DataMember(EmitDefaultValue = false)] public int? FaceIndex { get; set; }
        [DataMember(EmitDefaultValue = false)] public int? LoopIndex { get; set; }
        [DataMember(EmitDefaultValue = false)] public int? EdgeIndex { get; set; }
        [DataMember(EmitDefaultValue = false)] public DiagnosticPoint? LocalStartFeet { get; set; }
        [DataMember(EmitDefaultValue = false)] public DiagnosticPoint? LocalEndFeet { get; set; }
        [DataMember(EmitDefaultValue = false)] public DiagnosticPoint? WorldStartFeet { get; set; }
        [DataMember(EmitDefaultValue = false)] public DiagnosticPoint? WorldEndFeet { get; set; }
        public override string ToString() => Message + (FaceIndex.HasValue ? " (cara " + FaceIndex + ", contorno " + LoopIndex + ", arista " + EdgeIndex + ")" : "");
    }
    public sealed class GeometryValidationException : ArgumentException
    {
        public IReadOnlyList<BoundaryIssue> Issues { get; }
        public GeometryValidationException(IReadOnlyList<BoundaryIssue> issues) : base(issues[0].ToString()) { Issues = issues; }
    }
    [DataContract]
    public sealed class DiagnosticEdge
    {
        [DataMember] public string CurveType { get; set; } = "Line";
        [DataMember] public bool IsLine { get; set; } = true;
        [DataMember] public DiagnosticPoint WorldStartFeet { get; set; } = new DiagnosticPoint();
        [DataMember] public DiagnosticPoint WorldEndFeet { get; set; } = new DiagnosticPoint();
        [DataMember(EmitDefaultValue = false)] public DiagnosticPoint? LocalStartFeet { get; set; }
        [DataMember(EmitDefaultValue = false)] public DiagnosticPoint? LocalEndFeet { get; set; }
        // Samples are diagnostic only: curved edges never become brushes via tessellation.
        [DataMember(EmitDefaultValue = false)] public List<DiagnosticPoint>? WorldSamplesFeet { get; set; }
    }
    [DataContract]
    public sealed class DiagnosticLoop
    {
        [DataMember] public List<DiagnosticEdge> Edges { get; set; } = new List<DiagnosticEdge>();
    }
    [DataContract]
    public sealed class DiagnosticFace
    {
        [DataMember] public bool IsPlanar { get; set; }
        [DataMember] public string SurfaceType { get; set; } = "";
        [DataMember] public List<DiagnosticLoop> Loops { get; set; } = new List<DiagnosticLoop>();
    }
    [DataContract]
    public sealed class SolidGeometryDiagnostic
    {
        [DataMember] public int SolidIndex { get; set; }
        [DataMember] public double OriginalVolumeFeet3 { get; set; }
        [DataMember] public double ToleranceFeet { get; set; }
        [DataMember(EmitDefaultValue = false)] public DiagnosticFrame? Frame { get; set; }
        [DataMember] public List<DiagnosticFace> Faces { get; set; } = new List<DiagnosticFace>();
        [DataMember] public List<BoundaryIssue> Issues { get; set; } = new List<BoundaryIssue>();
        public IReadOnlyList<OrthogonalFace> ToOrthogonalFaces()
        {
            if (Frame == null) throw new ArgumentException("Sólido sin marco local.");
            if (Faces.Any(f => !f.IsPlanar || f.Loops.Any(l => l.Edges.Any(e => !e.IsLine)))) throw new ArgumentException("El contorno contiene superficies o aristas curvas.");
            return Faces.Select(f => new OrthogonalFace(f.Loops.Select(l => (IReadOnlyList<Vec3>)l.Edges.Select(e =>
                e.LocalStartFeet?.ToVector() ?? throw new ArgumentException("Arista sin coordenadas locales.")).ToList()).ToList())).ToList();
        }
        public IReadOnlyList<Box> Replay() => GeometryDecomposer.Decompose(Frame?.ToFrame() ?? throw new ArgumentException("Sólido sin marco local."), ToOrthogonalFaces(), OriginalVolumeFeet3, ToleranceFeet);
    }
    public static class GeometryBoundaryValidator
    {
        public static IReadOnlyList<BoundaryIssue> Inspect(Frame frame, IReadOnlyList<OrthogonalFace> faces, double tolerance)
        {
            if (!Vec3.Finite(tolerance) || tolerance <= 0) throw new ArgumentException("Tolerancia inválida.");
            var issues = new List<BoundaryIssue>();
            if (faces.Count < 6) issues.Add(new BoundaryIssue { Code = "invalid_contour", Message = "Sólido sin suficientes caras para el descomponedor ortogonal." });
            for (int f = 0; f < faces.Count; f++)
            {
                var face = faces[f]; var points = face.Loops.SelectMany(l => l).ToList();
                if (face.Loops.Count == 0 || points.Count == 0 || points.Any(p => !p.IsFinite))
                { issues.Add(new BoundaryIssue { Code = "invalid_contour", Message = "Contorno vacío o con coordenadas no finitas.", FaceIndex = f }); continue; }
                int constantAxes = Enumerable.Range(0, 3).Count(a => points.Max(p => p[a]) - points.Min(p => p[a]) <= tolerance);
                if (constantAxes != 1) issues.Add(new BoundaryIssue { Code = "non_orthogonal_face", Message = "Cara inclinada o no ortogonal.", FaceIndex = f });
                for (int l = 0; l < face.Loops.Count; l++)
                {
                    var loop = face.Loops[l];
                    if (loop.Count < 4) issues.Add(new BoundaryIssue { Code = "invalid_contour", Message = "Contorno con menos de cuatro vértices.", FaceIndex = f, LoopIndex = l });
                    for (int e = 0; e < loop.Count; e++)
                    {
                        var start = loop[e]; var end = loop[(e + 1) % loop.Count]; var delta = end - start;
                        int changing = Enumerable.Range(0, 3).Count(a => Math.Abs(delta[a]) > tolerance);
                        string? code = changing == 0 ? "short_edge" : changing > 1 ? "diagonal_edge" : null;
                        if (code == null) continue;
                        issues.Add(new BoundaryIssue
                        {
                            Code = code,
                            Message = code == "short_edge" ? "Segmento demasiado corto para la tolerancia configurada." : "Borde diagonal respecto al marco local.",
                            FaceIndex = f,
                            LoopIndex = l,
                            EdgeIndex = e,
                            LocalStartFeet = new DiagnosticPoint(start),
                            LocalEndFeet = new DiagnosticPoint(end),
                            WorldStartFeet = new DiagnosticPoint(frame.ToWorld(start)),
                            WorldEndFeet = new DiagnosticPoint(frame.ToWorld(end))
                        });
                    }
                }
            }
            return issues;
        }
    }
}
