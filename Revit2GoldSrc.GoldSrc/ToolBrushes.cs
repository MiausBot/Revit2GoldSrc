using System;
using System.Collections.Generic;
using System.Linq;
using Revit2GoldSrc.Core;
namespace Revit2GoldSrc.GoldSrc
{
    public enum BrushRole { Solid, Origin, Trigger }
    public static class ToolBrushes
    {
        public static GoldSrcBrush Origin(Vec3 hingeHU, double tolerance) => Build(new Box(Frame.Identity, hingeHU - new Vec3(4, 4, 4), hingeHU + new Vec3(4, 4, 4)), BrushRole.Origin, tolerance);
        public static GoldSrcBrush Build(Box box, BrushRole role, double tolerance)
        {
            if (role == BrushRole.Solid) throw new ArgumentException("Se requiere un rol técnico.");
            var solid = BrushBuilder.FromBox(box, role == BrushRole.Origin ? "ORIGIN" : "AAATRIGGER", tolerance);
            return new GoldSrcBrush(solid.Faces, role);
        }
        public static byte[] CreateWad()
        {
            var origin = TextureImageConverter.Convert(PixelImage.Solid(new RgbColor(200, 100, 0)), RgbColor.Neutral); origin.Name = "ORIGIN";
            var trigger = TextureImageConverter.Convert(PixelImage.Solid(new RgbColor(255, 0, 255)), RgbColor.Neutral); trigger.Name = "AAATRIGGER";
            return WadWriter.WriteTools(new[] { origin, trigger });
        }
        public static void Validate(MapDocument map)
        {
            foreach (var entity in map.Entities)
            {
                string classname = entity.Properties["classname"];
                foreach (var brush in entity.Brushes)
                {
                    if (brush.Role == BrushRole.Origin && classname != "func_door_rotating") throw new ArgumentException("ORIGIN solo se permite en func_door_rotating.");
                    if (brush.Role == BrushRole.Trigger && classname != "trigger_multiple") throw new ArgumentException("AAATRIGGER solo se permite en trigger_multiple.");
                    if (brush.Faces.Any(f => string.Equals(f.Texture, "ORIGIN", StringComparison.OrdinalIgnoreCase)) != (brush.Role == BrushRole.Origin) ||
                        brush.Faces.Any(f => string.Equals(f.Texture, "AAATRIGGER", StringComparison.OrdinalIgnoreCase)) != (brush.Role == BrushRole.Trigger)) throw new ArgumentException("Textura técnica usada fuera de su rol.");
                    if (brush.Role != BrushRole.Solid && brush.Faces.Select(f => f.Texture.ToUpperInvariant()).Distinct().Count() != 1) throw new ArgumentException("Brush técnico con texturas mezcladas.");
                }
                if (classname == "func_door_rotating" && (entity.Brushes.Count(b => b.Role == BrushRole.Origin) != 1 || !entity.Brushes.Any(b => b.Role == BrushRole.Solid))) throw new ArgumentException("Puerta abatible sin hoja o sin un único ORIGIN.");
            }
        }
    }
}
