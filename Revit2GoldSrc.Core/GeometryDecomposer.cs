using System;
using System.Collections.Generic;
using System.Linq;
namespace Revit2GoldSrc.Core
{
    public sealed class OrthogonalFace
    {
        public IReadOnlyList<IReadOnlyList<Vec3>> Loops { get; }
        public OrthogonalFace(IReadOnlyList<IReadOnlyList<Vec3>> loops) { Loops = loops; }
    }
    public static class GeometryDecomposer
    {
        // Rectilinear boundary, including holes. Grid classification uses an exact ray in local X.
        public static IReadOnlyList<Box> Decompose(Frame frame, IReadOnlyList<OrthogonalFace> faces, double expectedVolume, double tolerance)
        {
            if (!Vec3.Finite(tolerance) || tolerance <= 0 || !Vec3.Finite(expectedVolume) || expectedVolume <= tolerance * tolerance * tolerance)
                throw new ArgumentException("Volumen o tolerancia inválidos.");
            var issues = GeometryBoundaryValidator.Inspect(frame, faces, tolerance);
            if (issues.Count > 0) throw new GeometryValidationException(issues);
            var vertices = faces.SelectMany(f => f.Loops).SelectMany(l => l).ToList();
            var xFaces = faces.Where(f => f.Loops.SelectMany(l => l).Max(p => p.X) - f.Loops.SelectMany(l => l).Min(p => p.X) <= tolerance).ToList();
            var grids = Enumerable.Range(0, 3).Select(a => Distinct(vertices.Select(p => p[a]), tolerance)).ToArray();
            int nx = grids[0].Count - 1, ny = grids[1].Count - 1, nz = grids[2].Count - 1;
            if ((long)nx * ny * nz > 250000) throw new ArgumentException("Sólido supera 250000 celdas de análisis; exporte un sector más simple.");
            var occupied = new bool[nx, ny, nz];
            for (int x = 0; x < nx; x++) for (int y = 0; y < ny; y++) for (int z = 0; z < nz; z++)
            {
                var p = new Vec3((grids[0][x] + grids[0][x + 1]) / 2, (grids[1][y] + grids[1][y + 1]) / 2, (grids[2][z] + grids[2][z + 1]) / 2);
                occupied[x, y, z] = xFaces.Count(f => f.Loops[0][0].X > p.X && InsideFace(f, p)) % 2 == 1;
            }
            var boxes = new List<Box>();
            for (int x = 0; x < nx; x++) for (int y = 0; y < ny; y++) for (int z = 0; z < nz; z++)
            {
                if (!occupied[x, y, z]) continue;
                int ex = x + 1, ey = y + 1, ez = z + 1;
                while (ex < nx && occupied[ex, y, z]) ex++;
                while (ey < ny && Enumerable.Range(x, ex - x).All(i => occupied[i, ey, z])) ey++;
                while (ez < nz && Enumerable.Range(x, ex - x).All(i => Enumerable.Range(y, ey - y).All(j => occupied[i, j, ez]))) ez++;
                for (int i = x; i < ex; i++) for (int j = y; j < ey; j++) for (int k = z; k < ez; k++) occupied[i, j, k] = false;
                var b = new Box(frame, new Vec3(grids[0][x], grids[1][y], grids[2][z]), new Vec3(grids[0][ex], grids[1][ey], grids[2][ez])); b.Validate(tolerance); boxes.Add(b);
            }
            double actual = boxes.Sum(b => b.Volume);
            double dx = grids[0].Last() - grids[0][0], dy = grids[1].Last() - grids[1][0], dz = grids[2].Last() - grids[2][0];
            if (boxes.Count == 0 || Math.Abs(actual - expectedVolume) > Math.Max(expectedVolume * 1e-7, 2 * (dx * dy + dx * dz + dy * dz) * tolerance))
                throw new ArgumentException("La descomposición no conserva el volumen real; elemento omitido.");
            return boxes;
        }
        private static List<double> Distinct(IEnumerable<double> values, double t)
        {
            var result = new List<double>(); foreach (double v in values.OrderBy(v => v)) if (result.Count == 0 || v - result[result.Count - 1] > t) result.Add(v);
            if (result.Count < 2) throw new ArgumentException("Sólido sin espesor."); return result;
        }
        private static bool InsideFace(OrthogonalFace face, Vec3 p)
        {
            bool inside = false; foreach (var loop in face.Loops) for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                var a = loop[i]; var b = loop[j]; if ((a.Z > p.Z) != (b.Z > p.Z) && p.Y < (b.Y - a.Y) * (p.Z - a.Z) / (b.Z - a.Z) + a.Y) inside = !inside;
            }
            return inside;
        }
    }
}

