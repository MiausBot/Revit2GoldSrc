using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit2Source;

namespace Revit2GoldSrc.Revit
{
    internal sealed class SourceSession
    {
        public string Output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "revit_l4d2.vmf");
        public string GameRoot = Revit2Source.Compilation.SourceToolchain.Detect().GameRoot;
        public bool Selected = true, Center = true, AutoSurvivors = true, AutoLight = true;
        public string Scale = "12", Tolerance = "0.00001", OriginX = "0", OriginY = "0", OriginZ = "0";
        public int Quality;
        public HashSet<BuiltInCategory> Categories = new HashSet<BuiltInCategory>(SourceRevitReader.Categories);
        public SourceConfiguration Configuration = new SourceConfiguration();
        public string? LastVmf;
        public string Log = "";
        public string Status = "Listo. Exportación Source de solo lectura.";
        public string? Pick;
    }

    [Transaction(TransactionMode.ReadOnly)]
    public sealed class SourceExportCommand : IExternalCommand
    {
        private sealed class DoorsOnly : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is FamilyInstance && e.Category?.Id.Value == (long)BuiltInCategory.OST_Doors;
            public bool AllowReference(Reference reference, XYZ point) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null)
                return Result.Cancelled;
            if (ui.Document.Application.VersionNumber != App.BuiltFor)
            {
                message = "Versión Revit incorrecta.";
                return Result.Failed;
            }

            var session = new SourceSession();
            session.Configuration.Document = SourceRevitReader.DocumentKey(ui.Document);
            try
            {
                while (true)
                {
                    session.Pick = null;
                    var window = new SourceExportWindow(ui, session);
                    new System.Windows.Interop.WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
                    window.ShowDialog();
                    if (session.Pick == null)
                        break;
                    try
                    {
                        if (session.Pick == "Door")
                        {
                            var reference = ui.Selection.PickObject(ObjectType.Element, new DoorsOnly(), "Seleccione una puerta para configurar en Source");
                            long id = reference.ElementId.Value;
                            if (!session.Configuration.Doors.Any(d => d.ElementId == id && d.Document == session.Configuration.Document))
                                session.Configuration.Doors.Add(new SourceDoorConfig { ElementId = id, Document = session.Configuration.Document });
                            session.Status = "Puerta añadida: " + id + ". Edite bisagra, tipo y recorrido en la tabla.";
                        }
                        else
                        {
                            var point = ui.Selection.PickPoint("Seleccione un punto en el plano de trabajo; ajuste Z en la tabla si es necesario.");
                            session.Configuration.Placements.Add(new SourcePlacement { Kind = session.Pick, X = point.X, Y = point.Y, Z = point.Z, SurvivorOrder = Math.Min(4, session.Configuration.Placements.Count(p => p.Kind == "Survivor") + 1) });
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        session.Status = "Selección cancelada.";
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
