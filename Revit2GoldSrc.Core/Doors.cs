using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
namespace Revit2GoldSrc.Core
{
    public enum DoorKind { Rotating, Sliding, Static }
    public enum DoorActivation { Use, Touch, Trigger }
    public enum HingeSide { Auto, Left, Right }
    public enum SlideDirection { Auto, Left, Right, Up, Down }
    public enum DoorActivatorKind { Button, Trigger }
    public enum DoorLeafCount { Auto, Single, Double }
    public sealed class DoorSettings
    {
        public bool Animated { get; set; }
        public DoorKind Kind { get; set; } = DoorKind.Rotating;
        public double OpenAngleDegrees { get; set; } = 90;
        public double Speed { get; set; } = 100;
        public double Wait { get; set; } = 3;
        public DoorActivation Activation { get; set; } = DoorActivation.Use;
        public bool ReverseDirection { get; set; }
        public HingeSide Hinge { get; set; }
        public double HingeOffsetXFeet { get; set; }
        public double HingeOffsetYFeet { get; set; }
        public SlideDirection SlideDirection { get; set; }
        public double SlideDistanceFeet { get; set; }
        public double LeafThicknessFeet { get; set; } = .04 / .3048;
        public DoorLeafCount LeafCount { get; set; }
        public string Group { get; set; } = "";
        public DoorActivatorKind ActivatorKind { get; set; } = DoorActivatorKind.Trigger;
        public bool ManualActivator { get; set; }
        public Vec3 ActivatorOffsetFeet { get; set; } = Vec3.Zero;
        public bool IsDynamic => Animated && Kind != DoorKind.Static;
        public void Validate()
        {
            if (!Enum.IsDefined(typeof(DoorKind), Kind) || !Enum.IsDefined(typeof(DoorActivation), Activation) || !Enum.IsDefined(typeof(HingeSide), Hinge) ||
                !Enum.IsDefined(typeof(SlideDirection), SlideDirection) || !Enum.IsDefined(typeof(DoorActivatorKind), ActivatorKind) || !Enum.IsDefined(typeof(DoorLeafCount), LeafCount)) throw new ArgumentException("Opción de puerta inválida.");
            if (!Vec3.Finite(Speed) || Speed <= 0 || !Vec3.Finite(Wait) || (Wait < 0 && Wait != -1) || !Vec3.Finite(OpenAngleDegrees) || OpenAngleDegrees <= 0 || OpenAngleDegrees > 180 ||
                !Vec3.Finite(SlideDistanceFeet) || SlideDistanceFeet < 0 || !Vec3.Finite(LeafThicknessFeet) || LeafThicknessFeet <= 0 ||
                !Vec3.Finite(HingeOffsetXFeet) || !Vec3.Finite(HingeOffsetYFeet) || !ActivatorOffsetFeet.IsFinite) throw new ArgumentException("Ángulo, velocidad, espera o dimensiones de puerta inválidos.");
        }
    }
    public sealed class DoorLeaf
    {
        public Box GeometryFeet { get; }
        public HingeSide InferredHinge { get; }
        public IReadOnlyList<MaterialSurface> Materials { get; }
        public DoorLeaf(Box geometry, HingeSide inferredHinge = HingeSide.Auto, IReadOnlyList<MaterialSurface>? materials = null)
        { GeometryFeet = geometry; InferredHinge = inferredHinge; Materials = materials ?? Array.Empty<MaterialSurface>(); }
    }
    public sealed class DoorDefinition
    {
        public long Id { get; }
        public ElementOrigin Origin {get;set;}=new ElementOrigin();
        public Frame Frame { get; }
        public DoorSettings Settings { get; }
        public IReadOnlyList<DoorLeaf> Leaves { get; }
        public IReadOnlyList<Box> FramesFeet { get; }
        public IReadOnlyList<Box> UncertainGeometryFeet { get; }
        public DoorDefinition(long id, Frame frame, DoorSettings settings, IReadOnlyList<DoorLeaf> leaves, IReadOnlyList<Box>? frames = null, IReadOnlyList<Box>? uncertain = null)
        { Id = id; Origin.ElementId=id; Frame = frame; Settings = settings; Leaves = leaves; FramesFeet = frames ?? Array.Empty<Box>(); UncertainGeometryFeet = uncertain ?? Array.Empty<Box>(); }
    }
    public sealed class DoorExportOptions
    {
        public bool Enabled { get; set; }
        public string Texture { get; set; } = "";
    }
    [DataContract]
    public sealed class DoorExportInfo
    {
        [DataMember] public long ElementId { get; set; }
        [DataMember] public ElementOrigin? Origin {get;set;}
        [DataMember] public string Status { get; set; } = "";
        [DataMember] public string Kind { get; set; } = "";
        [DataMember] public string Activation { get; set; } = "";
        [DataMember] public string Group { get; set; } = "";
        [DataMember] public List<string> HingesHU { get; set; } = new List<string>();
        [DataMember] public List<string> Movements { get; set; } = new List<string>();
        [DataMember] public int Leaves { get; set; }
    }
    public static class DoorMotion
    {
        public static bool Intersects(Box a, Box b, double tolerance)
        {
            var axes = new[] { a.Frame.U, a.Frame.V, a.Frame.W, b.Frame.U, b.Frame.V, b.Frame.W }.ToList();
            foreach (var u in new[] { a.Frame.U, a.Frame.V, a.Frame.W }) foreach (var v in new[] { b.Frame.U, b.Frame.V, b.Frame.W }) if (u.Cross(v).Length > 1e-9) axes.Add(u.Cross(v).Unit());
            var ac = a.Frame.ToWorld((a.Min + a.Max) / 2); var bc = b.Frame.ToWorld((b.Min + b.Max) / 2); var ah = (a.Max - a.Min) / 2; var bh = (b.Max - b.Min) / 2;
            foreach (var axis in axes)
            {
                double ar = Math.Abs(axis.Dot(a.Frame.U)) * ah.X + Math.Abs(axis.Dot(a.Frame.V)) * ah.Y + Math.Abs(axis.Dot(a.Frame.W)) * ah.Z;
                double br = Math.Abs(axis.Dot(b.Frame.U)) * bh.X + Math.Abs(axis.Dot(b.Frame.V)) * bh.Y + Math.Abs(axis.Dot(b.Frame.W)) * bh.Z;
                if (Math.Abs(axis.Dot(ac - bc)) >= ar + br - tolerance) return false;
            }
            return true;
        }
        public static Vec3 RotateVector(Vec3 v, double degrees)
        { double a = degrees * Math.PI / 180; return new Vec3(v.X * Math.Cos(a) - v.Y * Math.Sin(a), v.X * Math.Sin(a) + v.Y * Math.Cos(a), v.Z); }
        public static Box Rotate(Box b, Vec3 pivot, double angle) => new Box(new Frame(pivot + RotateVector(b.Frame.Origin - pivot, angle), RotateVector(b.Frame.U, angle), RotateVector(b.Frame.V, angle), b.Frame.W), b.Min, b.Max);
        public static Box Translate(Box b, Vec3 delta) => new Box(new Frame(b.Frame.Origin + delta, b.Frame.U, b.Frame.V, b.Frame.W), b.Min, b.Max);
        public static Box SlidingSweep(Box box, Vec3 delta)
        {
            var local = new Vec3(delta.Dot(box.Frame.U), delta.Dot(box.Frame.V), delta.Dot(box.Frame.W));
            if (new[] { local.X, local.Y, local.Z }.Count(v => Math.Abs(v) > 1e-8) != 1) throw new ArgumentException("El recorrido corredizo debe seguir un eje de la hoja.");
            return new Box(box.Frame, new Vec3(box.Min.X + Math.Min(0, local.X), box.Min.Y + Math.Min(0, local.Y), box.Min.Z + Math.Min(0, local.Z)),
                new Vec3(box.Max.X + Math.Max(0, local.X), box.Max.Y + Math.Max(0, local.Y), box.Max.Z + Math.Max(0, local.Z)));
        }
        public static string? CheckRotation(Box box, Vec3 pivot, double angle, IReadOnlyList<Box> obstacles, double tolerance)
        {
            int nodes = 0; double radius = box.Vertices.Max(p => new Vec3(p.X - pivot.X, p.Y - pivot.Y, 0).Length);
            string? Check(double start, double end, int depth)
            {
                if (++nodes > 50000) return "No se pudo verificar el barrido dentro del presupuesto de análisis.";
                double mid = (start + end) / 2;
                var middle = Rotate(box, pivot, mid);
                if (obstacles.Any(o => Intersects(Rotate(box, pivot, start), o, tolerance) || Intersects(middle, o, tolerance) || Intersects(Rotate(box, pivot, end), o, tolerance))) return "La hoja interseca geometría durante el giro.";
                double half = Math.Min(180, Math.Abs(end - start) / 2); double margin = 2 * radius * Math.Sin(half * Math.PI / 360);
                var bound = new Box(middle.Frame, middle.Min - new Vec3(margin, margin, 0), middle.Max + new Vec3(margin, margin, 0));
                if (obstacles.All(o => !Intersects(bound, o, tolerance))) return null;
                if (depth >= 28) return "Barrido de giro incierto cerca de una superficie.";
                return Check(start, mid, depth + 1) ?? Check(mid, end, depth + 1);
            }
            return Check(0, angle, 0);
        }
        public static Box Bounds(IEnumerable<Vec3> vertices)
        {
            var p = vertices.ToList(); return new Box(Frame.Identity, new Vec3(p.Min(v => v.X), p.Min(v => v.Y), p.Min(v => v.Z)), new Vec3(p.Max(v => v.X), p.Max(v => v.Y), p.Max(v => v.Z)));
        }
    }
}
