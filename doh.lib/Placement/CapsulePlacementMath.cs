using System;
using System.Numerics;
using BepuPhysics.Collidables;

namespace MeowSci.DohLib.Placement;

/// <summary>All positions here are small, local offsets; game frame conversions stay in doubles.</summary>
internal static class CapsulePlacementMath
{
    public const float Skin = .02f;

    public static Quaternion BodyOrientation(Vector3 normal, Vector3 heading)
    {
        normal = Unit(normal);
        var forward = heading - normal * Vector3.Dot(heading, normal);
        if (forward.LengthSquared() < 1e-6f)
        {
            var fallback = MathF.Abs(normal.Z) < .9f ? Vector3.UnitZ : Vector3.UnitY;
            forward = Vector3.Cross(fallback, normal);
        }
        forward = Unit(forward);
        // KSA kittens: +X forward, -Z up. Matrix rows are transformed local basis vectors.
        var down = -normal;
        var right = Unit(Vector3.Cross(down, forward));
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            forward.X, forward.Y, forward.Z, 0, right.X, right.Y, right.Z, 0,
            down.X, down.Y, down.Z, 0, 0, 0, 0, 1)));
    }

    public static Vector3 CentreOffset(Capsule capsule, Quaternion capsuleOrientation, Vector3 normal)
    {
        Validate(capsule);
        normal = Unit(normal);
        var axis = Vector3.Transform(Vector3.UnitY, capsuleOrientation);
        float sign = Vector3.Dot(axis, normal) < 0 ? -1 : 1;
        return axis * (sign * capsule.HalfLength) + normal * (capsule.Radius + Skin);
    }

    public static Vector3 ComOffset(Vector3 centreOffset, Vector3 colliderCentreAsmb,
        Vector3 comAsmb, Quaternion bodyOrientation) =>
        centreOffset - Vector3.Transform(colliderCentreAsmb - comAsmb, bodyOrientation);

    public static Vector3 PointVelocity(Vector3 linear, Vector3 angular, Vector3 offset) =>
        linear + Vector3.Cross(angular, offset);

    public static Vector3 Unit(Vector3 value)
    {
        float length = value.Length();
        if (!float.IsFinite(length) || length < 1e-6f) throw new ArgumentException("Invalid placement direction.");
        return value / length;
    }

    public static void Validate(Capsule capsule)
    {
        if (!float.IsFinite(capsule.Radius) || !float.IsFinite(capsule.HalfLength)
            || capsule.Radius <= 0 || capsule.HalfLength < 0 || capsule.Radius + capsule.HalfLength > 20)
            throw new ArgumentException("Unsupported kitten collider dimensions.");
    }
}
