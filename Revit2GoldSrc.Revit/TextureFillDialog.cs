using System.Windows;
using System.Windows.Controls;
using Revit2GoldSrc.GoldSrc;
namespace Revit2GoldSrc.Revit
{
    public sealed class TextureFillDialog : Window
    {
        public TextureFillMode Mode { get; private set; } = TextureFillMode.Cancel;
        public TextureFillDialog()
        {
            Title = "Rellenar texturas recomendadas";
            Width = 610; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var panel = new StackPanel { Margin = new Thickness(18) }; Content = panel;
            panel.Children.Add(new TextBlock { Text = "¿Cómo quieres aplicar las recomendaciones del WAD cargado?", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = "Se incluyen muros, pisos, columnas y recinto. Después puedes ajustar cada textura manualmente.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            panel.Children.Add(buttons);
            Add(buttons, "Solo campos vacíos", TextureFillMode.EmptyOnly, true, false);
            Add(buttons, "Reemplazar todas", TextureFillMode.ReplaceAll, false, false);
            Add(buttons, "Cancelar", TextureFillMode.Cancel, false, true);
        }
        private void Add(Panel panel, string text, TextureFillMode mode, bool isDefault, bool isCancel)
        {
            var button = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(12, 7, 12, 7) };
            IconAssets.Attach(button,mode==TextureFillMode.Cancel?PluginIcon.Cancel:PluginIcon.Recommend,mode==TextureFillMode.Cancel?"Cerrar sin modificar asignaciones.":"Aplicar las recomendaciones elegidas.");
            button.Click += (sender, args) => { Mode = mode; DialogResult = mode != TextureFillMode.Cancel; };
            panel.Children.Add(button);
        }
    }
}
