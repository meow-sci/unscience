using System;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.RockyMcRockFaceLib;

namespace MeowSci.BloominOnionLib;

/// <summary>
/// The stock assets a new ring falls back to when a definition leaves an id empty. Preferred
/// source is any ring the current system already defines (whatever the content ships); the
/// known Saturn ids are the fallback so rings work in systems without a ringed body.
/// </summary>
public sealed class StockRingAssets
{
    public const string BandTextureId = "SaturnRings";
    public const string ControlTextureId = "SaturnRingsControl";
    public const string DiffuseId = "LunaRockAtlasDiffuse";
    public const string NormalId = "LunaRockAtlasNormal";
    public const string PbrId = "LunaRockAtlasAoRoughMetal";
    public static readonly string[] LodMeshIds = { "RingRock_LOD0", "RingRock_LOD1", "RingRock_LOD2", "RingRock_LOD3", "RingRock_LOD4" };

    public TextureReference? Band { get; private set; }
    public TextureReference? Control { get; private set; }
    public TextureReference? Diffuse { get; private set; }
    public TextureReference? Normal { get; private set; }
    public TextureReference? Pbr { get; private set; }
    public MeshReference?[] LodMeshes { get; } = new MeshReference?[RingDefinition.MaxLods];

    /// <summary>True once every slot has a usable stock asset.</summary>
    public bool IsComplete =>
        Band != null && Control != null && Diffuse != null && Normal != null && Pbr != null && LodMesh(0) != null;

    /// <summary>Stock mesh for a LOD slot, falling back to the nearest lower slot that has one.</summary>
    public MeshReference? LodMesh(int index)
    {
        for (int i = Math.Min(index, LodMeshes.Length - 1); i >= 0; i--)
            if (LodMeshes[i] != null) return LodMeshes[i];
        return null;
    }

    public void Refresh(RingAssetCatalog catalog)
    {
        Band = Control = Diffuse = Pbr = null;
        Normal = null;
        Array.Fill(LodMeshes, null);

        foreach (var celestial in CelestialProvider.GetAllCelestials())
        {
            var rings = celestial.BodyTemplate?.RingsReference;
            if (rings?.RingObjects == null || rings.RingObjects.NumLods == 0) continue;
            if (rings.Texture?.Value is PaintedTextureReference) continue; // one of ours, not stock
            TakeFrom(rings);
            if (IsComplete) return;
        }

        if (Band == null && catalog.TryGetTexture(BandTextureId, out var band)) Band = band;
        if (Control == null && catalog.TryGetTexture(ControlTextureId, out var control)) Control = control;
        if (Diffuse == null && catalog.TryGetTexture(DiffuseId, out var diffuse)) Diffuse = diffuse;
        if (Normal == null && catalog.TryGetNormalTexture(NormalId, out var normal)) Normal = normal;
        if (Pbr == null && catalog.TryGetTexture(PbrId, out var pbr)) Pbr = pbr;
        for (int i = 0; i < LodMeshIds.Length; i++)
        {
            if (LodMeshes[i] == null && catalog.TryGetMesh(LodMeshIds[i], out var mesh) && HasDeviceMesh(mesh))
                LodMeshes[i] = mesh;
        }
    }

    private void TakeFrom(PlanetaryRingsReference rings)
    {
        var objects = rings.RingObjects;
        Band ??= rings.Texture?.Value;
        Control ??= rings.ControlTexture?.Value;
        Diffuse ??= objects.MaterialReference?.Value?.DiffuseReference?.Value;
        Normal ??= objects.MaterialReference?.Value?.NormalReference?.Value;
        Pbr ??= objects.MaterialReference?.Value?.PBRMap?.Value;
        for (int i = 0; i < objects.NumLods && i < LodMeshes.Length; i++)
        {
            var mesh = objects.Lods[i].MeshFileReference?.Value?.Mesh;
            if (LodMeshes[i] == null && mesh != null && HasDeviceMesh(mesh)) LodMeshes[i] = mesh;
        }
    }

    private static bool HasDeviceMesh(MeshReference mesh) =>
        mesh.DevicePrimitives is { Length: > 0 } && mesh.DevicePrimitives[0] != null;
}
