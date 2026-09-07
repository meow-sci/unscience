using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace MeowSci.SphinxLib;

[StructLayout(LayoutKind.Sequential)]
internal struct StaticVertex { public Vector3 Position, Normal; public Vector2 Uv; }

/// <summary>Per-placement mapping, always applied to the original imported UVs.</summary>
internal readonly record struct TextureMapping(Vector2 Scale, Vector2 Offset)
{
    public static TextureMapping Identity => new(Vector2.One, Vector2.Zero);

    public void Validate()
    {
        if (!Finite(Scale) || !Finite(Offset) || Scale.X < .01f || Scale.Y < .01f ||
            Scale.X > 1000 || Scale.Y > 1000)
            throw new ArgumentException("Texture scale must be 0.01–1000 on both axes; UV offsets must be finite.");
    }

    public StaticVertex[] Apply(ReadOnlySpan<StaticVertex> original)
    {
        Validate();
        var vertices = original.ToArray();
        for (int i = 0; i < vertices.Length; i++)
        {
            var uv = vertices[i].Uv * Scale + Offset;
            if (!Finite(uv)) throw new ArgumentException("Texture mapping produces nonfinite UV coordinates.");
            vertices[i].Uv = uv;
        }
        return vertices;
    }

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
