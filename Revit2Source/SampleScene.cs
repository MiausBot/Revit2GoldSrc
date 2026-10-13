using System;
using System.Collections.Generic;
using System.Linq;
using Revit2GoldSrc.Core;

namespace Revit2Source
{
    public static class SourceSample
    {
        public static SourceExportResult Room(FgdCatalog? fgd = null)
        {
            double F(double meters) => meters / .3048;
            Box B(double x, double y, double z, double dx, double dy, double dz) => new Box(Frame.Identity, new Vec3(F(x), F(y), F(z)), new Vec3(F(x + dx), F(y + dy), F(z + dz)));
            var material = new BimMaterial
            {
                Id = 1,
                Name = "Pintura beige",
                Color = new RgbColor(215, 195, 165)
            };
            var tile = new BimMaterial
            {
                Id = 2,
                Name = "Baldosa 60 cm",
                WidthFeet = F(.6),
                HeightFeet = F(.6)
            };
            var pixels = new byte[64 * 64 * 4];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int i = (y * 64 + x) * 4;
                    byte c = (byte)(x < 2 || y < 2 ? 80 : 205);
                    pixels[i] = c;
                    pixels[i + 1] = c;
                    pixels[i + 2] = c;
                    pixels[i + 3] = 255;
                }

            tile.Image = new PixelImage(64, 64, pixels);
            var boxes = new[]
            {
                B(-.15, -.15, -.2, 4.3, 5.3, .2),
                B(-.15, -.15, 3, 4.3, 5.3, .15),
                B(-.15, -.15, 0, .15, 5.3, 3),
                B(4, -.15, 0, .15, 5.3, 3),
                B(0, -.15, 0, 4, .15, 3),
                B(0, 5, 0, 4, .15, 3),
                B(0, 2.45, 0, 1.55, .1, 3),
                B(2.45, 2.45, 0, 1.55, .1, 3),
                B(1.55, 2.45, 2.1, .9, .1, .9)
            };
            var elements = boxes.Select((b, i) => new BimElement(i + 1, i == 0 ? BimCategory.Floor : BimCategory.Wall, Array.Empty<Box>(), convexSolids: new[] { new ConvexSolid(SourceGeometry.FromBox(b, 1e-5).Faces.Select(f => new MeshFace(f.Points, i == 0 ? tile : material)).ToList()) }) { CategoryName = i == 0 ? "Floor" : i == 1 ? "Ceiling" : "Wall" }).ToList();
            var frame = new Frame(new Vec3(F(2), F(2.5), 0), Vec3.XAxis, Vec3.YAxis, Vec3.ZAxis);
            var leaf = new Box(frame, new Vec3(F(-.43), F(-.02), F(.01)), new Vec3(F(.43), F(.02), F(2.08)));
            var door = new DoorDefinition(100, frame, new DoorSettings { Animated = true, Hinge = HingeSide.Left, Wait = 3 }, new[] { new DoorLeaf(leaf, HingeSide.Left) });
            var config = new SourceConfiguration
            {
                Document = "Synthetic C# room"
            };
            int order = 1;
            foreach (var p in new[]
            {
                new Vec3(1, 1, 0),
                new Vec3(3, 1, 0),
                new Vec3(1, 4, 0),
                new Vec3(3, 4, 0)
            }

            )
            {
                config.Placements.Add(new SourcePlacement { Kind = "Survivor", X = F(p.X), Y = F(p.Y), Z = 0, SurvivorOrder = order++ });
                config.Placements.Add(new SourcePlacement { Kind = "Weapon", X = F(p.X), Y = F(p.Y + .4), Z = F(.12) });
                config.Placements.Add(new SourcePlacement { Kind = "Medkit", X = F(p.X + .4), Y = F(p.Y), Z = F(.12) });
            }

            foreach (double y in new[]
            {
                1.2,
                3.8
            }

            )
                config.Placements.Add(new SourcePlacement { Kind = "Light", X = F(2), Y = F(y), Z = F(2.8) });
            return SourceExporter.Prepare(elements, new SourceOptions { MapName = "revit_l4d2_room", Fgd = fgd, Configuration = config, AutoLight = false, AutoSurvivors = false }, doors: new[] { door });
        }
    }
}
