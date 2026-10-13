using System;
using System.Reflection;
using Autodesk.Revit.UI;
namespace Revit2GoldSrc.Revit
{
    public sealed class App : IExternalApplication
    {
#if REVIT2024
        internal const string BuiltFor = "2024";
#elif REVIT2025
  internal const string BuiltFor="2025";
#elif REVIT2026
  internal const string BuiltFor="2026";
#elif REVIT2027
  internal const string BuiltFor="2027";
#else
#error Missing Revit version
#endif
        public Result OnStartup(UIControlledApplication application)
        {
            if (application.ControlledApplication.VersionNumber != BuiltFor)
            { TaskDialog.Show("Revit2GoldSrc", "Este complemento fue compilado para Revit " + BuiltFor + ". Instale el paquete del año correcto."); return Result.Failed; }
            const string tab = "Revit2GoldSrc";
            try { application.CreateRibbonTab(tab); } catch (Autodesk.Revit.Exceptions.ArgumentException) { }
            var panel = application.CreateRibbonPanel(tab, "GoldSrc");
            panel.AddItem(new PushButtonData("Revit2GoldSrc.Export", "Exportar mapa\nGoldSrc", Assembly.GetExecutingAssembly().Location, typeof(ExportCommand).FullName) { Image = IconAssets.Ribbon(PluginIcon.Export, 16), LargeImage = IconAssets.Ribbon(PluginIcon.Export, 32), ToolTip = "Exportar arquitectura y puertas a un mapa Half-Life GoldSrc." });
            panel.AddItem(new PushButtonData("Revit2GoldSrc.PrepareDoors", "Preparar parámetros\nde puertas", Assembly.GetExecutingAssembly().Location, typeof(PrepareDoorsCommand).FullName) { Image = IconAssets.Ribbon(PluginIcon.Prepare, 16), LargeImage = IconAssets.Ribbon(PluginIcon.Prepare, 32), ToolTip = "Crear parámetros compartidos opcionales para configurar puertas GoldSrc." });
            var source = application.CreateRibbonPanel(tab, "Source / L4D2");
            source.AddItem(new PushButtonData("Revit2Source.Export", "Exportar mapa\nLeft 4 Dead 2", Assembly.GetExecutingAssembly().Location, typeof(SourceExportCommand).FullName) { Image = IconAssets.Ribbon(PluginIcon.Export, 16), LargeImage = IconAssets.Ribbon(PluginIcon.Export, 32), ToolTip = "Exportar VMF y materiales Source; compilar con Left 4 Dead 2 Authoring Tools." });
            return Result.Succeeded;
        }
        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    }
}
