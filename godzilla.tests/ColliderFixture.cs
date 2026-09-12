using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BepuPhysics.Collidables;
using Brutal.Numerics;

namespace KSA;

public static class TestMathExtensions
{
    public static double3 Transform(this double3 value, doubleQuat rotation) =>
        double3.Transform(value, double4x4.CreateFromQuaternion(rotation));
    public static doubleQuat Concatenate(this doubleQuat a, doubleQuat b) => doubleQuat.Concatenate(a, b);
}

public sealed class ModuleList
{
    public List<object> Items = new();
    public Span<T> Get<T>() => Items.OfType<T>().ToArray();
}

public readonly struct ScaleFactors(double scale)
{
    public readonly double Scale = scale;
    public ScaleFactors(double3 scale) : this(Math.Max(scale.X, Math.Max(scale.Y, scale.Z))) { }
}

public sealed class ColliderModule
{
    public Part Parent = null!;
    public double3 PositionPartAsmb;
    public double3 AuthoredOffset = new(1, 0, 0);
    public double AppliedScale = 1;
    public Sphere Shape = new(1);
    public bool NeedsColliderUpdate;
    public bool ThrowOnScale;
    public double3 PositionVehicleAsmb
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => PositionPartAsmb.Transform(Parent.Asmb2VehicleAsmb) + Parent.PositionVehicleAsmb;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetScale(in ScaleFactors scale)
    {
        AppliedScale = scale.Scale;
        Shape = new Sphere((float)scale.Scale);
        PositionPartAsmb = AuthoredOffset * scale.Scale;
        NeedsColliderUpdate = true;
        if (ThrowOnScale) throw new InvalidOperationException("test shape failure");
    }
}

public sealed class PhysicsStates { public VehicleProperties Props; }
public struct VehicleProperties
{
    public Box BoundingBoxAsmb;
    public float3 GeometricCenterAsmb;
    public float BoundingSphereRadiusBody;
    public float Mass, Inertia, Aero;
}
