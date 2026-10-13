using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Revit2GoldSrc.Revit
{
    public enum PluginIcon { Export, Prepare, Analyze, Recommend, Register, Load, Save, Cancel, Compile }
    public static class IconAssets
    {
        private static readonly Dictionary<PluginIcon, DrawingImage> cache = new Dictionary<PluginIcon, DrawingImage>();
        public static DrawingImage Vector(PluginIcon icon)
        {
            if (cache.TryGetValue(icon, out var image)) return image;
            var group = new DrawingGroup(); using (var draw = group.Open())
            {
                var outline = new SolidColorBrush(Color.FromRgb(105, 128, 150)); var blue = new SolidColorBrush(Color.FromRgb(40, 142, 210)); var gold = new SolidColorBrush(Color.FromRgb(222, 153, 45));
                var line = new Pen(outline, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; var accent = new Pen(blue, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                void Path(string data, Pen? pen = null, Brush? fill = null) => draw.DrawGeometry(fill, pen ?? line, Geometry.Parse(data));
                switch (icon)
                {
                    case PluginIcon.Export:
                        Path("M3,21 L3,4 14,4 14,21 M6,7 L8,7 M11,7 L11,7.1 M6,11 L8,11 M11,11 L11,11.1 M6,15 L8,15 M7,21 L7,18 10,18 10,21"); Path("M12,15 L21,15 M17,11 L21,15 17,19", accent); break;
                    case PluginIcon.Prepare:
                        Path("M3,21 L3,3 14,3 14,12 M6,21 L6,6 11,8 11,21 Z M9,14 L9,14.1"); draw.DrawEllipse(null, new Pen(gold, 2), new Point(17, 17), 3.5, 3.5); Path("M17,11 L17,13 M17,21 L17,23 M11,17 L13,17 M21,17 L23,17 M12.7,12.7 L14,14 M20,20 L21.3,21.3 M12.7,21.3 L14,20 M20,14 L21.3,12.7", new Pen(gold, 1.6)); break;
                    case PluginIcon.Analyze:
                        draw.DrawEllipse(null, accent, new Point(9.5, 9.5), 6.5, 6.5); Path("M14.5,14.5 L21,21", line); Path("M6,10 L8.5,12 13,7", line); break;
                    case PluginIcon.Recommend:
                        Path("M3,3 L19,3 19,19 3,19 Z M3,7 L19,7 M3,11 L19,11 M3,15 L19,15 M7,3 L7,19 M11,3 L11,19 M15,3 L15,19"); foreach (var p in new[] { new Point(3, 3), new Point(11, 3), new Point(7, 7), new Point(15, 7), new Point(3, 11), new Point(11, 11), new Point(7, 15) }) draw.DrawRectangle(blue, null, new Rect(p.X + .5, p.Y + .5, 3, 3)); Path("M16,21 L18,16 23,14 21,19 Z", new Pen(gold, 1.5), gold); break;
                    case PluginIcon.Register:
                        Path("M3,21 L3,3 12,3 17,8 17,13 M12,3 L12,8 17,8 M6,11 L12,11 M6,15 L10,15"); Path("M12,18 L15,15 Q18,12 20,15 Q22,17 19,20 L17,22 M10,20 L12,22 Q14,24 17,21 L20,18", accent); break;
                    case PluginIcon.Load:
                        Path("M3,20 L3,6 10,6 12,9 21,9 21,20 Z"); Path("M12,3 L12,14 M8,10 L12,14 16,10", accent); break;
                    case PluginIcon.Save:
                        Path("M3,3 L18,3 21,6 21,21 3,21 Z M7,3 L7,9 16,9 16,3 M7,21 L7,14 17,14 17,21"); Path("M13,4 L13,7", accent); break;
                    case PluginIcon.Cancel:
                        draw.DrawEllipse(null, line, new Point(12, 12), 9, 9); Path("M8,8 L16,16 M16,8 L8,16", accent); break;
                    case PluginIcon.Compile:
                        Path("M4,3 L9,8 7,10 2,5 Q0,11 7,13 L17,23 21,19 11,9 Q14,2 8,2 L10,6 8,8 Z", line); Path("M4,21 L17,8 M15,3 L20,3 23,6 20,9 16,5", new Pen(gold, 2)); break;
                }
                // Stable 24-unit canvas keeps every icon aligned, including sparse drawings.
                draw.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 24, 24));
            }
            group.Freeze(); image = new DrawingImage(group); image.Freeze(); cache.Add(icon, image); return image;
        }
        public static BitmapSource Ribbon(PluginIcon icon, int pixels)
        {
            var visual = new DrawingVisual(); using (var draw = visual.RenderOpen()) draw.DrawImage(Vector(icon), new Rect(0, 0, pixels, pixels));
            var bitmap = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
        }
        public static void Attach(Button button, PluginIcon icon, string tooltip)
        {
            string label = button.Content?.ToString() ?? ""; var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var image = new Image { Source = Vector(icon), Width = 20, Height = 20, Margin = new Thickness(0, 0, 6, 0), Stretch = Stretch.Uniform };
            var style = new Style(typeof(Image)); var trigger = new DataTrigger { Binding = new Binding("IsEnabled") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) }, Value = false }; trigger.Setters.Add(new Setter(UIElement.OpacityProperty, .35)); style.Triggers.Add(trigger); image.Style = style;
            panel.Children.Add(image); panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }); button.Content = panel; button.ToolTip = tooltip;
        }
    }
}
