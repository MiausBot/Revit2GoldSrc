using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using UIDocument = Autodesk.Revit.UI.UIDocument;
using Microsoft.Win32;
using Revit2GoldSrc.Core;
using Revit2Source;
using Revit2Source.Compilation;

namespace Revit2GoldSrc.Revit
{
    internal sealed class SourceExportWindow : Window
    {
        private readonly UIDocument ui;
        private readonly SourceSession state;
        private readonly StackPanel form = new StackPanel();
        private readonly TextBox output = new TextBox(), game = new TextBox(), scale = new TextBox(), tolerance = new TextBox(), ox = new TextBox(), oy = new TextBox(), oz = new TextBox();
        private readonly ComboBox scope = new ComboBox(), quality = new ComboBox();
        private readonly CheckBox center = new CheckBox
        {
            Content = "Centrar origen"
        }, spawn = new CheckBox
        {
            Content = "Buscar cuatro posiciones de supervivientes"
        }, light = new CheckBox
        {
            Content = "Generar luz si no hay luces manuales"
        };
        private readonly Dictionary<BuiltInCategory, CheckBox> categories = new Dictionary<BuiltInCategory, CheckBox>();
        private readonly TextBox log = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        private readonly TextBlock status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };
        private readonly ProgressBar progress = new ProgressBar
        {
            Height = 5
        };
        private readonly DataGrid points = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            Height = 180
        }, doors = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            Height = 150
        };
        private readonly List<Button> buttons = new List<Button>();
        private Button compile = null!;
        private bool busy, cancelled;
        private CancellationTokenSource? cancellation;
        private readonly Stopwatch refresh = Stopwatch.StartNew();
        public SourceExportWindow(UIDocument ui, SourceSession state)
        {
            this.ui = ui;
            this.state = state;
            Title = "Revit2Source / Left 4 Dead 2 / Revit " + App.BuiltFor;
            Width = Math.Min(1080, SystemParameters.WorkArea.Width);
            Height = Math.Min(850, SystemParameters.WorkArea.Height);
            MinWidth = Math.Min(560, SystemParameters.WorkArea.Width);
            MinHeight = Math.Min(360, SystemParameters.WorkArea.Height);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            output.Text = state.Output;
            game.Text = state.GameRoot;
            scale.Text = state.Scale;
            tolerance.Text = state.Tolerance;
            ox.Text = state.OriginX;
            oy.Text = state.OriginY;
            oz.Text = state.OriginZ;
            center.IsChecked = state.Center;
            spawn.IsChecked = state.AutoSurvivors;
            light.IsChecked = state.AutoLight;
            log.Text = state.Log;
            status.Text = state.Status;
            scope.Items.Add("Solo elementos seleccionados");
            scope.Items.Add("Arquitectura del documento anfitrión");
            scope.SelectedIndex = state.Selected ? 0 : 1;
            quality.Items.Add("Equilibrado - 5 mm / 3 %");
            quality.Items.Add("Máximo detalle - 1 mm / 1 %");
            quality.SelectedIndex = state.Quality;
            Row("Ámbito", scope);
            Row("Calidad", quality);
            var cats = new WrapPanel();
            foreach (var c in SourceRevitReader.Categories.Where(c => c != BuiltInCategory.OST_StairsRuns && c != BuiltInCategory.OST_StairsLandings))
            {
                var box = new CheckBox
                {
                    Content = Category.GetCategory(ui.Document, c)?.Name ?? c.ToString(),
                    IsChecked = state.Categories.Contains(c),
                    Margin = new Thickness(0, 4, 12, 4)
                };
                categories.Add(c, box);
                cats.Children.Add(box);
            }

            Row("Arquitectura: vínculos y MEP fuera de esta entrega", cats);
            var target = new DockPanel();
            var choose = ActionButton("Elegir...", PluginIcon.Save, "Seleccionar destino VMF", () =>
            {
                var d = new SaveFileDialog
                {
                    Filter = "VMF (*.vmf)|*.vmf",
                    FileName = Path.GetFileName(output.Text),
                    OverwritePrompt = false
                };
                if (d.ShowDialog(this) == true)
                    output.Text = d.FileName;
            });
            DockPanel.SetDock(choose, Dock.Right);
            target.Children.Add(choose);
            target.Children.Add(output);
            Row("Destino VMF (nombre ASCII sin espacios)", target);
            Row("Carpeta L4D2: contiene bin y left4dead2", game);
            Row("Unidades Source por pie", scale);
            Row("Tolerancia en pies", tolerance);
            form.Children.Add(center);
            var origins = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            foreach (var box in new[]
            {
                ox,
                oy,
                oz
            }

            )
            {
                box.Width = 140;
                box.Margin = new Thickness(0, 0, 6, 0);
                origins.Children.Add(box);
            }

            Row("Origen manual X / Y / Z (pies internos Revit)", origins);
            center.Checked += (s, e) => Origins();
            center.Unchecked += (s, e) => Origins();
            Origins();
            form.Children.Add(spawn);
            form.Children.Add(light);
            var kind = new ComboBox
            {
                ItemsSource = new[]
                {
                    "Light",
                    "Survivor",
                    "Weapon",
                    "Medkit",
                    "Checkpoint"
                },
                SelectedIndex = 0,
                Width = 120
            };
            var pointActions = new WrapPanel();
            pointActions.Children.Add(kind);
            pointActions.Children.Add(ActionButton("Elegir punto en Revit", PluginIcon.Prepare, "Seleccionar punto del plano de trabajo y ajustar Z en la tabla", () => Pick((string)kind.SelectedItem)));
            pointActions.Children.Add(ActionButton("Añadir manual", PluginIcon.Prepare, "Crear posición editable en pies internos", () =>
            {
                state.Configuration.Placements.Add(new SourcePlacement { Kind = (string)kind.SelectedItem, SurvivorOrder = Math.Min(4, state.Configuration.Placements.Count(p => p.Kind == "Survivor") + 1) });
                points.Items.Refresh();
            }));
            pointActions.Children.Add(ActionButton("Eliminar punto", PluginIcon.Cancel, "Eliminar colocación seleccionada", () =>
            {
                if (points.SelectedItem is SourcePlacement p)
                {
                    state.Configuration.Placements.Remove(p);
                    points.Items.Refresh();
                }
            }));
            Row("Colocaciones: pies del jugador; pivote para refugios", pointActions);
            Columns(points, new[] { "Kind", "X", "Y", "Z", "Yaw", "SurvivorOrder", "LightColor", "Model", "OpeningWidthFeet", "OpeningHeightFeet" });
            points.ItemsSource = state.Configuration.Placements;
            form.Children.Add(points);
            var doorActions = new WrapPanel();
            doorActions.Children.Add(ActionButton("Seleccionar puerta Revit", PluginIcon.Prepare, "Configuración Source independiente de parámetros GoldSrc", () => Pick("Door")));
            doorActions.Children.Add(ActionButton("Eliminar puerta", PluginIcon.Cancel, "Conservar el vano libre", () =>
            {
                if (doors.SelectedItem is SourceDoorConfig d)
                {
                    state.Configuration.Doors.Remove(d);
                    doors.Items.Refresh();
                }
            }));
            Row("Puertas: Rotating / Sliding / Static; Auto/Left/Right mirando desde FacingOrientation", doorActions);
            Columns(doors, new[] { "ElementId", "Kind", "Hinge", "Angle", "Speed", "Wait", "Reverse", "SlideDirection", "SlideDistanceFeet" });
            doors.ItemsSource = state.Configuration.Doors;
            form.Children.Add(doors);
            var configs = new WrapPanel();
            configs.Children.Add(ActionButton("Cargar JSON", PluginIcon.Load, "Cargar configuración lateral del mismo documento", Load));
            configs.Children.Add(ActionButton("Guardar JSON", PluginIcon.Save, "Guardar sin modificar Revit", Save));
            form.Children.Add(configs);
            form.Children.Add(new TextBlock { Text = "Materiales Revit: VMT/VTF propios. Las geometrías inseguras se omiten. Source BSP, navegación y validación jugable son etapas separadas.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
            var footer = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            footer.Children.Add(ActionButton("Analizar", PluginIcon.Analyze, "Validar sin publicar", () => Run(false)));
            footer.Children.Add(ActionButton("Exportar VMF", PluginIcon.Export, "Publicar geometría, materiales, configuración y reporte", () => Run(true)));
            compile = ActionButton("Compilar BSP", PluginIcon.Compile, "Ejecutar VBSP, VVIS y VRAD", Compile);
            compile.IsEnabled = state.LastVmf != null;
            footer.Children.Add(compile);
            footer.Children.Add(ActionButton("Escena de prueba", PluginIcon.Export, "Habitación sintética 4 x 5 m, independiente del modelo", Sample));
            footer.Children.Add(ActionButton("Guía Nav Mesh", PluginIcon.Analyze, "Pasos manuales dentro del juego", () => MessageBox.Show(this, "La guía se publica junto al VMF. Cargue el mapa en L4D2 y revise nav_generate, nav_edit, nav_analyze, nav_trouble_report y nav_save. No se inicia el juego ni se declara Director validado.", "Nav Mesh pendiente")));
            footer.Children.Add(ActionButton("Cancelar / Cerrar", PluginIcon.Cancel, "Cancelar operación o cerrar", () =>
            {
                if (busy)
                {
                    cancelled = true;
                    cancellation?.Cancel();
                    status.Text = "Cancelando...";
                }
                else
                    Close();
            }));
            Content = ExportWindowLayout.Create(form, progress, status, log, footer);
            Closing += (s, e) =>
            {
                if (busy)
                {
                    cancelled = true;
                    cancellation?.Cancel();
                    e.Cancel = true;
                }
                else
                    Snapshot();
            };
        }

        private void Origins()
        {
            foreach (var box in new[]
            {
                ox,
                oy,
                oz
            }

            )
                box.IsEnabled = center.IsChecked != true;
        }

        private static void Columns(DataGrid grid, IEnumerable<string> names)
        {
            foreach (string name in names)
                grid.Columns.Add(new DataGridTextColumn { Header = name, Binding = new System.Windows.Data.Binding(name), IsReadOnly = name == "ElementId" });
        }

        private void Row(string text, UIElement control)
        {
            form.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 3), TextWrapping = TextWrapping.Wrap });
            form.Children.Add(control);
        }

        private Button ActionButton(string text, PluginIcon icon, string help, Action action)
        {
            var b = new Button
            {
                Margin = new Thickness(4),
                Padding = new Thickness(8, 5, 8, 5)
            };
            IconAssets.Attach(b, icon, help);
            b.Click += (s, e) => action();
            buttons.Add(b);
            return b;
        }

        private void Snapshot(bool validate = false)
        {
            bool valid = points.CommitEdit(DataGridEditingUnit.Cell, true) && points.CommitEdit(DataGridEditingUnit.Row, true)
                && doors.CommitEdit(DataGridEditingUnit.Cell, true) && doors.CommitEdit(DataGridEditingUnit.Row, true);
            if (validate && !valid) throw new ArgumentException("Corrija los valores inválidos de las tablas antes de exportar o guardar.");
            state.Output = output.Text;
            state.GameRoot = game.Text;
            state.Scale = scale.Text;
            state.Tolerance = tolerance.Text;
            state.OriginX = ox.Text;
            state.OriginY = oy.Text;
            state.OriginZ = oz.Text;
            state.Center = center.IsChecked == true;
            state.AutoSurvivors = spawn.IsChecked == true;
            state.AutoLight = light.IsChecked == true;
            state.Selected = scope.SelectedIndex == 0;
            state.Quality = quality.SelectedIndex;
            state.Categories = new HashSet<BuiltInCategory>(categories.Where(c => c.Value.IsChecked == true).Select(c => c.Key));
            state.Log = log.Text;
            state.Status = status.Text;
        }

        private void Pick(string kind)
        {
            Snapshot();
            state.Pick = kind;
            Close();
        }

        private static double Number(string text)
        {
            if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double n) && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out n)) || !Vec3.Finite(n))
                throw new ArgumentException("Número inválido: " + text);
            return n;
        }

        private void Update(string text)
        {
            if (cancelled)
                throw new OperationCanceledException();
            if (refresh.ElapsedMilliseconds < 100)
                return;
            status.Text = text;
            Dispatcher.Invoke(() =>
            {
            }, DispatcherPriority.Background);
            refresh.Restart();
            if (cancelled)
                throw new OperationCanceledException();
        }

        private void Busy(bool value)
        {
            busy = value;
            form.IsEnabled = !value;
            foreach (var b in buttons)
                b.IsEnabled = !value;
            buttons.Last().IsEnabled = true;
            if (!value)
                compile.IsEnabled = state.LastVmf != null;
            progress.IsIndeterminate = value;
        }

        private SourceToolchain Tools() => SourceToolchain.Detect(game.Text);
        private SourceOptions Options()
        {
            var tools = Tools();
            GameAssets? resources = state.Configuration.Placements.Any(p => p.Kind == "Checkpoint") && File.Exists(Path.Combine(tools.GameDirectory, "gameinfo.txt")) ? new GameAssets(tools.GameRoot) : null;
            return new SourceOptions
            {
                MapName = Path.GetFileNameWithoutExtension(output.Text),
                UnitsPerFoot = Number(scale.Text),
                ToleranceFeet = Number(tolerance.Text),
                CenterOrigin = center.IsChecked == true,
                OriginFeet = new Vec3(Number(ox.Text), Number(oy.Text), Number(oz.Text)),
                AutoSurvivors = spawn.IsChecked == true,
                AutoLight = light.IsChecked == true,
                Configuration = state.Configuration,
                Fgd = File.Exists(tools.Fgd) ? FgdCatalog.Read(tools.Fgd) : null,
                ResolveModel = resources == null ? null : new Func<string, ModelBounds?>(resources.DoorBounds)
            };
        }

        private bool Confirm()
        {
            string file = Path.GetFullPath(output.Text);
            if (Path.GetExtension(file).ToLowerInvariant() != ".vmf")
                throw new ArgumentException("Destino debe ser VMF.");
            return !File.Exists(file) || MessageBox.Show(this, "Ya existe el VMF. ¿Reemplazar sus archivos asociados?", "Revit2Source", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        }

        private void Run(bool publish)
        {
            if (busy)
                return;
            try
            {
                Snapshot(true);
                var options = Options();
                if (publish && !Confirm())
                    return;
                cancelled = false;
                Busy(true);
                log.Clear();
                var data = SourceRevitReader.Read(ui, state.Selected, state.Categories, (GeometryQuality)state.Quality, options.ToleranceFeet, Update);
                var readDoors = SourceRevitReader.Doors(ui, state.Configuration, data, options.ToleranceFeet, Update);
                options.UncertainObstaclesFeet = readDoors.Unknown;
                var result = SourceExporter.Prepare(data.Elements, options, data.Report, readDoors.Doors, Update);
                if (publish)
                {
                    SourcePublication.Publish(result, output.Text);
                    state.LastVmf = Path.GetFullPath(output.Text);
                }

                status.Text = publish ? "VMF y materiales publicados: " + state.LastVmf : "Análisis terminado. No se escribieron archivos.";
                log.Text = "Encontrados: " + result.Report.ElementsFound + " | Convertidos: " + result.Report.ElementsConverted + " | Omitidos: " + result.Report.ElementsSkipped + "\nSólidos: " + result.Report.Solids + " | Entidades: " + result.Report.Entities + "\n" + string.Join("\n", result.Report.Diagnostics.Select(d => (d.ElementId.HasValue ? "Elemento " + d.ElementId + ": " : "") + d.Message));
            }
            catch (OperationCanceledException)
            {
                status.Text = "Cancelado. Archivos anteriores conservados.";
            }
            catch (Exception ex)
            {
                status.Text = "No se publicó exportación.";
                log.AppendText("\n" + ex.Message);
            }
            finally
            {
                Busy(false);
                Snapshot();
            }
        }

        private void Sample()
        {
            if (busy)
                return;
            try
            {
                Snapshot();
                output.Text = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output.Text))!, "revit_l4d2_room.vmf");
                if (!Confirm())
                    return;
                Busy(true);
                var tools = Tools();
                var result = SourceSample.Room(File.Exists(tools.Fgd) ? FgdCatalog.Read(tools.Fgd) : null);
                SourcePublication.Publish(result, output.Text);
                state.LastVmf = Path.GetFullPath(output.Text);
                status.Text = "Escena sintética publicada; BSP y navegación son etapas posteriores.";
                log.Text = result.Report.Solids + " sólidos, " + result.Report.Entities + " entidades.\n" + string.Join("\n", result.Report.Diagnostics.Select(d => d.Message));
            }
            catch (Exception ex)
            {
                status.Text = "Escena no publicada.";
                log.Text = ex.Message;
            }
            finally
            {
                Busy(false);
                Snapshot();
            }
        }

        private async void Compile()
        {
            if (busy || state.LastVmf == null)
                return;
            cancelled = false;
            Busy(true);
            cancellation = new CancellationTokenSource();
            try
            {
                var result = await SourceCompiler.CompileAsync(state.LastVmf, Tools(), new Progress<string>(text =>
                {
                    status.Text = text;
                    log.AppendText("\n" + text);
                }), cancellation.Token);
                status.Text = "BSP L4D2 validado: " + result.BspPath;
                log.AppendText("\nLogs: " + result.Workspace + "\nNav Mesh y prueba en juego pendientes.");
            }
            catch (OperationCanceledException)
            {
                status.Text = "Compilación cancelada.";
            }
            catch (Exception ex)
            {
                status.Text = "Compilación fallida. BSP anterior conservado.";
                log.AppendText("\n" + ex.Message);
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                Busy(false);
                Snapshot();
            }
        }

        private void Load()
        {
            try
            {
                var d = new OpenFileDialog
                {
                    Filter = "Configuración Source (*.json)|*.json"
                };
                if (d.ShowDialog(this) != true)
                    return;
                var config = SourcePublication.ReadJson<SourceConfiguration>(d.FileName);
                if (config.SchemaVersion != 1 || config.Document != SourceRevitReader.DocumentKey(ui.Document))
                    throw new ArgumentException("JSON de otro documento o versión.");
                state.Configuration = config;
                points.ItemsSource = config.Placements;
                doors.ItemsSource = config.Doors;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Configuración inválida");
            }
        }

        private void Save()
        {
            try
            {
                Snapshot(true);
                var d = new SaveFileDialog
                {
                    Filter = "Configuración Source (*.json)|*.json",
                    FileName = Path.GetFileNameWithoutExtension(output.Text) + "_source_config.json"
                };
                if (d.ShowDialog(this) != true)
                    return;
                SourcePublication.AtomicFiles(Path.GetDirectoryName(d.FileName)!, new Dictionary<string, byte[]> { { Path.GetFileName(d.FileName), SourcePublication.Json(state.Configuration) } });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "No se guardó configuración");
            }
        }
    }
}
