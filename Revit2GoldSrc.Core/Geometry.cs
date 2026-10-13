using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace Revit2GoldSrc.Core
{
    public readonly struct Vec3
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double this[int a] => a == 0 ? X : a == 1 ? Y : Z;
        public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public bool IsFinite => Finite(X) && Finite(Y) && Finite(Z);
        public double Dot(Vec3 v) => X * v.X + Y * v.Y + Z * v.Z;
        public Vec3 Cross(Vec3 v) => new Vec3(Y * v.Z - Z * v.Y, Z * v.X - X * v.Z, X * v.Y - Y * v.X);
        public double Length => Math.Sqrt(Dot(this));
        public Vec3 Unit() { if (!IsFinite || Length < 1e-12) throw new ArgumentException("Vector degenerado."); return this / Length; }
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => a * -1;
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator /(Vec3 a, double s) => a * (1 / s);
        public override string ToString() => string.Join(" ", new[] { X, Y, Z }.Select(v => v.ToString("G17", CultureInfo.InvariantCulture)));
        public static Vec3 Zero => new Vec3(0, 0, 0);
        public static Vec3 XAxis => new Vec3(1, 0, 0); public static Vec3 YAxis => new Vec3(0, 1, 0); public static Vec3 ZAxis => new Vec3(0, 0, 1);
    }
    public sealed class Frame
    {
        public Vec3 Origin { get; }
        public Vec3 U { get; }
        public Vec3 V { get; }
        public Vec3 W { get; }
        public Frame(Vec3 origin, Vec3 u, Vec3 v, Vec3 w)
        {
            if (!origin.IsFinite) throw new ArgumentException("Origen no finito.");
            Origin = origin; U = u.Unit(); V = v.Unit(); W = w.Unit();
            if (Math.Abs(U.Dot(V)) > 1e-8 || Math.Abs(U.Dot(W)) > 1e-8 || Math.Abs(V.Dot(W)) > 1e-8) throw new ArgumentException("Marco no ortogonal.");
        }
        public Vec3 ToWorld(Vec3 p) => Origin + U * p.X + V * p.Y + W * p.Z;
        public Vec3 ToLocal(Vec3 p) { var d = p - Origin; return new Vec3(d.Dot(U), d.Dot(V), d.Dot(W)); }
        public static Frame Identity => new Frame(Vec3.Zero, Vec3.XAxis, Vec3.YAxis, Vec3.ZAxis);
    }
    public sealed class Box
    {
        public Frame Frame { get; }
        public Vec3 Min { get; }
        public Vec3 Max { get; }
        public Box(Frame frame, Vec3 min, Vec3 max) { Frame = frame; Min = min; Max = max; }
        public double Volume => (Max.X - Min.X) * (Max.Y - Min.Y) * (Max.Z - Min.Z);
        public void Validate(double t)
        {
            if (!Vec3.Finite(t) || t <= 0 || !Min.IsFinite || !Max.IsFinite || Max.X - Min.X <= t || Max.Y - Min.Y <= t || Max.Z - Min.Z <= t || !Vec3.Finite(Volume))
                throw new ArgumentException("Caja no finita o de espesor inferior a la tolerancia.");
        }
        public IEnumerable<Vec3> Vertices
        {
            get
            {
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                    yield return Frame.ToWorld(new Vec3(x == 0 ? Min.X : Max.X, y == 0 ? Min.Y : Max.Y, z == 0 ? Min.Z : Max.Z));
            }
        }
    }
    public static class Coordinates
    {
        public static double RevitFeetToGoldSrc(double feet, double unitsPerFoot = 12) => feet * unitsPerFoot;
        public static double MetersToGoldSrc(double meters, double unitsPerFoot = 12) => meters / 0.3048 * unitsPerFoot;
        public static Vec3 TransformCoordinates(Vec3 p, Vec3 origin, double scale = 12)
        {
            if (!p.IsFinite || !origin.IsFinite || !Vec3.Finite(scale) || scale <= 0) throw new ArgumentException("Coordenadas o escala inválidas.");
            var result = (p - origin) * scale; if (!result.IsFinite) throw new ArgumentException("Desbordamiento de coordenadas."); return result;
        }
        public static Vec3 NormalizeCoordinates(IEnumerable<Vec3> vertices)
        {
            var p = vertices.ToList(); if (p.Count == 0 || p.Any(v => !v.IsFinite)) throw new ArgumentException("Sin vértices finitos.");
            return new Vec3(p.Min(v => v.X) / 2 + p.Max(v => v.X) / 2, p.Min(v => v.Y) / 2 + p.Max(v => v.Y) / 2, p.Min(v => v.Z) / 2 + p.Max(v => v.Z) / 2);
        }
        public static Box Convert(Box b, Vec3 origin, double scale) => new Box(new Frame(TransformCoordinates(b.Frame.Origin, origin, scale), b.Frame.U, b.Frame.V, b.Frame.W), b.Min * scale, b.Max * scale);
    }
}

