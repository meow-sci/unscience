using System;
using System.Linq;
using System.Numerics;
using BepuPhysics.Collidables;
using Brutal.Numerics;
using KSA;
using MeowSci.DohLib.Spawning;

namespace MeowSci.DohLib.Placement;

internal readonly record struct SurfaceCapsule(Capsule Shape, double3 CentreAsmb, doubleQuat RotationAsmb)
{
    public static SurfaceCapsule Preview()
    {
        var template = KittenSpawner.FindPartTemplate("KittenBackPackPart")
            ?? throw new InvalidOperationException("Kitten backpack template is unavailable.");
        var colliders = template.Components.OfType<ColliderModule.Template>().SelectMany(t => t.Colliders).ToArray();
        if (colliders.Length != 1 || colliders[0] is not CapsuleColliderTemplate capsule)
            throw new InvalidOperationException("Surface spawning requires one kitten capsule collider.");
        var shape = ConstraintSim.ReadOnlyShapes.GetShape<Capsule>(capsule.GetShape().Index);
        CapsulePlacementMath.Validate(shape);
        return new(shape, capsule.ComputeAuthoredLocationPartAsmb(), QuaternionEx.CreateFromXyzRadians(capsule.Collider2Asmb));
    }

    public static SurfaceCapsule FromKitten(KittenEva kitten)
    {
        var colliders = kitten.Parts.Modules.Get<ColliderModule>();
        if (colliders.Length != 1 || !colliders[0].Handle.Exists || colliders[0].Handle.Type != Capsule.TypeId)
            throw new InvalidOperationException("The kitten must have exactly one physical capsule collider.");
        var module = colliders[0];
        var shape = ConstraintSim.ReadOnlyShapes.GetShape<Capsule>(module.Handle.Index);
        CapsulePlacementMath.Validate(shape);
        return new(shape, module.PositionVehicleAsmb, module.Collider2VehicleAsmb);
    }

    public static Vector3 Small(double3 value) => new((float)value.X, (float)value.Y, (float)value.Z);
    public static double3 Double(Vector3 value) => new(value.X, value.Y, value.Z);
    public static Quaternion Small(doubleQuat value)
    {
        var rotation = new Quaternion((float)value.X, (float)value.Y, (float)value.Z, (float)value.W);
        if (!float.IsFinite(rotation.LengthSquared()) || rotation.LengthSquared() < 1e-8f)
            throw new InvalidOperationException("A surface has an invalid orientation.");
        return Quaternion.Normalize(rotation);
    }
    public static doubleQuat Double(Quaternion value) => new(value.X, value.Y, value.Z, value.W);
    public static bool Finite(double3 value) => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
}
