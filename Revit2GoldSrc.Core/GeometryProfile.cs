namespace Revit2GoldSrc.Core
{
    public sealed class GeometryProfile
    {
        public static GeometryProfile GoldSrc { get; } = new GeometryProfile(128, 31, 4096);
        public static GeometryProfile Source { get; } = new GeometryProfile(128, 64, 4096);
        public int MaxFaces { get; }
        public int MaxFaceVertices { get; }
        public int MaxPieces { get; }
        private GeometryProfile(int faces, int vertices, int pieces) { MaxFaces = faces; MaxFaceVertices = vertices; MaxPieces = pieces; }
    }
}
