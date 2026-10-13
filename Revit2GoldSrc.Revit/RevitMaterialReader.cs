using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Revit2GoldSrc.Core;
using Color = Autodesk.Revit.DB.Color;
namespace Revit2GoldSrc.Revit
{
    internal sealed class RevitMaterialReader
    {
        private readonly Document document;
        private readonly string sourceDocument;
        private readonly Dictionary<long, BimMaterial> cache = new Dictionary<long, BimMaterial>();
        private readonly List<string> folders = new List<string>();
        public RevitMaterialReader(Document document,string sourceDocument="")
        {
            this.document = document;this.sourceDocument=sourceDocument;
            if (!string.IsNullOrEmpty(document.PathName))
            {
                try { var project = Path.GetDirectoryName(document.PathName); if (project != null) folders.Add(project); } catch (ArgumentException) { }
            }
            foreach (string location in document.Application.GetLibraryPaths().Values)
            { if (Directory.Exists(location)) folders.Add(location); else { var parent = Path.GetDirectoryName(location); if (parent != null) folders.Add(parent); } }
            string common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86), "Autodesk Shared", "Materials", "Textures");
            folders.Add(common);
            if (Directory.Exists(common)) foreach (string version in Directory.GetDirectories(common))
            { folders.Add(version); string mats = Path.Combine(version, "Mats"); if (Directory.Exists(mats)) folders.Add(mats); }
            string ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Autodesk Revit " + document.Application.VersionNumber, "Revit.ini");
            if (File.Exists(ini))
            {
                bool directories = false;
                foreach (string line in File.ReadLines(ini))
                {
                    string trimmed = line.Trim(); if (trimmed.StartsWith("[", StringComparison.Ordinal)) directories = string.Equals(trimmed, "[Directories]", StringComparison.OrdinalIgnoreCase);
                    if (!directories) continue;
                    int equals = trimmed.IndexOf('='); if (equals < 0) continue;
                    if (string.Equals(trimmed.Substring(0, equals).Trim(), "AdditionalRenderAppearancePaths", StringComparison.OrdinalIgnoreCase))
                        folders.AddRange(trimmed.Substring(equals + 1).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => Environment.ExpandEnvironmentVariables(s.Trim().Trim('"'))));
                }
            }
        }
        public BimMaterial Read(ElementId id)
        {
            if (cache.TryGetValue(id.Value, out var cached)) return cached;
            var result = new BimMaterial { SourceDocument=sourceDocument, Id = id.Value, Name = "Material " + id.Value };
            cache.Add(id.Value, result);
            if (!(document.GetElement(id) is Material material))
            { result.Substitutions.Add("Material no disponible: gris neutro."); return result; }
            result.Name = material.Name;
            SetColor(result, material.Color);
            try
            {
                if (!(document.GetElement(material.AppearanceAssetId) is AppearanceAssetElement appearance))
                { result.Substitutions.Add("Sin recurso de apariencia: se utilizó el color del material."); return result; }
                using (var asset = appearance.GetRenderingAsset())
                {
                    if (asset.Size == 0) { result.Substitutions.Add("Recurso de apariencia vacío: se utilizó el color del material."); return result; }
                    var diffuse = new[] { Generic.GenericDiffuse, "concrete_color", "masonrycmu_color", "hardwood_color", "ceramic_color", "metal_color", "stone_color", "opaque_albedo", "advancedopaque_albedo", "base_color" }
                        .Select(asset.FindByName).FirstOrDefault(p => p != null);
                    if (diffuse == null) { result.Substitutions.Add("Esquema de apariencia no soportado: se utilizó el color del material."); return result; }
                    if (diffuse is AssetPropertyDoubleArray4d color) SetColor(result, color.GetValueAsColor());
                    var connected = diffuse.GetAllConnectedProperties().OfType<Asset>().ToList();
                    if (connected.Count == 0) return result;
                    var bitmapAssets = connected.Where(a => a.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) is AssetPropertyString).ToList();
                    if (bitmapAssets.Count != 1 || connected.Count != 1)
                    { result.Substitutions.Add("Textura procedural o mezcla de recursos no soportada: se utilizó el color del material."); return result; }
                    var bitmap = bitmapAssets[0];
                    string original = ((AssetPropertyString)bitmap.FindByName(UnifiedBitmap.UnifiedbitmapBitmap)).Value;
                    result.ImagePath = MaterialImagePaths.Resolve(original, folders) ?? original;
                    if (!File.Exists(result.ImagePath)) { result.Substitutions.Add("Imagen ausente: " + original + ". Se utilizó el color del material."); return result; }
                    result.WidthFeet = Distance(bitmap, UnifiedBitmap.TextureRealWorldScaleX, 1, result, true);
                    result.HeightFeet = Distance(bitmap, UnifiedBitmap.TextureRealWorldScaleY, 1, result, true);
                    result.OffsetXFeet = Distance(bitmap, UnifiedBitmap.TextureRealWorldOffsetX, 0, result, false);
                    result.OffsetYFeet = Distance(bitmap, UnifiedBitmap.TextureRealWorldOffsetY, 0, result, false);
                    result.RotationDegrees = (bitmap.FindByName(UnifiedBitmap.TextureWAngle) as AssetPropertyDouble)?.Value ?? 0;
                    if (asset.FindByName(Generic.GenericDiffuseImageFade) is AssetPropertyDouble fade) result.ImageWeight = Math.Max(0, Math.Min(1, fade.Value));
                    result.Validate(); result.Image = Decode(result.ImagePath);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException || ex is Autodesk.Revit.Exceptions.ApplicationException || ex is System.IO.FileFormatException)
            {
                result.Image = null; result.WidthFeet = 1; result.HeightFeet = 1; result.RotationDegrees = 0; result.OffsetXFeet = 0; result.OffsetYFeet = 0;
                result.Substitutions.Add("No se pudo leer la apariencia/imagen: " + ex.Message + ". Se utilizó el color del material.");
            }
            return result;
        }
        private static void SetColor(BimMaterial material, Color color)
        { if (color != null && color.IsValid) material.Color = new RgbColor(color.Red, color.Green, color.Blue); }
        private static double Distance(Asset asset, string property, double fallback, BimMaterial material, bool positive)
        {
            if (asset.FindByName(property) is AssetPropertyDistance distance)
            {
                double value = UnitUtils.ConvertToInternalUnits(distance.Value, distance.GetUnitTypeId());
                if (Vec3.Finite(value) && (!positive || value > 0)) return value;
            }
            if (positive) material.Substitutions.Add("Tamaño físico " + property + " no disponible: se utilizó 1 pie.");
            return fallback;
        }
        private static PixelImage Decode(string path)
        {
            using (var file = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource frame = decoder.Frames[0];
                if ((long)frame.PixelWidth * frame.PixelHeight > 64000000) throw new ArgumentException("Imagen de más de 64 millones de píxeles.");
                // Decode/scale in WPF; neutral pixel conversion and WAD quantization remain in independent libraries.
                double factor = Math.Min(1, 256.0 / Math.Max(frame.PixelWidth, frame.PixelHeight));
                if (factor < 1) frame = new TransformedBitmap(frame, new ScaleTransform(factor, factor));
                var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                var bgra = new byte[checked(converted.PixelWidth * converted.PixelHeight * 4)]; converted.CopyPixels(bgra, converted.PixelWidth * 4, 0);
                for (int i = 0; i < bgra.Length; i += 4) { byte b = bgra[i]; bgra[i] = bgra[i + 2]; bgra[i + 2] = b; }
                return new PixelImage(converted.PixelWidth, converted.PixelHeight, bgra);
            }
        }
    }
}
