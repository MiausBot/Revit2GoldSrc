using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Threading;
using Revit2GoldSrc.Compilation;
using System.Windows.Controls;
using System.Windows.Threading;
using UIDocument = Autodesk.Revit.UI.UIDocument;
using Microsoft.Win32;
using Revit2GoldSrc.Core;
using Revit2GoldSrc.GoldSrc;
namespace Revit2GoldSrc.Revit
{
    public sealed class ExportWindow : Window
    {
        private readonly UIDocument document;
        private readonly ComboBox profile=new ComboBox(),quality=new ComboBox();
        private readonly CheckBox includeLinks=new CheckBox{Content="Incluir vínculos cargados",IsChecked=true};
        private readonly StackPanel modelCategories=new StackPanel(),categoryRows=new StackPanel();
        private readonly TextBox categorySearch=new TextBox();
        private IReadOnlyList<CategorySelection> modelRules=Array.Empty<CategorySelection>();
        private readonly List<UIElement> architectureTextureControls=new List<UIElement>();
        private readonly Dictionary<string,CheckBox> modelChecks=new Dictionary<string,CheckBox>();
        private readonly Dictionary<string,ComboBox> modelCollisions=new Dictionary<string,ComboBox>(),modelTextures=new Dictionary<string,ComboBox>();
        private readonly ComboBox scope = new ComboBox();
        private readonly ComboBox textureMode = new ComboBox();
        private readonly StackPanel wadSettings = new StackPanel();
        private readonly TextBox output = new TextBox(), wad = new TextBox(), scale = new TextBox { Text = "12" }, tolerance = new TextBox { Text = "0.00001" };
        private readonly TextBox ox = new TextBox { Text = "0" }, oy = new TextBox { Text = "0" }, oz = new TextBox { Text = "0" };
        private readonly CheckBox center = new CheckBox { Content = "Centrar origen en el edificio", IsChecked = true };
        private readonly CheckBox entities = new CheckBox { Content = "Generar jugador e iluminación", IsChecked = true };
        private readonly CheckBox enclosure = new CheckBox { Content = "Generar recinto cerrado de prueba (margen 128 HU)" };
        private readonly Dictionary<BimCategory, CheckBox> categories = new Dictionary<BimCategory, CheckBox>();
        private readonly Dictionary<BimCategory, ComboBox> textures = new Dictionary<BimCategory, ComboBox>();
        private readonly ComboBox enclosureTexture = new ComboBox();
        private readonly TextBox log = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 120 };
        private readonly TextBlock status = new TextBlock { Text = "Listo. El modelo se leerá sin modificaciones.", TextWrapping = TextWrapping.Wrap };
        private readonly ProgressBar progress = new ProgressBar { Height = 5, Margin = new Thickness(0, 8, 0, 8) };
        private readonly Button analyze = new Button { Content = "Analizar modelo" }, export = new Button { Content = "Exportar MAP" }, cancel = new Button { Content = "Cancelar" };
        private readonly StackPanel form = new StackPanel();
        private readonly Button fillTextures = new Button { Content = "Rellenar con texturas recomendadas", IsEnabled = false, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(8, 5, 8, 5), HorizontalAlignment = HorizontalAlignment.Left };
        private IReadOnlyList<string> loadedTextures = Array.Empty<string>();
        private readonly CheckBox exportDoors = new CheckBox { Content="Exportar puertas (desactivado por defecto)" };
        private readonly ComboBox doorTexture = new ComboBox();
        private readonly Button registerWad = new Button { Content="Registrar WAD en J.A.C.K.",IsEnabled=false };
        private readonly Button compile = new Button { Content="Compilar MAP a BSP", IsEnabled=false };
        private readonly TextBox csg = new TextBox(), bsp = new TextBox(), vis = new TextBox(), rad = new TextBox();
        private CancellationTokenSource? compilationCancellation;
        private string? lastMap;
        private bool busy, cancelled;
        private readonly Stopwatch refresh = Stopwatch.StartNew();
        public ExportWindow(UIDocument document)
        {
            this.document = document; Title = "Revit2GoldSrc — Revit " + App.BuiltFor; Width = Math.Min(940, SystemParameters.WorkArea.Width); Height = Math.Min(800, SystemParameters.WorkArea.Height); MinWidth = Math.Min(560, SystemParameters.WorkArea.Width); MinHeight = Math.Min(360, SystemParameters.WorkArea.Height); WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            foreach (var b in new[] { analyze, export, compile, registerWad, cancel }) { b.Margin = new Thickness(6, 0, 0, 0); b.Padding = new Thickness(12, 6, 12, 6); buttons.Children.Add(b); }
            IconAssets.Attach(analyze,PluginIcon.Analyze,"Analizar sólidos y mostrar omisiones sin escribir archivos.");
            IconAssets.Attach(export,PluginIcon.Export,"Exportar MAP, WAD y reporte conservando los contornos compatibles.");
            IconAssets.Attach(compile,PluginIcon.Compile,"Ejecutar CSG, BSP, VIS y RAD y publicar el BSP solo al completar las etapas.");
            IconAssets.Attach(registerWad,PluginIcon.Register,"Registrar los WAD del último MAP en el perfil Half-Life de J.A.C.K.");
            IconAssets.Attach(cancel,PluginIcon.Cancel,"Cancelar la operación en curso o cerrar esta ventana.");
            IconAssets.Attach(fillTextures,PluginIcon.Recommend,"Rellenar asignaciones con recomendaciones basadas en nombres de texturas.");
            Content = ExportWindowLayout.Create(form, progress, status, log, buttons);
            profile.Items.Add("Arquitectura actual");profile.Items.Add("Modelo completo con materiales");profile.SelectedIndex=0;Row("Perfil de exportación",profile);
            modelCategories.Visibility=Visibility.Collapsed;
            quality.Items.Add("Equilibrado — 5 mm / 3 % volumen");quality.Items.Add("Máximo detalle — 1 mm / 1 % volumen");quality.SelectedIndex=0;
            Row("Detalle de familias y MEP",quality,modelCategories);modelCategories.Children.Add(includeLinks);Row("Buscar categoría",categorySearch,modelCategories);
            var actions=new WrapPanel();var selectAll=new Button{Content="Seleccionar todo",Margin=new Thickness(0,6,6,6)};var clearAll=new Button{Content="Desmarcar todo",Margin=new Thickness(0,6,6,6)};
            IconAssets.Attach(selectAll,PluginIcon.Recommend,"Habilitar todas las categorías físicas detectadas.");IconAssets.Attach(clearAll,PluginIcon.Cancel,"Deshabilitar las categorías detectadas.");actions.Children.Add(selectAll);actions.Children.Add(clearAll);modelCategories.Children.Add(actions);
            modelCategories.Children.Add(new ScrollViewer{Content=categoryRows,MaxHeight=240,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});form.Children.Add(modelCategories);
            selectAll.Click+=(sender,args)=>{foreach(var check in modelChecks.Values)check.IsChecked=true;};clearAll.Click+=(sender,args)=>{foreach(var check in modelChecks.Values)check.IsChecked=false;};
            categorySearch.TextChanged+=(sender,args)=>FilterCategories();
            scope.Items.Add("Modelo arquitectónico completo"); scope.Items.Add("Solo elementos seleccionados"); scope.SelectedIndex = 0; Row("Ámbito", scope);
            foreach (var pair in new[] { new KeyValuePair<BimCategory, string>(BimCategory.Wall, "Muros"), new KeyValuePair<BimCategory, string>(BimCategory.Floor, "Pisos y losas"), new KeyValuePair<BimCategory, string>(BimCategory.Column, "Columnas") })
            {
                var check = new CheckBox { Content = pair.Value, IsChecked = true }; categories.Add(pair.Key, check); form.Children.Add(check);
            }
            output.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "edificio_recuperado.map");
            var outputPanel = new DockPanel(); var chooseOutput = new Button { Content = "Elegir…", Margin = new Thickness(6, 0, 0, 0) }; DockPanel.SetDock(chooseOutput, Dock.Right); outputPanel.Children.Add(chooseOutput); outputPanel.Children.Add(output); Row("Archivo .MAP / nombre del mapa", outputPanel);
            IconAssets.Attach(chooseOutput,PluginIcon.Save,"Elegir el destino y nombre del MAP.");
            chooseOutput.Click += (s, e) => { var dialog = new SaveFileDialog { Filter = "GoldSrc MAP (*.map)|*.map", DefaultExt = ".map", FileName = string.IsNullOrWhiteSpace(output.Text) ? "edificio.map" : Path.GetFileName(output.Text), OverwritePrompt = false }; if (dialog.ShowDialog(this) == true) output.Text = dialog.FileName; };
            Row("Escala (HU por pie de Revit)", scale); Row("Tolerancia geométrica (pies)", tolerance); form.Children.Add(center);
            Row("Origen manual X / Y / Z (pies internos Revit)", new StackPanel { Orientation = Orientation.Horizontal, Children = { ox, oy, oz } });
            foreach (var box in new[] { ox, oy, oz }) { box.Width = 120; box.Margin = new Thickness(0, 0, 6, 0); box.IsEnabled = false; }
            center.Checked += (s, e) => SetOriginEnabled(false); center.Unchecked += (s, e) => SetOriginEnabled(true);
            textureMode.Items.Add("WAD existente"); textureMode.Items.Add("Materiales de Revit (WAD propio)"); textureMode.SelectedIndex = 0;
            Row("Origen de las texturas", textureMode);
            textureMode.SelectionChanged += (sender, args) => { wadSettings.IsEnabled = textureMode.SelectedIndex == 0;foreach(var combo in modelTextures.Values)combo.IsEnabled=textureMode.SelectedIndex==0; };
            form.Children.Add(wadSettings);
            var wadPanel = new DockPanel(); var chooseWad = new Button { Content = "Cargar WAD…", Margin = new Thickness(6, 0, 0, 0) }; DockPanel.SetDock(chooseWad, Dock.Right); wadPanel.Children.Add(chooseWad); wadPanel.Children.Add(wad); wad.IsReadOnly = true; Row("Archivo WAD3 existente", wadPanel, wadSettings);
            IconAssets.Attach(chooseWad,PluginIcon.Load,"Leer un WAD3 y mostrar sus texturas sólidas disponibles.");
            chooseWad.Click += (s, e) => LoadWad();
            wadSettings.Children.Add(fillTextures);
            fillTextures.Click += (s, e) => FillTextures();
            foreach (var pair in categories)
            {
                var combo = new ComboBox(); textures.Add(pair.Key, combo); Row("Textura: " + pair.Value.Content, combo, wadSettings);architectureTextureControls.Add(wadSettings.Children[wadSettings.Children.Count-2]);architectureTextureControls.Add(combo);
            }
            form.Children.Add(exportDoors); Row("Textura de puerta (WAD existente)",doorTexture,wadSettings);
            var detected=CompilerOptions.Detect(); csg.Text=detected.Csg;bsp.Text=detected.Bsp;vis.Text=detected.Vis;rad.Text=detected.Rad;
            var compilerFields=new StackPanel(); Row("CSG",csg,compilerFields);Row("BSP",bsp,compilerFields);Row("VIS",vis,compilerFields);Row("RAD",rad,compilerFields);
            form.Children.Add(new Expander{Header="Compiladores GoldSrc externos (rutas .exe)",Content=compilerFields});
            registerWad.Click += (sender,args)=>RegisterWad();
            compile.Click += async (sender,args) => {
                if(busy||lastMap==null)return;
                busy=true;form.IsEnabled=false;analyze.IsEnabled=false;export.IsEnabled=false;compile.IsEnabled=false;registerWad.IsEnabled=false;progress.IsIndeterminate=true;compilationCancellation=new CancellationTokenSource();
                try {
                    var paths=new CompilerOptions{Csg=csg.Text,Bsp=bsp.Text,Vis=vis.Text,Rad=rad.Text};
                    var result=await BspCompiler.CompileAsync(lastMap,paths,new Progress<string>(text=>{status.Text=text;log.AppendText("\n"+text);}),compilationCancellation.Token);
                    status.Text="BSP validado: "+result.BspPath; log.AppendText("\nLogs: "+result.Workspace);
                } catch(OperationCanceledException){status.Text="Compilación cancelada. BSP anterior conservado.";}
                catch(Exception ex){status.Text="Compilación fallida. BSP anterior conservado.";log.AppendText("\n"+ex.Message);}
                finally{compilationCancellation.Dispose();compilationCancellation=null;busy=false;form.IsEnabled=true;analyze.IsEnabled=true;export.IsEnabled=true;compile.IsEnabled=lastMap!=null;registerWad.IsEnabled=lastMap!=null;progress.IsIndeterminate=false;}
            };
            form.Children.Add(entities); form.Children.Add(enclosure); Row("Textura del recinto (modo WAD existente)", enclosureTexture, wadSettings);
            var info = new TextBlock { Text = "Muros y losas con contornos rectos poligonales, vanos y uniones diagonales; columnas rectangulares. La geometría no soportada se omite y se informa. No incluye vínculos ni escaleras.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) }; form.Children.Add(info);
            analyze.Click += (s, e) => Run(false); export.Click += (s, e) => Run(true);
            cancel.Click += (s, e) => { if (busy) { cancelled = true; compilationCancellation?.Cancel(); status.Text = "Cancelando…"; } else Close(); };
            profile.SelectionChanged+=(sender,args)=>{
                bool full=profile.SelectedIndex==1;modelCategories.Visibility=full?Visibility.Visible:Visibility.Collapsed;
                foreach(var check in categories.Values)check.Visibility=full?Visibility.Collapsed:Visibility.Visible;foreach(var control in architectureTextureControls)control.Visibility=full?Visibility.Collapsed:Visibility.Visible;
                if(full){textureMode.SelectedIndex=1;DiscoverCategories();}
            };
            includeLinks.Checked+=(sender,args)=>{if(profile.SelectedIndex==1)DiscoverCategories();};includeLinks.Unchecked+=(sender,args)=>{if(profile.SelectedIndex==1)DiscoverCategories();};
            Closing += (s, e) => { if (busy) { cancelled = true; compilationCancellation?.Cancel(); e.Cancel = true; } };
        }
        private void DiscoverCategories()
        {
            if(busy)return;busy=true;cancelled=false;form.IsEnabled=false;analyze.IsEnabled=false;export.IsEnabled=false;compile.IsEnabled=false;registerWad.IsEnabled=false;
            try
            {
                var previous=ReadModelRules().ToDictionary(r=>r.Key);modelRules=RevitModelReader.Discover(document,includeLinks.IsChecked==true,Update);
                modelChecks.Clear();modelCollisions.Clear();modelTextures.Clear();categoryRows.Children.Clear();
                foreach(var rule in modelRules)
                {
                    if(previous.TryGetValue(rule.Key,out var old)){rule.Enabled=old.Enabled;rule.Collision=old.Collision;rule.Texture=old.Texture;}
                    var row=new DockPanel{Margin=new Thickness(0,3,0,3),Tag=rule.Name};var check=new CheckBox{Content=rule.Name+" ("+rule.Count+")",IsChecked=rule.Enabled,MinWidth=230,VerticalAlignment=VerticalAlignment.Center};
                    var collision=new ComboBox{Width=130,Margin=new Thickness(8,0,0,0)};collision.Items.Add("Sólido");collision.Items.Add("Sin colisión");collision.SelectedIndex=rule.Collision?0:1;
                    var texture=new ComboBox{Width=160,IsEnabled=textureMode.SelectedIndex==0,Margin=new Thickness(8,0,0,0),ItemsSource=loadedTextures,SelectedItem=rule.Texture,ToolTip="Textura de esta categoría en modo WAD existente."};
                    modelChecks.Add(rule.Key,check);modelCollisions.Add(rule.Key,collision);modelTextures.Add(rule.Key,texture);
                    DockPanel.SetDock(texture,Dock.Right);row.Children.Add(texture);DockPanel.SetDock(collision,Dock.Right);row.Children.Add(collision);row.Children.Add(check);categoryRows.Children.Add(row);
                }
                FilterCategories();status.Text=modelRules.Count+" categorías físicas detectadas. Las puertas usan su opción independiente.";
            }
            catch(OperationCanceledException){status.Text="Descubrimiento cancelado.";}
            catch(Exception ex){status.Text="No se pudieron descubrir categorías: "+ex.Message;}
            finally{busy=false;cancelled=false;form.IsEnabled=true;analyze.IsEnabled=true;export.IsEnabled=true;compile.IsEnabled=lastMap!=null;registerWad.IsEnabled=lastMap!=null;}
        }
        private void FilterCategories()
        {foreach(FrameworkElement row in categoryRows.Children)row.Visibility=(row.Tag?.ToString()??"").IndexOf(categorySearch.Text,StringComparison.OrdinalIgnoreCase)>=0?Visibility.Visible:Visibility.Collapsed;}
        private IReadOnlyList<CategorySelection> ReadModelRules()
        {
            return modelRules.Select(r=>new CategorySelection{Key=r.Key,Name=r.Name,Count=r.Count,Enabled=modelChecks.TryGetValue(r.Key,out var check)?check.IsChecked==true:r.Enabled,Collision=modelCollisions.TryGetValue(r.Key,out var collision)?collision.SelectedIndex==0:r.Collision,Texture=modelTextures.TryGetValue(r.Key,out var texture)?texture.SelectedItem as string??"":r.Texture}).ToList();
        }
        private void RegisterWad()
        {
            if(busy||lastMap==null)return;
            try
            {
                string? config=JackWadRegistration.DetectConfig();
                if(config==null){var dialog=new OpenFileDialog{Title="Elegir VDKGameCfg.ini de J.A.C.K.",Filter="Configuración J.A.C.K. (VDKGameCfg.ini)|VDKGameCfg.ini",CheckFileExists=true};if(dialog.ShowDialog(this)!=true)return;config=dialog.FileName;}
                var profiles=JackWadRegistration.Profiles(config);if(profiles.Count==0)throw new ArgumentException("No hay perfiles Half-Life habilitados en "+config);
                var picker=new ComboBox{ItemsSource=profiles,SelectedIndex=0,Margin=new Thickness(12)};
                var accept=new Button{Content="Registrar",Margin=new Thickness(12),Padding=new Thickness(12,6,12,6)};IconAssets.Attach(accept,PluginIcon.Register,"Registrar los WAD en el perfil seleccionado.");
                var panel=new StackPanel();panel.Children.Add(new TextBlock{Text="Perfil Half-Life de J.A.C.K. (el editor debe estar cerrado)",Margin=new Thickness(12),TextWrapping=TextWrapping.Wrap});panel.Children.Add(picker);panel.Children.Add(accept);
                var window=new Window{Title="Registrar texturas en J.A.C.K.",Owner=this,Content=panel,Width=440,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner};accept.Click+=(sender,args)=>window.DialogResult=true;if(window.ShowDialog()!=true)return;
                var profile=(JackProfile)picker.SelectedItem;var plan=JackWadRegistration.Prepare(lastMap,config,profile.Section);
                var result=JackWadRegistration.Apply(plan,Path.GetDirectoryName(lastMap)!);status.Text=result.Message;log.AppendText("\n"+result.Message+"\nWAD: "+string.Join("\n",plan.WadPaths));
            }
            catch(Exception ex){status.Text="No se registraron las texturas.";log.AppendText("\n"+ex.Message);}
        }
        private void Row(string label, UIElement field, StackPanel? parent = null)
        { var panel = parent ?? form; panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) }); panel.Children.Add(field); }
        private void SetOriginEnabled(bool enabled) { foreach (var box in new[] { ox, oy, oz }) box.IsEnabled = enabled; }
        private void LoadWad()
        {
            var dialog = new OpenFileDialog { Filter = "GoldSrc WAD (*.wad)|*.wad", CheckFileExists = true }; if (dialog.ShowDialog(this) != true) return;
            try
            {
                var catalog = WadCatalog.Read(dialog.FileName); var names = catalog.Textures.Where(n => { try { TextureMapper.ValidateSolidTexture(n); return true; } catch (ArgumentException) { return false; } }).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                if (names.Count == 0) throw new ArgumentException("El WAD no contiene texturas sólidas normales.");
                wad.Text = dialog.FileName;
                loadedTextures = names;
                fillTextures.IsEnabled = true;
                foreach (var combo in textures.Values.Concat(new[] { enclosureTexture, doorTexture }).Concat(modelTextures.Values)) { combo.ItemsSource = names; combo.SelectedIndex = -1; }
                status.Text = "WAD cargado. Seleccione las texturas o pulse «Rellenar con texturas recomendadas».";
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "WAD inválido", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        private void FillTextures()
        {
            var dialog = new TextureFillDialog { Owner = this };
            dialog.ShowDialog();
            if (dialog.Mode == TextureFillMode.Cancel) return;
            var current = textures.ToDictionary(p => p.Key, p => p.Value.SelectedItem as string);
            var result = TextureRecommendations.Fill(loadedTextures, current, enclosureTexture.SelectedItem as string, dialog.Mode);
            foreach (var pair in result.Categories) textures[pair.Key].SelectedItem = pair.Value;
            enclosureTexture.SelectedItem = result.Enclosure;
            if(doorTexture.SelectedItem==null||dialog.Mode==TextureFillMode.ReplaceAll)doorTexture.SelectedItem=loadedTextures.FirstOrDefault(n=>n.IndexOf("door",StringComparison.OrdinalIgnoreCase)>=0)??result.Enclosure;
            foreach(var combo in modelTextures.Values)if(combo.SelectedItem==null||dialog.Mode==TextureFillMode.ReplaceAll)combo.SelectedItem=loadedTextures.FirstOrDefault(n=>n.IndexOf("METAL",StringComparison.OrdinalIgnoreCase)>=0)??result.Enclosure;
            status.Text = "Texturas recomendadas: " + result.Changed + " asignaciones actualizadas.";
            if (result.GenericFallbackTargets.Count > 0)
                status.Text += " Alternativa genérica por nombre: " + string.Join(", ", result.GenericFallbackTargets.Select(target =>
                    target == "Recinto" ? target : categories[(BimCategory)Enum.Parse(typeof(BimCategory), target)].Content.ToString())) + ". Puede cambiarla manualmente.";
        }
        private static double Number(TextBox field)
        {
            if (!double.TryParse(field.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) && !double.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new ArgumentException("Número inválido: " + field.Text);
            if (!Vec3.Finite(value)) throw new ArgumentException("Se requieren números finitos."); return value;
        }
        private ExportOptions ReadOptions(bool forExport)
        {
            var options = new ExportOptions
            {
                TextureSource = textureMode.SelectedIndex == 1 ? TextureSourceMode.RevitMaterials : TextureSourceMode.ExistingWad,
                UnitsPerFoot = Number(scale),
                ToleranceFeet = Number(tolerance),
                CenterOrigin = center.IsChecked == true,
                OriginFeet = new Vec3(Number(ox), Number(oy), Number(oz)),
                WadPath = wad.Text,
                GenerateEntities = entities.IsChecked == true,
                TestEnclosure = enclosure.IsChecked == true
            };
            options.Doors.Enabled=exportDoors.IsChecked==true;options.Doors.Texture=doorTexture.SelectedItem as string??"";
            options.Validate(); if (!forExport || options.TextureSource == TextureSourceMode.RevitMaterials) return options;
            if (string.IsNullOrWhiteSpace(options.WadPath)) throw new ArgumentException("Cargue un WAD y asigne sus texturas.");
            foreach(var rule in ReadModelRules().Where(r=>r.Enabled))if(profile.SelectedIndex==1)options.CategoryTextures.Add(rule.Key,string.IsNullOrEmpty(rule.Texture)?throw new ArgumentException("Seleccione textura para "+rule.Name):rule.Texture);
            foreach (var c in (profile.SelectedIndex==1?new List<BimCategory>():SelectedCategories())) options.Textures.Add(c, textures[c].SelectedItem as string ?? throw new ArgumentException("Seleccione textura para " + c));
            if (options.TestEnclosure) options.EnclosureTexture = enclosureTexture.SelectedItem as string ?? throw new ArgumentException("Seleccione la textura del recinto.");
            return options;
        }
        private List<BimCategory> SelectedCategories() => categories.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
        private void Update(string message)
        {
            if (cancelled) throw new OperationCanceledException();
            if (refresh.ElapsedMilliseconds < 100) return;
            status.Text = message; Dispatcher.Invoke(() => { }, DispatcherPriority.Background); refresh.Restart();
            if (cancelled) throw new OperationCanceledException();
        }
        private void Run(bool publish)
        {
            if (busy) return;
            try
            {
                var options = ReadOptions(publish); var selectedCategories = SelectedCategories(); if ((profile.SelectedIndex==1?!ReadModelRules().Any(r=>r.Enabled):selectedCategories.Count == 0) && !options.Doors.Enabled) throw new ArgumentException("Seleccione al menos una categoría.");
                string target = publish ? Path.GetFullPath(output.Text) : "";
                if (publish)
                {
                    if (!string.Equals(Path.GetExtension(target), ".map", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(Path.GetDirectoryName(target))) throw new ArgumentException("Seleccione una ruta .map en una carpeta existente.");
                    var reportPath = Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target) + "_export_report.json");
                    if ((File.Exists(target) || File.Exists(reportPath) || (options.TextureSource == TextureSourceMode.RevitMaterials && File.Exists(Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target) + "_materiales.wad")))) && MessageBox.Show(this, "Ya existen archivos de esta exportación. ¿Reemplazarlos?", "Revit2GoldSrc", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                }
                bool selected = scope.SelectedIndex == 1;
                busy = true; cancelled = false; compile.IsEnabled=false; registerWad.IsEnabled=false; form.IsEnabled = false; analyze.IsEnabled = false; export.IsEnabled = false; progress.IsIndeterminate = true; log.Clear();
                Update("Leyendo geometría real de Revit…");
                bool fullModel=profile.SelectedIndex==1;
                var data = fullModel?RevitModelReader.Read(document,selected,includeLinks.IsChecked==true,ReadModelRules(),quality.SelectedIndex==1?GeometryQuality.High:GeometryQuality.Balanced,options.ToleranceFeet,options.TextureSource==TextureSourceMode.RevitMaterials,Update):RevitGeometryExtractor.Read(document, selected, selectedCategories, options.ToleranceFeet, Update, options.TextureSource == TextureSourceMode.RevitMaterials);
                data.Report.TextureSource = options.TextureSource.ToString();
                DoorExtraction? doorData=null;
                if(options.Doors.Enabled)
                {
                    var selectedDoorIds=new System.Collections.Generic.HashSet<long>(document.Selection.GetElementIds().Select(document.Document.GetElement).Where(e=>e?.Category?.Id.Value==(long)Autodesk.Revit.DB.BuiltInCategory.OST_Doors).Select(e=>e!.Id.Value));
                    if(selected&&!fullModel){data.Report.ElementsFound-=selectedDoorIds.Count;data.Report.Diagnostics.RemoveAll(d=>d.ElementId.HasValue&&selectedDoorIds.Contains(d.ElementId.Value));}
                    doorData=fullModel?RevitModelReader.ReadDoors(document,selected,includeLinks.IsChecked==true,options.ToleranceFeet,options.TextureSource==TextureSourceMode.RevitMaterials,data.Report,Update,data.Elements):RevitDoorReader.Read(document,selected,options.ToleranceFeet,options.TextureSource==TextureSourceMode.RevitMaterials,data.Report,Update,data.Elements);
                }
                log.Text = string.Join("\n", data.Report.Diagnostics.Select(d => "Elemento " + d.ElementId + ": " + d.Message));
                if (publish)
                {
                    if (data.Elements.Count == 0 && (doorData?.Doors.Count ?? 0)==0)
                    {
                        data.Report.UnitsPerFoot = options.UnitsPerFoot;
                        data.Report.ToleranceFeet = options.ToleranceFeet;
                        data.Report.Diagnostics.Add(new Diagnostic(null, "Todos los elementos fueron omitidos. Se publicó únicamente el diagnóstico; no se creó ni reemplazó el mapa.") { Code = "diagnostics_only" });
                        if (cancelled) throw new OperationCanceledException();
                        Exporter.PublishDiagnostics(data.Report, target);
                        status.Text = "Sin geometría compatible. Diagnóstico guardado: " + Path.GetFileNameWithoutExtension(target) + "_export_report.json";
                        return;
                    }
                    var result = Exporter.Prepare(data.Elements, options, data.Report, Update,doorData?.Doors,doorData?.Unknown);
                    Update("Publicando mapa, texturas y reporte…"); if (cancelled) throw new OperationCanceledException(); Exporter.Publish(result, target);
                    lastMap=target;status.Text = "Exportación terminada: " + target;
                }
                else { if(doorData!=null)data.Report.ElementsFound+=doorData.Doors.Count; status.Text = "Análisis terminado: " + data.Elements.Sum(e => e.Solids.Count+e.Prisms.Count+e.ConvexSolids.Count) + " prismas comprobados; "+(doorData?.Doors.Count??0)+" puertas leídas. No se ha escrito ningún archivo."; }
                var report = data.Report;
                log.Text = "Encontrados: " + report.ElementsFound + " | Convertidos: " + report.ElementsConverted + " | Omitidos: " + report.ElementsSkipped +
                 (publish ? "\nBrushes: " + report.Brushes + " | Caras: " + report.Faces + " | Entidades: " + report.Entities : "") +
                 "\nTiempo: " + report.ElapsedSeconds.ToString("F2", CultureInfo.CurrentCulture) + " s\n" +
                 string.Join("\n", report.Diagnostics.Select(d => (d.ElementId.HasValue ? "Elemento " + d.ElementId + ": " : "") + d.Message));
            }
            catch (OperationCanceledException) { status.Text = "Operación cancelada. No se publicó una nueva exportación."; }
            catch (Exception ex) { status.Text = "No se pudo completar la operación."; log.AppendText("\nERROR: " + ex.Message); }
            finally { busy = false; progress.IsIndeterminate = false; form.IsEnabled = true; analyze.IsEnabled = true; export.IsEnabled = true; compile.IsEnabled=lastMap!=null;registerWad.IsEnabled=lastMap!=null; }
        }
    }
}
