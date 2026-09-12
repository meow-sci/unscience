using System;
using System.Linq;

namespace MeowSci.RockyMcRockFaceLib;

/// <summary>
/// The user's desired ring overrides for one celestial body. Empty strings / NaN mean
/// "keep the game default" for that slot.
/// </summary>
public sealed class RingSelection
{
    /// <summary>The game rejects ring definitions with more than 5 LODs.</summary>
    public const int MaxLods = 5;

    /// <summary>Per-LOD replacement mesh id; "" keeps the game's mesh for that LOD.</summary>
    public string[] LodMeshIds { get; } = { "", "", "", "", "" };

    public string DiffuseId = "";
    public string NormalId = "";
    public string PbrId = "";
    public string BandTextureId = "";

    /// <summary>When false the four field parameters below are ignored and game defaults apply.</summary>
    public bool OverrideFieldSettings;
    public double SizeM = 10.0;
    public double DensityPerKm3 = 3125.0;
    public double RenderDistanceKm = 20.0;
    public double ThicknessKm = 1.0;

    public bool HasAnyOverride =>
        OverrideFieldSettings
        || LodMeshIds.Any(id => id.Length > 0)
        || DiffuseId.Length > 0 || NormalId.Length > 0 || PbrId.Length > 0
        || BandTextureId.Length > 0;

    public RingSelection Clone()
    {
        var copy = new RingSelection { DiffuseId = DiffuseId, NormalId = NormalId, PbrId = PbrId,
            BandTextureId = BandTextureId, OverrideFieldSettings = OverrideFieldSettings, SizeM = SizeM,
            DensityPerKm3 = DensityPerKm3, RenderDistanceKm = RenderDistanceKm, ThicknessKm = ThicknessKm };
        Array.Copy(LodMeshIds, copy.LodMeshIds, MaxLods);
        return copy;
    }

    public void Clear()
    {
        Array.Fill(LodMeshIds, "");
        DiffuseId = NormalId = PbrId = BandTextureId = "";
        OverrideFieldSettings = false;
    }
}
