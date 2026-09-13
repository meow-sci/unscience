using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Brutal.Numerics;

namespace KSA;

// Mirror the relevant KSA contract: Scale invalidates only this part, and children inherit it.
public sealed class Part
{
    public PartTemplate Template = new();
    private double3 _scale = new(1);
    private double3? _cachedTotal;
    public Part? PartParent;
    public List<Part> SubParts = new();
    public double3 PositionParentAsmb;
    public int Writes;
    public double3 Scale
    {
        get => _scale;
        set { _scale = value; Writes++; ResetCachedPosMatrixValues(); }
    }
    public double3 ScaleTotal => _cachedTotal ??= PartParent == null ? Scale :
        new double3(Scale.X * PartParent.ScaleTotal.X, Scale.Y * PartParent.ScaleTotal.Y,
            Scale.Z * PartParent.ScaleTotal.Z);
    public void ResetCachedPosMatrixValues() => _cachedTotal = null;
}

public sealed class PartTree { public List<Part> Parts = new(); }
public sealed class KittenEva : Vehicle { public KittenRenderable Renderable = new(); }
public sealed class CharacterAvatar { public CharacterCore Core = new() { Scale = 0.01f }; }
public struct CharacterCore { public float Scale; }
public sealed class KittenRenderable
{
    private CharacterAvatar? _characterAvatar = new();
    public CharacterAvatar? Avatar { get => _characterAvatar; set => _characterAvatar = value; }
    public float4x4 ReadMatrix() => ModelToBodyMatrix();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private float4x4 ModelToBodyMatrix() => float4x4.CreateScale(new float3(_characterAvatar!.Core.Scale));
}
