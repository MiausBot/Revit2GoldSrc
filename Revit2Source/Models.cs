using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Revit2GoldSrc.Core;

namespace Revit2Source
{
    public sealed class SourceSide
    {
        public int Id { get; set; }
        public Vec3 A { get; set; }
        public Vec3 B { get; set; }
        public Vec3 C { get; set; }
        public Vec3 U { get; set; }
        public Vec3 V { get; set; }
        public double UShift { get; set; }
        public double VShift { get; set; }
        public double UScale { get; set; } = .25;
        public double VScale { get; set; } = .25;
        public string Material { get; set; } = "tools/toolsnodraw";
        public int LightmapScale { get; set; } = 16;
        internal string? SourceMaterialKey { get; set; }
        internal bool Painted { get; set; }
        public Vec3 Normal => (A - B).Cross(C - B).Unit();
        public double Distance => Normal.Dot(A);
    }

    public sealed class SourceSolid
    {
        public int Id { get; set; }
        public List<SourceSide> Sides { get; } = new List<SourceSide>();
    }

    public sealed class SourceConnection
    {
        public string Output { get; set; } = "";
        public string Target { get; set; } = "";
        public string Input { get; set; } = "";
        public string Parameter { get; set; } = "";
        public double Delay { get; set; }
        public int Times { get; set; } = -1;
    }

    public sealed class SourceEntity
    {
        public int Id { get; set; }
        public Dictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<SourceSolid> Solids { get; } = new List<SourceSolid>();
        public List<SourceConnection> Connections { get; } = new List<SourceConnection>();

        public SourceEntity(string classname)
        {
            Properties["classname"] = classname;
        }
    }

    public sealed class SourceDocument
    {
        public SourceEntity World { get; } = new SourceEntity("worldspawn");
        public List<SourceEntity> Entities { get; } = new List<SourceEntity>();
    }

    [DataContract]
    public sealed class SourcePlacement
    {
        [DataMember]
        public string Kind { get; set; } = "Light";

        [DataMember]
        public double X { get; set; }

        [DataMember]
        public double Y { get; set; }

        [DataMember]
        public double Z { get; set; }

        [DataMember]
        public double Yaw { get; set; }

        [DataMember]
        public int SurvivorOrder { get; set; } = 1;

        [DataMember]
        public string Model { get; set; } = "models/props_doors/checkpoint_door_01.mdl";

        [DataMember]
        public string LightColor { get; set; } = "255 240 220 300";

        [DataMember]
        public double OpeningWidthFeet { get; set; }

        [DataMember]
        public double OpeningHeightFeet { get; set; }
        public Vec3 Position => new Vec3(X, Y, Z);
    }

    [DataContract]
    public sealed class SourceDoorConfig
    {
        [DataMember]
        public string Document { get; set; } = "";

        [DataMember]
        public long ElementId { get; set; }

        [DataMember]
        public string Kind { get; set; } = "Rotating";

        [DataMember]
        public string Hinge { get; set; } = "Left";

        [DataMember]
        public double Angle { get; set; } = 90;

        [DataMember]
        public double Speed { get; set; } = 100;

        [DataMember]
        public double Wait { get; set; } = 3;

        [DataMember]
        public bool Reverse { get; set; }

        [DataMember]
        public string SlideDirection { get; set; } = "Right";

        [DataMember]
        public double SlideDistanceFeet { get; set; }
    }

    [DataContract]
    public sealed class SourceConfiguration
    {
        [DataMember]
        public int SchemaVersion { get; set; } = 1;

        [DataMember]
        public string Document { get; set; } = "";

        [DataMember]
        public List<SourcePlacement> Placements { get; set; } = new List<SourcePlacement>();

        [DataMember]
        public List<SourceDoorConfig> Doors { get; set; } = new List<SourceDoorConfig>();
    }

    public sealed class SourceOptions
    {
        public double UnitsPerFoot { get; set; } = 12;
        public double ToleranceFeet { get; set; } = .00001;
        public bool CenterOrigin { get; set; } = true;
        public Vec3 OriginFeet { get; set; } = Vec3.Zero;
        public string MapName { get; set; } = "revit_room";
        public bool AutoSurvivors { get; set; } = true;
        public bool AutoLight { get; set; } = true;
        public SourceConfiguration Configuration { get; set; } = new SourceConfiguration();
        public IReadOnlyList<Box> UncertainObstaclesFeet { get; set; } = Array.Empty<Box>();
        public FgdCatalog? Fgd { get; set; }
        public Func<string, ModelBounds?>? ResolveModel { get; set; }
    }

    public sealed class ModelBounds
    {
        public Vec3 Min { get; }
        public Vec3 Max { get; }

        public ModelBounds(Vec3 min, Vec3 max)
        {
            Min = min;
            Max = max;
        }
    }

    [DataContract]
    public sealed class SourceDoorInfo
    {
        [DataMember]
        public long? ElementId { get; set; }

        [DataMember]
        public string Document { get; set; } = "";

        [DataMember]
        public string Kind { get; set; } = "";

        [DataMember]
        public string Status { get; set; } = "";

        [DataMember]
        public string Reason { get; set; } = "";

        [DataMember]
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
    }

    [DataContract]
    public sealed class SourceReport
    {
        [DataMember]
        public string Target { get; set; } = "L4D2";

        [DataMember]
        public string RevitVersion { get; set; } = "";

        [DataMember]
        public int ElementsFound { get; set; }

        [DataMember]
        public int ElementsConverted { get; set; }

        [DataMember]
        public int ElementsSkipped { get; set; }

        [DataMember]
        public int Solids { get; set; }

        [DataMember]
        public int Sides { get; set; }

        [DataMember]
        public int Entities { get; set; }

        [DataMember]
        public string OriginFeet { get; set; } = "";

        [DataMember]
        public double UnitsPerFoot { get; set; }

        [DataMember]
        public double Seconds { get; set; }

        [DataMember]
        public bool Published { get; set; }

        [DataMember]
        public bool FgdValidated { get; set; }

        [DataMember]
        public string NavStatus { get; set; } = "Pending manual generation and in-game validation";

        [DataMember]
        public List<Diagnostic> Diagnostics { get; set; } = new List<Diagnostic>();

        [DataMember]
        public List<ElementExportInfo> Elements { get; set; } = new List<ElementExportInfo>();

        [DataMember]
        public List<SourceDoorInfo> Doors { get; set; } = new List<SourceDoorInfo>();

        [DataMember]
        public List<MaterialExportInfo> Materials { get; set; } = new List<MaterialExportInfo>();
    }

    public sealed class SourceExportResult
    {
        public SourceDocument Map { get; }
        public SourceReport Report { get; }
        public Dictionary<string, byte[]> Assets { get; }
        public SourceConfiguration Configuration { get; }

        public SourceExportResult(SourceDocument map, SourceReport report, Dictionary<string, byte[]> assets, SourceConfiguration configuration)
        {
            Map = map;
            Report = report;
            Assets = assets;
            Configuration = configuration;
        }
    }
}
