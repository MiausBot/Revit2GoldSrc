using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
namespace Revit2GoldSrc.Core
{
    public enum TextureSourceMode { ExistingWad, RevitMaterials }
    public readonly struct RgbColor
    {
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }
        public RgbColor(byte r, byte g, byte b) { R = r; G = g; B = b; }
        public static RgbColor Neutral => new RgbColor(128, 128, 128);
    }
    public sealed class PixelImage
    {
        public int Width { get; }
        public int Height { get; }
        public byte[] Rgba { get; }
        public PixelImage(int width, int height, byte[] rgba)
        {
            if (width <= 0 || height <= 0 || (long)width * height > 64000000 || rgba.Length != (long)width * height * 4) throw new ArgumentException("Imagen RGBA inválida o demasiado grande.");
            Width = width; Height = height; Rgba = rgba;
        }
        public static PixelImage Solid(RgbColor color)
        {
            var data = new byte[16 * 16 * 4]; for (int i = 0; i < data.Length; i += 4) { data[i] = color.R; data[i + 1] = color.G; data[i + 2] = color.B; data[i + 3] = 255; }
            return new PixelImage(16, 16, data);
        }
    }
    public sealed class BimMaterial
    {
        public long Id { get; set; }
        public string SourceDocument {get;set;}="";
        public string SourceKey=>SourceDocument+"|"+Id;
        public string Name { get; set; } = "";
        public RgbColor Color { get; set; } = RgbColor.Neutral;
        public PixelImage? Image { get; set; }
        public double ImageWeight { get; set; } = 1;
        public string? ImagePath { get; set; }
        public double WidthFeet { get; set; } = 1;
        public double HeightFeet { get; set; } = 1;
        public double RotationDegrees { get; set; }
        public double OffsetXFeet { get; set; }
        public double OffsetYFeet { get; set; }
        public List<string> Substitutions { get; } = new List<string>();
        public void Validate()
        {
            if (!Vec3.Finite(ImageWeight) || ImageWeight < 0 || ImageWeight > 1 || !Vec3.Finite(WidthFeet) || WidthFeet <= 0 || !Vec3.Finite(HeightFeet) || HeightFeet <= 0 || !Vec3.Finite(RotationDegrees) || !Vec3.Finite(OffsetXFeet) || !Vec3.Finite(OffsetYFeet))
                throw new ArgumentException("Tamaño físico o transformación de textura inválidos.");
        }
    }
    public sealed class MaterialSurface
    {
        public Vec3 Normal { get; }
        public double DistanceFeet { get; }
        public IReadOnlyList<IReadOnlyList<Vec3>> WorldLoopsFeet { get; }
        public BimMaterial Material { get; }
        public bool IsPainted { get; }
        public MaterialSurface(Vec3 outwardNormal, double distanceFeet, IReadOnlyList<IReadOnlyList<Vec3>> loops, BimMaterial material, bool painted = false)
        { Normal = outwardNormal.Unit(); DistanceFeet = distanceFeet; WorldLoopsFeet = loops; Material = material; IsPainted = painted; }
    }
    [DataContract]
    public sealed class MaterialExportInfo
    {
        [DataMember] public long MaterialId { get; set; }
        [DataMember] public string SourceDocument {get;set;}="";
        [DataMember] public string MaterialName { get; set; } = "";
        [DataMember] public string TextureName { get; set; } = "";
        [DataMember(EmitDefaultValue = false)] public string? ImagePath { get; set; }
        [DataMember] public bool UsesImage { get; set; }
        [DataMember] public double ImageWeight { get; set; }
        [DataMember] public double WidthFeet { get; set; }
        [DataMember] public double HeightFeet { get; set; }
        [DataMember] public double RotationDegrees { get; set; }
        [DataMember] public double OffsetXFeet { get; set; }
        [DataMember] public double OffsetYFeet { get; set; }
        [DataMember] public int FacesUsed { get; set; }
        [DataMember] public int PaintedFacesUsed { get; set; }
        [DataMember] public List<string> Substitutions { get; set; } = new List<string>();
    }
}
