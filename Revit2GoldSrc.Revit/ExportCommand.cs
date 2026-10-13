using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace Revit2GoldSrc.Revit
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class ExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ui = commandData.Application;
            if (ui.ActiveUIDocument == null) { TaskDialog.Show("Revit2GoldSrc", "Abra un proyecto Revit primero."); return Result.Cancelled; }
            if (ui.Application.VersionNumber != App.BuiltFor) { message = "Versión de Revit incorrecta."; return Result.Failed; }
            try
            {
                var window = new ExportWindow(ui.ActiveUIDocument);
                new System.Windows.Interop.WindowInteropHelper(window).Owner = ui.MainWindowHandle;
                window.ShowDialog(); return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
