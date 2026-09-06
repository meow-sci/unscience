using System;
using System.Collections.Generic;
namespace Brutal.Numerics
{
    public readonly record struct double3(double X, double Y, double Z)
    {
        public double3(double v) : this(v, v, v) { }
        public static double3 operator +(double3 a, double3 b) => new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static double3 operator -(double3 a, double3 b) => new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static double3 operator *(double3 a, double b) => new(a.X*b,a.Y*b,a.Z*b);
    }
    public readonly record struct float3(float X, float Y, float Z)
    { public float3(float v) : this(v,v,v) { } }
}
namespace KSA
{
    using Brutal.Numerics;
    public class Part
    {
        public double3 Scale = new(1);
        public double3 PositionParentAsmb;
        public List<Part> SubParts = new();
        public int Invalidations, Refreshes, Bounds;
        public void ResetCachedPosMatrixValues() => Invalidations++;
        public void RefreshScale() { Refreshes++; foreach(var p in SubParts) p.RefreshScale(); }
        public void UpdateBounds() => Bounds++;
    }
    public class PartTree
    {
        public List<Part> Parts = new();
        public int Refreshes;
        public void RecomputeAllDerivedData() => Refreshes++;
    }
    public class Vehicle
    {
        public bool IsDisposed;
        public double3 CenterOfMassAsmb;
        public PartTree Parts = new();
        public int Refreshes;
        public void UpdateAfterPartTreeModification() => Refreshes++;
    }
    public class KittenEva : Vehicle { public KittenRenderable Renderable = new(); }
    public class KittenRenderable { public CharacterAvatar Avatar = new(); public float3 Correction; }
    public class CharacterAvatar { public CharacterCore Core; }
    public struct CharacterCore { public float Scale; }
}
namespace MeowSci.KsaAbstractions
{
    public static class ReflectionHelpers
    {
        public static object GetFieldValue(KSA.KittenRenderable r, string field) => r.Avatar;
    }
}
namespace MeowSci.GarrysTorchLib
{
    public static class WeldScale
    {
        public static bool IsValid(Brutal.Numerics.float3 f) =>
            float.IsFinite(f.X) && float.IsFinite(f.Y) && float.IsFinite(f.Z) &&
            f.X >= .05f && f.Y >= .05f && f.Z >= .05f && f.X <= 20 && f.Y <= 20 && f.Z <= 20;
    }
    public static class KittenScalePatches
    {
        public static void SetScale(KSA.KittenRenderable r, Brutal.Numerics.float3 f) => r.Correction=f;
    }
}
