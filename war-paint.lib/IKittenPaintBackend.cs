using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using MeowSci.WarPaintLib.Materials;

namespace MeowSci.WarPaintLib;

/// <summary>Native material operations, separated from paint ownership and scene replay.</summary>
public interface IKittenPaintBackend
{
    bool Initialize();
    IReadOnlyList<KittenMaterialBinding>? CaptureBindings(KittenEva kitten);
    KittenMaterialSet? CreateMaterials(IReadOnlyList<KittenMaterialBinding> bindings,
        string characterId, float4 tint, KittenMaterialSet? reusable);
    void Bind(IReadOnlyList<KittenMaterialBinding> bindings, KittenMaterialSet materials);
    void Restore(IReadOnlyList<KittenMaterialBinding> bindings, KittenMaterialSet materials);
    bool SetTint(KittenMaterialSet materials, float4 tint);
    bool SetColor(MaterialEntry material, float4 color);
    bool CanReuse(KittenMaterialSet materials);
    void Release(KittenMaterialSet materials);
    /// <summary>Asset name of a source handle, used only to migrate legacy global color records.</summary>
    string? GetMaterialName(int handle) => null;
}

/// <summary>The original material bindings of one native renderable. Never serialized.</summary>
public sealed class KittenMaterialBinding(string source, int[] indices)
{
    public string Source { get; } = source;
    public int[] Indices { get; } = indices;
    public int[] OriginalIndices { get; } = (int[])indices.Clone();
}

/// <summary>A live target's privately owned material slots. Use Snapshot for durable settings.</summary>
public sealed class ActivePaint
{
    public KittenEva Kitten { get; }
    public string VehicleId => Kitten.Id;
    public string CharacterId { get; }
    public KittenMaterialSet MaterialSet { get; }
    internal IReadOnlyList<KittenMaterialBinding> Bindings { get; }

    internal ActivePaint(KittenEva kitten, string characterId, KittenMaterialSet materialSet,
        IReadOnlyList<KittenMaterialBinding> bindings)
    {
        Kitten = kitten; CharacterId = characterId; MaterialSet = materialSet; Bindings = bindings;
    }
}
