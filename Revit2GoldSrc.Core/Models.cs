using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
namespace Revit2GoldSrc.Core
{
    public enum BimCategory { Wall, Floor, Column }
    public sealed class BimElement
    {
        public long Id { get; }
        public BimCategory Category { get; }
        public IReadOnlyList<Box> Solids { get; }
        public IReadOnlyList<ConvexPrism> Prisms { get; }
        public IReadOnlyList<ConvexSolid> ConvexSolids { get; }
        public ElementOrigin Origin {get;set;}=new ElementOrigin();
        public string CategoryKey {get;set;}="";
        public string CategoryName {get;set;}="";
        public string FamilyName {get;set;}="";public string TypeName {get;set;}="";
        public bool Collision {get;set;}=true;
        public GeometryQuality? Quality {get;set;}
        public double VolumeError {get;set;} public double SurfaceDeviationFeet {get;set;}
        public IEnumerable<Vec3> Vertices => Solids.SelectMany(b=>b.Vertices).Concat(Prisms.SelectMany(p=>p.Vertices)).Concat(ConvexSolids.SelectMany(s=>s.Vertices));
        public IReadOnlyList<SolidGeometryDiagnostic>? SourceGeometry { get; }
        public IReadOnlyList<MaterialSurface> MaterialSurfaces { get; }
        public Frame? MaterialFrame { get; }
        public BimElement(long id, BimCategory category, IReadOnlyList<Box> solids, IReadOnlyList<SolidGeometryDiagnostic>? sourceGeometry = null, IReadOnlyList<MaterialSurface>? materialSurfaces = null, Frame? materialFrame = null, IReadOnlyList<ConvexPrism>? prisms = null, IReadOnlyList<ConvexSolid>? convexSolids = null)
        { Id = id; Origin.ElementId=id; ConvexSolids=convexSolids??Array.Empty<ConvexSolid>(); Category = category; Solids = solids; Prisms=prisms??Array.Empty<ConvexPrism>(); SourceGeometry = sourceGeometry; MaterialSurfaces = materialSurfaces ?? Array.Empty<MaterialSurface>(); MaterialFrame = materialFrame; }
    }
    public sealed class ExportOptions
    {
        public DoorExportOptions Doors { get; } = new DoorExportOptions();
        public TextureSourceMode TextureSource { get; set; } = TextureSourceMode.ExistingWad;
        public double UnitsPerFoot { get; set; } = 12;
        public double ToleranceFeet { get; set; } = 0.00001;
        public bool CenterOrigin { get; set; } = true;
        public Vec3 OriginFeet { get; set; } = Vec3.Zero;
        public string WadPath { get; set; } = "";
        public Dictionary<string,string> CategoryTextures {get;}=new Dictionary<string,string>();
        public Dictionary<BimCategory, string> Textures { get; } = new Dictionary<BimCategory, string>();
        public bool GenerateEntities { get; set; } = true;
        public bool TestEnclosure { get; set; }
        public string EnclosureTexture { get; set; } = "";
        public void Validate()
        {
            if (!Enum.IsDefined(typeof(TextureSourceMode), TextureSource)) throw new ArgumentException("Modo de texturas inválido.");
            if (!Vec3.Finite(UnitsPerFoot) || UnitsPerFoot <= 0 || !Vec3.Finite(ToleranceFeet) || ToleranceFeet <= 0 || !OriginFeet.IsFinite)
                throw new ArgumentException("Escala, origen o tolerancia inválidos.");
        }
    }
    [DataContract]
    public sealed class Diagnostic
    {
        [DataMember] public long? ElementId { get; set; }
        [DataMember] public string Severity { get; set; } = "warning";
        [DataMember] public string Message { get; set; } = "";
        [DataMember(EmitDefaultValue = false)] public string? Code { get; set; }
        [DataMember(EmitDefaultValue = false)] public string? Category { get; set; }
        [DataMember(EmitDefaultValue = false)] public List<SolidGeometryDiagnostic>? Geometry { get; set; }
        public Diagnostic() { }
        public Diagnostic(long? id, string message, string severity = "warning") { ElementId = id; Message = message; Severity = severity; }
    }
    [DataContract]
    public sealed class ExportReport
    {
        [DataMember] public int DiagnosticSchemaVersion { get; set; } = 2;
        [DataMember] public string RevitVersion { get; set; } = "";
        [DataMember] public int ElementsFound { get; set; }
        [DataMember] public int ElementsConverted { get; set; }
        [DataMember] public int ElementsSkipped { get; set; }
        [DataMember] public int Brushes { get; set; }
        [DataMember] public int Faces { get; set; }
        [DataMember] public int Entities { get; set; }
        [DataMember] public double ElapsedSeconds { get; set; }
        [DataMember] public double UnitsPerFoot { get; set; }
        [DataMember] public double ToleranceFeet { get; set; }
        [DataMember] public string OriginFeet { get; set; } = "";
        [DataMember] public string MapPath { get; set; } = "";
        [DataMember] public bool MapPublished { get; set; }
        [DataMember] public string TextureSource { get; set; } = "ExistingWad";
        [DataMember(EmitDefaultValue = false)] public string? GeneratedWadPath { get; set; }
        [DataMember] public List<MaterialExportInfo> Materials { get; set; } = new List<MaterialExportInfo>();
        [DataMember] public List<DoorExportInfo> Doors { get; set; } = new List<DoorExportInfo>();
        [DataMember(EmitDefaultValue = false)] public string? ToolsWadPath { get; set; }
        [DataMember] public List<ElementExportInfo> Elements {get;set;}=new List<ElementExportInfo>();
        [DataMember] public List<Diagnostic> Diagnostics { get; set; } = new List<Diagnostic>();
    }
}

