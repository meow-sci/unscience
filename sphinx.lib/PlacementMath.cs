using System;
using System.Numerics;

namespace MeowSci.SphinxLib;

/// <summary>Game-independent glTF Y-up placement math.</summary>
internal static class PlacementMath
{
    public static Matrix4x4 GroundedLocal(Vector3 min, Vector3 max, Vector3 scale, Vector3 degrees, Vector3 offset)
    {
        if (!Finite(scale) || scale.X < .01f || scale.Y < .01f || scale.Z < .01f || scale.X > 1000 || scale.Y > 1000 || scale.Z > 1000)
            throw new ArgumentException("Scale axes must be between 0.01 and 1000.");
        if (!Finite(degrees) || !Finite(offset)) throw new ArgumentException("Offsets and rotation must be finite.");
        if (!Finite(min) || !Finite(max) || min.X > max.X || min.Y > max.Y || min.Z > max.Z)
            throw new ArgumentException("Model bounds must be finite and ordered.");
        var r = degrees * (MathF.PI / 180);
        var local = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationX(r.X)
            * Matrix4x4.CreateRotationY(r.Y) * Matrix4x4.CreateRotationZ(r.Z);
        var low = new Vector3(float.PositiveInfinity); var high = new Vector3(float.NegativeInfinity);
        for (int i = 0; i < 8; i++)
        {
            var p = Vector3.Transform(new((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z), local);
            low = Vector3.Min(low, p); high = Vector3.Max(high, p);
        }
        var centering = new Vector3(-(low.X * .5f + high.X * .5f), -low.Y, -(low.Z * .5f + high.Z * .5f));
        if (!Finite(low) || !Finite(high) || !Finite(centering + offset))
            throw new ArgumentException("The model transform exceeds supported coordinates.");
        return local * Matrix4x4.CreateTranslation(centering + offset);
    }
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}
