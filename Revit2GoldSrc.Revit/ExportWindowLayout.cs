using System.Windows;
using System.Windows.Controls;

namespace Revit2GoldSrc.Revit
{
    internal static class ExportWindowLayout
    {
        // Star rows constrain both scrollable areas regardless of report length.
        // The action row is measured first and never participates in scrolling.
        public static Grid Create(UIElement form, ProgressBar progress, TextBlock status, TextBox log, UIElement buttons)
        {
            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var settings = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
            root.Children.Add(settings);
            var splitter = new GridSplitter
            {
                Height = 6, Margin = new Thickness(0, 8, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = SystemColors.ControlDarkBrush, ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext, ShowsPreview = true,
                ToolTip = "Arrastrar para ajustar el espacio de opciones y reporte."
            };
            Grid.SetRow(splitter, 1); root.Children.Add(splitter);

            var report = new Grid();
            report.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            report.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            report.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            report.Children.Add(progress);
            status.MaxHeight = 60;
            Grid.SetRow(status, 1); report.Children.Add(status);
            log.MinHeight = 0;
            Grid.SetRow(log, 2); report.Children.Add(log);
            Grid.SetRow(report, 2); root.Children.Add(report);
            Grid.SetRow(buttons, 3); root.Children.Add(buttons);
            return root;
        }
    }
}
