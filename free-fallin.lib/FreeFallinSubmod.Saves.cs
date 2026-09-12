using System;
using System.Collections.Generic;
using Brutal.Numerics;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.FreeFallinLib;

public sealed partial class FreeFallinSubmod : ISaveParticipantSource
{
    public sealed class CanopySave
    {
        public CanopyMaterialSettings? Applied { get; set; }
        public float4? EffectiveAlbedo { get; set; }
    }
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get
        {
            yield return new SaveParticipant<CanopySave>("free-fallin", () => new()
                { Applied = CanopyMaterialController.AppliedSettings, EffectiveAlbedo = CanopyMaterialController.CaptureEffectiveAlbedo() },
                FreeFallinPatches.RestoreStock, (saved, context) =>
                {
                    if (saved.Applied == null) return;
                    try { CanopyMaterialController.Apply(saved.Applied, saved.EffectiveAlbedo); }
                    catch (Exception ex) { context.Warn("Canopy appearance: " + ex.Message); }
                }, 20, ValidateSave);
        }
    }
    private static void ValidateSave(CanopySave saved)
    {
        if (saved.EffectiveAlbedo is { } color && (saved.Applied == null || !float.IsFinite(color.X)
            || !float.IsFinite(color.Y) || !float.IsFinite(color.Z) || !float.IsFinite(color.W)))
            throw new InvalidOperationException("Invalid saved effective canopy color.");
        if (saved.Applied is not { } s) return;
        if (!Enum.IsDefined(s.TextureMode) || s.TextureMode != CanopyTextureMode.Stock && string.IsNullOrWhiteSpace(s.TextureName)
            || s.Brightness is < 0 or > 4 || s.DecalScale is < .05f or > 1
            || s.AmbientOcclusion < 0 || s.AmbientOcclusion > (s.UseStockPbrMap ? 4 : 1)
            || s.Roughness < 0 || s.Roughness > (s.UseStockPbrMap ? 4 : 1)
            || s.Metallic < 0 || s.Metallic > (s.UseStockPbrMap ? 4 : 1))
            throw new InvalidOperationException("Invalid saved canopy settings.");
    }
}
