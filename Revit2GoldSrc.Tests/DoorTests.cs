using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Globalization;
using Revit2GoldSrc.Core;
using Revit2GoldSrc.GoldSrc;
using Revit2GoldSrc.Compilation;
using Xunit;
namespace Revit2GoldSrc.Tests
{
    public sealed class DoorTests
    {
        private const double T = .0001;
        private static Box B(double x, double y, double z, double dx, double dy, double dz) => new Box(Frame.Identity, new Vec3(x, y, z), new Vec3(x + dx, y + dy, z + dz));
        private static DoorDefinition Door(DoorSettings? settings = null, Frame? frame = null, IReadOnlyList<DoorLeaf>? leaves = null, long id = 1)
        { frame = frame ?? Frame.Identity; return new DoorDefinition(id, frame, settings ?? new DoorSettings { Animated = true, Hinge = HingeSide.Left }, leaves ?? new[] { new DoorLeaf(new Box(frame, new Vec3(-18, -1, 0), new Vec3(18, 1, 80))) }); }
        private static MapDocument Run(IReadOnlyList<DoorDefinition> doors, out ExportReport report, IReadOnlyList<Box>? obstacles = null, IReadOnlyList<Box>? unknown = null, bool enabled = true)
        {
            var map = new MapDocument(); map.Entities.Add(new MapEntity("worldspawn")); report = new ExportReport(); var options = new ExportOptions { UnitsPerFoot = 1, ToleranceFeet = T }; options.Doors.Enabled = enabled;
            DoorEntityBuilder.Add(map, doors, obstacles ?? Array.Empty<Box>(), unknown ?? Array.Empty<Box>(), options, Vec3.Zero, report, (d, l) => BrushBuilder.FromBox(l.GeometryFeet, "DOOR", T)); return map;
        }
        [Fact] public void DisabledDoesNotChangeMap() { var map = Run(new[] { Door() }, out var report, enabled: false); Assert.Single(map.Entities); Assert.Empty(map.Entities[0].Brushes); Assert.Empty(report.Doors); }
        [Theory]
        [InlineData(false, DoorKind.Rotating)]
        [InlineData(true, DoorKind.Static)]
        public void StaticIsWorldGeometry(bool animated, DoorKind kind) { var map = Run(new[] { Door(new DoorSettings { Animated = animated, Kind = kind }) }, out var report); Assert.Single(map.Entities); Assert.Single(map.Entities[0].Brushes); Assert.Equal("Static", report.Doors[0].Status); }
        [Theory]
        [InlineData(0, false)]
        [InlineData(45, false)]
        [InlineData(90, false)]
        [InlineData(45, true)]
        [InlineData(90, true)]
        public void HingeTracksRotatedAndReflectedFrames(double angle, bool reflected)
        {
            var u = DoorMotion.RotateVector(Vec3.XAxis, angle) * (reflected ? -1 : 1); var v = DoorMotion.RotateVector(Vec3.YAxis, angle); var frame = new Frame(new Vec3(100, 20, 0), u, v, Vec3.ZAxis);
            var map = Run(new[] { Door(frame: frame) }, out var report); Assert.Equal("Animated", report.Doors[0].Status); var e = map.Entities[1]; Assert.Equal("func_door_rotating", e.Properties["classname"]); Assert.Equal("0 0 0", e.Properties["angles"]);
            var origin = e.Brushes.Single(b => b.Role == BrushRole.Origin); var points = ConvexGeometryValidator.Validate(origin, T).Vertices; var pivot = points.Aggregate(Vec3.Zero, (a, b) => a + b) / points.Count;
            Assert.True((pivot - frame.ToWorld(new Vec3(-18, 1, 40))).Length < 1e-7); Assert.Empty(map.Entities[0].Brushes); Assert.True((int.Parse(e.Properties["spawnflags"]) & 16) != 0);
        }
        [Theory]
        [InlineData(DoorActivation.Use, 256)]
        [InlineData(DoorActivation.Touch, 0)]
        public void SingleDirectActivation(DoorActivation activation, int flag) { var map = Run(new[] { Door(new DoorSettings { Animated = true, Hinge = HingeSide.Right, Activation = activation, Wait = -1 }) }, out var report); var e = map.Entities[1]; Assert.False(e.Properties.ContainsKey("targetname")); Assert.Equal(flag, int.Parse(e.Properties["spawnflags"]) & 256); Assert.Equal("-1", e.Properties["wait"]); }
        [Fact] public void AmbiguousHingeIsOmitted() { var map = Run(new[] { Door(new DoorSettings { Animated = true }) }, out var report); Assert.Single(map.Entities); Assert.Equal("Omitted", report.Doors[0].Status); Assert.Contains("Bisagra", report.Diagnostics[0].Message); }
        [Fact] public void ReverseMatchesValidatedDirection() { var a = Run(new[] { Door() }, out _); var b = Run(new[] { Door(new DoorSettings { Animated = true, Hinge = HingeSide.Left, ReverseDirection = true }) }, out _); Assert.NotEqual(int.Parse(a.Entities[1].Properties["spawnflags"]) & 2, int.Parse(b.Entities[1].Properties["spawnflags"]) & 2); }
        [Theory]
        [InlineData(0)]
        [InlineData(45)]
        [InlineData(90)]
        public void SlidingLipMatchesEngineExpandedBounds(double angle)
        { var frame = new Frame(Vec3.Zero, DoorMotion.RotateVector(Vec3.XAxis, angle), DoorMotion.RotateVector(Vec3.YAxis, angle), Vec3.ZAxis); var map = Run(new[] { Door(new DoorSettings { Animated = true, Kind = DoorKind.Sliding, SlideDirection = SlideDirection.Right, SlideDistanceFeet = 40 }, frame) }, out var report); Assert.Equal("Animated", report.Doors[0].Status); var e = map.Entities[1]; Assert.Equal("func_door", e.Properties["classname"]); Assert.False(e.Properties.ContainsKey("distance")); var bound = DoorMotion.Bounds(new Box(frame, new Vec3(-18, -1, 0), new Vec3(18, 1, 80)).Vertices); var size = bound.Max - bound.Min + new Vec3(2, 2, 2); double native = Math.Abs(frame.U.X * (size.X - 2)) + Math.Abs(frame.U.Y * (size.Y - 2)) + Math.Abs(frame.U.Z * (size.Z - 2)) - double.Parse(e.Properties["lip"], CultureInfo.InvariantCulture); Assert.Equal(40, native, 7); }
        [Theory]
        [InlineData(SlideDirection.Up, "0 -1 0")]
        [InlineData(SlideDirection.Down, "0 -2 0")]
        public void VerticalSlideUsesSdkSentinels(SlideDirection direction, string angles) { var map = Run(new[] { Door(new DoorSettings { Animated = true, Kind = DoorKind.Sliding, SlideDirection = direction }) }, out _); Assert.Equal(angles, map.Entities[1].Properties["angles"]); }
        [Fact] public void IntermediateArcCollisionIsDetected() { var door = Door(); var box = door.Leaves[0].GeometryFeet; var pivot = new Vec3(-18, 1, 40); var mid = DoorMotion.Rotate(box, pivot, 45); var center = mid.Frame.ToWorld(new Vec3(12, 0, 40)); var obstacle = B(center.X - 1, center.Y - 1, 35, 2, 2, 10); Assert.False(DoorMotion.Intersects(box, obstacle, T)); Assert.False(DoorMotion.Intersects(DoorMotion.Rotate(box, pivot, 90), obstacle, T)); var map = Run(new[] { door }, out var report, new[] { obstacle }); Assert.Single(map.Entities); Assert.Equal("Omitted", report.Doors[0].Status); }
        [Fact] public void UnknownGeometryNearArcIsOmitted() { Run(new[] { Door() }, out var report, unknown: new[] { B(0, 10, 0, 10, 10, 80) }); Assert.Equal("Omitted", report.Doors[0].Status); Assert.Contains("no evaluable", report.Diagnostics[0].Message); }
        [Fact] public void LinearSweepDetectsObstacleBetweenEndpoints() { var d = Door(new DoorSettings { Animated = true, Kind = DoorKind.Sliding, SlideDirection = SlideDirection.Right, SlideDistanceFeet = 100 }); Run(new[] { d }, out var report, new[] { B(40, -.5, 5, 2, 1, 2) }); Assert.Equal("Omitted", report.Doors[0].Status); }
        [Fact] public void ContactWithoutPenetrationIsAllowed() { var floor = B(-100, -100, -8, 200, 200, 8); Run(new[] { Door() }, out var report, new[] { floor }); Assert.Equal("Animated", report.Doors[0].Status); Assert.False(DoorMotion.Intersects(B(0, 0, 0, 1, 1, 1), B(1, 0, 0, 1, 1, 1), T)); }
        [Theory]
        [InlineData(DoorActivation.Use, "func_button")]
        [InlineData(DoorActivation.Touch, "trigger_multiple")]
        public void DoubleLeavesHaveOwnOriginsAndSharedActivator(DoorActivation activation, string classname)
        { var leaves = new[] { new DoorLeaf(B(-36, -1, 0, 35, 2, 80), HingeSide.Left), new DoorLeaf(B(1, -1, 0, 35, 2, 80), HingeSide.Right) }; var d = Door(new DoorSettings { Animated = true, Activation = activation, LeafCount = DoorLeafCount.Double }, leaves: leaves); var map = Run(new[] { d }, out var report); Assert.True(report.Doors[0].Status == "Animated", string.Join(";", report.Diagnostics.Select(d => d.Message))); var entities = map.Entities.Where(e => e.Properties["classname"] == "func_door_rotating").ToList(); Assert.Equal(2, entities.Count); Assert.Equal(entities[0].Properties["targetname"], entities[1].Properties["targetname"]); Assert.Equal(entities[0].Properties["targetname"], map.Entities.Single(e => e.Properties["classname"] == classname).Properties["target"]); Assert.Equal(2, entities.Sum(e => e.Brushes.Count(b => b.Role == BrushRole.Origin))); }
        [Fact] public void TriggerUsesOnlyConnectedName() { var map = Run(new[] { Door(new DoorSettings { Animated = true, Hinge = HingeSide.Left, Activation = DoorActivation.Trigger }) }, out var report); Assert.Equal("Animated", report.Doors[0].Status); Assert.Equal(map.Entities[1].Properties["targetname"], map.Entities[2].Properties["target"]); Assert.Equal("trigger_multiple", map.Entities[2].Properties["classname"]); Assert.Equal(BrushRole.Trigger, map.Entities[2].Brushes[0].Role); }
        [Fact] public void InvalidManualActivatorOmitsWholeDoor() { Run(new[] { Door(new DoorSettings { Animated = true, Hinge = HingeSide.Left, Activation = DoorActivation.Trigger, ManualActivator = true }) }, out var report); Assert.Equal("Omitted", report.Doors[0].Status); }
        [Fact] public void GroupMismatchOmitsBoth() { var a = Door(new DoorSettings { Animated = true, Hinge = HingeSide.Left, Group = "pair" }); var b = Door(new DoorSettings { Animated = true, Hinge = HingeSide.Left, Group = "pair", Speed = 50 }, new Frame(new Vec3(150, 0, 0), Vec3.XAxis, Vec3.YAxis, Vec3.ZAxis), id: 2); var map = Run(new[] { a, b }, out var report); Assert.Single(map.Entities); Assert.Equal(2, report.Diagnostics.Count); }
        [Theory]
        [InlineData(0, 100, 3)]
        [InlineData(90, 0, 3)]
        [InlineData(double.NaN, 100, 3)]
        [InlineData(90, 100, -2)]
        [InlineData(181, 100, 3)]
        public void InvalidParameters(double angle, double speed, double wait) { Assert.Throws<ArgumentException>(() => new DoorSettings { OpenAngleDegrees = angle, Speed = speed, Wait = wait }.Validate()); }
        [Fact]
        public void CrossingMovingLeavesAreNotBothAccepted()
        {
            var settings = new DoorSettings { Animated = true, Kind = DoorKind.Sliding, SlideDirection = SlideDirection.Right, SlideDistanceFeet = 200 };
            var a = Door(settings, new Frame(new Vec3(-100, 0, 0), Vec3.XAxis, Vec3.YAxis, Vec3.ZAxis));
            var b = Door(settings, new Frame(new Vec3(0, -100, 0), Vec3.YAxis, -Vec3.XAxis, Vec3.ZAxis), id: 2);
            var map = Run(new[] { a, b }, out var report); Assert.Equal("Animated", report.Doors[0].Status); Assert.Equal("Omitted", report.Doors[1].Status); Assert.Equal(2, map.Entities.Count); Assert.Contains("recorrido de dos hojas", report.Diagnostics[0].Message);
        }
        [Fact]
        public void ManualHingeOffsetsAreAppliedInLocalFrame()
        {
            var frame = new Frame(new Vec3(50, 60, 0), Vec3.YAxis, -Vec3.XAxis, Vec3.ZAxis);
            var d = Door(new DoorSettings { Animated = true, Hinge = HingeSide.Right, HingeOffsetXFeet = .5, HingeOffsetYFeet = .25 }, frame);
            var map = Run(new[] { d }, out var report); Assert.Equal("Animated", report.Doors[0].Status);
            var points = ConvexGeometryValidator.Validate(map.Entities[1].Brushes.Single(b => b.Role == BrushRole.Origin), T).Vertices;
            var pivot = points.Aggregate(Vec3.Zero, (a, b) => a + b) / points.Count; Assert.True((pivot - frame.ToWorld(new Vec3(18.5, 1.25, 40))).Length < 1e-7);
        }
        [Fact]
        public void SeparatedInstancesShareActivationGroup()
        {
            var a = Door(new DoorSettings { Animated = true, Hinge = HingeSide.Left, Group = "entry" });
            var b = Door(new DoorSettings { Animated = true, Hinge = HingeSide.Right, Group = "entry" }, new Frame(new Vec3(150, 0, 0), Vec3.XAxis, Vec3.YAxis, Vec3.ZAxis), id: 2);
            var map = Run(new[] { a, b }, out var report); Assert.All(report.Doors, i => Assert.Equal("Animated", i.Status));
            Assert.Equal(map.Entities[1].Properties["targetname"], map.Entities[2].Properties["targetname"]);
            Assert.Equal(map.Entities[1].Properties["targetname"], map.Entities[3].Properties["target"]);
        }
        [Fact] public void ToolWadHasOwnTexturesButCannotSupplyArchitecturalTexture() { using (var memory = new MemoryStream(ToolBrushes.CreateWad())) { var wad = WadCatalog.Read(memory); Assert.Contains("ORIGIN", wad.Textures); Assert.Contains("AAATRIGGER", wad.Textures); Assert.Throws<ArgumentException>(() => wad.Require("ORIGIN")); } }
        [Fact] public void OriginInWorldIsRejected() { var map = new MapDocument(); var world = new MapEntity("worldspawn"); map.Entities.Add(world); world.Brushes.Add(ToolBrushes.Origin(Vec3.Zero, T)); Assert.Throws<ArgumentException>(() => MapSerializer.Serialize(map, T)); }
        [Theory]
        [InlineData("C:\\with spaces\\map", "\"C:\\with spaces\\map\"")]
        [InlineData("tail\\", "\"tail\\\\\"")]
        [InlineData("x\"y", "\"x\\\"y\"")]
        public void WindowsArgumentsAreQuoted(string input, string expected) => Assert.Equal(expected, BspCompiler.QuoteArgument(input));
    }
}
