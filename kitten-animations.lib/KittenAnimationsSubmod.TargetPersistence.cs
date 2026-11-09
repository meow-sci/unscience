using System.Linq;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.KittenAnimationsLib;

public sealed partial class KittenAnimationsSubmod
{
    private static SavedKittenAnimation CaptureTarget(Ui.AnimationUiContext ui) => new()
    {
        KittenId = ui.Kitten.Id, ExpressionVariant = ui.ExpressionVariant,
        ClipLabel = ui.Driver.ForcedLabel,
        ClipActive = ui.Driver.OverrideActive, ClipPaused = ui.Driver.Paused,
        ClipPhase = ui.Driver.CapturePhase(),
        LatchedExpression = ui.Expressions.Latch ? ui.Expressions.Current : KittenExpressionController.ExpressionType.None,
        LatchedVariant = ui.Expressions.CurrentVariantIndex,
        LatchedClipId = CaptureLatchedClipId(ui),
        ClipSource = ui.Catalog.Groups.SelectMany(g => g.Entries)
            .FirstOrDefault(e => ReferenceEquals(e.Animation, ui.Driver.ForcedAnimation) && e.Label == ui.Driver.ForcedLabel)?.Source,
        BlendTime = ui.Driver.BlendTime,
        PlaybackRateScale = ui.Driver.PlaybackRateScale,
        OverrideEarWeight = ui.Driver.OverrideEarWeight,
        EarWeight = ui.Driver.EarWeight,
        OverrideEyeLookAngle = ui.Driver.OverrideEyeLookAngle,
        EyeLookAngleDeg = ui.Driver.EyeLookAngleDeg,
        OverrideEyePitch = ui.Driver.OverrideEyePitch,
        EyePitchDeg = ui.Driver.EyePitchDeg,
        OverridePersonalityWeight = ui.Driver.OverridePersonalityWeight,
        PersonalityWeight = ui.Driver.PersonalityWeight,
        LimitReactiveExpression = ui.Driver.LimitReactiveExpression,
        ReactiveExpressionMax = ui.Driver.ReactiveExpressionMax,
        EaseInDuration = ui.Expressions.EaseInDuration,
        HoldDuration = ui.Expressions.HoldDuration,
        EaseOutDuration = ui.Expressions.EaseOutDuration,
        PeakWeight = ui.Expressions.PeakWeight,
        Latch = ui.Expressions.Latch,
    };

    private static void RestoreTarget(SavedKittenAnimation saved, Ui.AnimationUiContext ui, SaveRestoreContext context)
    {
        context.Require(!saved.ClipPhase.HasValue || (saved.ClipPhase.Value >= 0 && KittenPlaybackPhase.IsAvailable),
            "Saved animation phase or native playback fields are unavailable.");
        ApplyAnimationSettings(saved, ui);
        ui.ExpressionVariant = saved.ExpressionVariant;
        if (saved.ClipSource != null)
        {
            var matches = ui.Catalog.Groups.SelectMany(g => g.Entries)
                .Where(e => e.Source == saved.ClipSource && e.Label == saved.ClipLabel).ToArray();
            if (matches.Length == 1) ui.Driver.RestorePlayback(matches[0], saved.ClipActive, saved.ClipPaused, saved.ClipPhase);
            else context.Warn($"Saved animation clip unavailable or ambiguous: {saved.ClipLabel}.");
        }
        if (saved.LatchedExpression != KittenExpressionController.ExpressionType.None && saved.Latch)
        {
            var variants = KittenExpressionController.GetVariants(ui.Avatar, saved.LatchedExpression);
            int variantIndex = saved.LatchedClipId == null ? -1
                : variants?.FindIndex(v => v != null && v.Id.ToString() == saved.LatchedClipId) ?? -1;
            if (variants == null || variantIndex < 0)
                context.Warn("Saved latched expression variant is unavailable.");
            else
            {
                ui.Expressions.Trigger(ui.Avatar, saved.LatchedExpression, variantIndex, ui.Random);
                ui.Expressions.Update(ui.Expressions.EaseInDuration);
            }
        }
    }

    private static string? CaptureLatchedClipId(Ui.AnimationUiContext ui)
    {
        if (!ui.Expressions.Latch) return null;
        var variants = KittenExpressionController.GetVariants(ui.Avatar, ui.Expressions.Current);
        int index = ui.Expressions.CurrentVariantIndex;
        return variants != null && index >= 0 && index < variants.Count ? variants[index]?.Id.ToString() : null;
    }

    private static void ApplyAnimationSettings(SavedKittenAnimation saved, Ui.AnimationUiContext ui)
    {
        ui.Driver.BlendTime = saved.BlendTime;
        ui.Driver.PlaybackRateScale = saved.PlaybackRateScale;
        ui.Driver.OverrideEarWeight = saved.OverrideEarWeight;
        ui.Driver.EarWeight = saved.EarWeight;
        ui.Driver.OverrideEyeLookAngle = saved.OverrideEyeLookAngle;
        ui.Driver.EyeLookAngleDeg = saved.EyeLookAngleDeg;
        ui.Driver.OverrideEyePitch = saved.OverrideEyePitch;
        ui.Driver.EyePitchDeg = saved.EyePitchDeg;
        ui.Driver.OverridePersonalityWeight = saved.OverridePersonalityWeight;
        ui.Driver.PersonalityWeight = saved.PersonalityWeight;
        ui.Driver.LimitReactiveExpression = saved.LimitReactiveExpression;
        ui.Driver.ReactiveExpressionMax = saved.ReactiveExpressionMax;
        ui.Expressions.EaseInDuration = saved.EaseInDuration;
        ui.Expressions.HoldDuration = saved.HoldDuration;
        ui.Expressions.EaseOutDuration = saved.EaseOutDuration;
        ui.Expressions.PeakWeight = saved.PeakWeight;
        ui.Expressions.Latch = saved.Latch;
    }

}
