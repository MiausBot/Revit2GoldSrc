using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace Revit2GoldSrc.Revit
{
    internal sealed class DoorParameter
    {
        public string Name { get; }
        public ForgeTypeId Type { get; }
        public object Default { get; }
        public Guid Guid { get; }
        public DoorParameter(string name, ForgeTypeId type, object value)
        { Name = "GoldSrc_" + name; Type = type; Default = value; using (var md5 = MD5.Create()) Guid = new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes("Revit2GoldSrc.Doors.v1:" + Name))); }
    }
    internal static class DoorParameters
    {
        public static readonly IReadOnlyList<DoorParameter> All = new[]{
            new DoorParameter("Animated",SpecTypeId.Boolean.YesNo,0),new DoorParameter("DoorType",SpecTypeId.String.Text,"Rotating"),
            new DoorParameter("OpenAngle",SpecTypeId.Angle,Math.PI/2),new DoorParameter("Speed",SpecTypeId.Number,100d),new DoorParameter("Wait",SpecTypeId.Number,3d),
            new DoorParameter("Activation",SpecTypeId.String.Text,"Use"),new DoorParameter("ReverseDirection",SpecTypeId.Boolean.YesNo,0),
            new DoorParameter("HingeSide",SpecTypeId.String.Text,"Auto"),new DoorParameter("HingeOffsetX",SpecTypeId.Length,0d),new DoorParameter("HingeOffsetY",SpecTypeId.Length,0d),
            new DoorParameter("SlideDirection",SpecTypeId.String.Text,"Auto"),new DoorParameter("SlideDistance",SpecTypeId.Length,0d),new DoorParameter("LeafThickness",SpecTypeId.Length,.04/.3048),
            new DoorParameter("DoorGroup",SpecTypeId.String.Text,""),new DoorParameter("LeafCount",SpecTypeId.String.Text,"Auto"),new DoorParameter("ActivatorType",SpecTypeId.String.Text,"Trigger"),
            new DoorParameter("ActivatorPlacement",SpecTypeId.String.Text,"Auto"),new DoorParameter("ActivatorOffsetX",SpecTypeId.Length,0d),new DoorParameter("ActivatorOffsetY",SpecTypeId.Length,0d),new DoorParameter("ActivatorOffsetZ",SpecTypeId.Length,0d)};
        public static Parameter? Get(Element door, string name) => door.get_Parameter(All.Single(p => p.Name == "GoldSrc_" + name).Guid);
    }
    [Transaction(TransactionMode.Manual)]
    public sealed class PrepareDoorsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application; var doc = ui.ActiveUIDocument?.Document; if (doc == null) { message = "Abra un proyecto."; return Result.Failed; }
            string previous = ui.Application.SharedParametersFilename;
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Revit2GoldSrc"); Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "Doors.sharedparameters.txt");
                if (!File.Exists(path)) File.WriteAllText(path, "# Revit2GoldSrc shared parameters\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n", Encoding.UTF8);
                ui.Application.SharedParametersFilename = path; var file = ui.Application.OpenSharedParameterFile() ?? throw new InvalidOperationException("No se pudo abrir el archivo de parámetros.");
                var group = file.Groups.get_Item("Revit2GoldSrc Doors") ?? file.Groups.Create("Revit2GoldSrc Doors");
                var created = new List<DoorParameter>(); using (var transaction = new Transaction(doc, "Preparar parámetros de puertas GoldSrc"))
                {
                    transaction.Start();
                    foreach (var item in DoorParameters.All)
                    {
                        var existing = SharedParameterElement.Lookup(doc, item.Guid);
                        if (existing != null)
                        {
                            var def = existing.GetDefinition(); if (def.Name != item.Name || def.GetDataType() != item.Type) throw new InvalidOperationException("Parámetro incompatible: " + item.Name);
                            var binding = doc.ParameterBindings.get_Item(def) as InstanceBinding;
                            if (binding == null || !binding.Categories.Contains(doc.Settings.Categories.get_Item(BuiltInCategory.OST_Doors))) throw new InvalidOperationException("Binding existente incompatible: " + item.Name);
                            continue;
                        }
                        if (new FilteredElementCollector(doc).OfClass(typeof(ParameterElement)).Cast<ParameterElement>().Any(p => p.Name == item.Name)) throw new InvalidOperationException("Ya existe " + item.Name + " con otro GUID. No se modificó.");
                        var definition = group.Definitions.get_Item(item.Name) as ExternalDefinition;
                        if (definition != null && (definition.GUID != item.Guid || definition.GetDataType() != item.Type)) throw new InvalidOperationException("Definición compartida incompatible: " + item.Name);
                        definition = definition ?? (ExternalDefinition)group.Definitions.Create(new ExternalDefinitionCreationOptions(item.Name, item.Type) { GUID = item.Guid, UserModifiable = true });
                        var cats = ui.Application.Create.NewCategorySet(); cats.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_Doors));
                        if (!doc.ParameterBindings.Insert(definition, ui.Application.Create.NewInstanceBinding(cats), GroupTypeId.Data)) throw new InvalidOperationException("Falló binding " + item.Name);
                        created.Add(item);
                    }
                    doc.Regenerate(); foreach (var door in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType()) foreach (var item in created)
                    { var p = door.get_Parameter(item.Guid); if (p == null || p.IsReadOnly) continue; if (item.Default is string text) p.Set(text); else if (item.Default is int number) p.Set(number); else p.Set((double)item.Default); }
                    transaction.Commit();
                }
                TaskDialog.Show("Revit2GoldSrc", "Parámetros nuevos preparados: " + created.Count + ". Las exportaciones siguen siendo de solo lectura."); return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
            finally { ui.Application.SharedParametersFilename = previous; }
        }
    }
}
