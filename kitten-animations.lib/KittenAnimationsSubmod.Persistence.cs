using System;
using System.Collections.Generic;
using System.Linq;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.KittenAnimationsLib;

public sealed partial class KittenAnimationsSubmod
{
    private SavedAnimationTuning? _originalTuning;
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return new SaveParticipant<SavedAnimations>("kitten-animations", CaptureAnimations, ResetAnimations, RestoreAnimations, validate: ValidateAnimations); }
    }

    private static void ValidateAnimations(SavedAnimations saved)
    {
        if (!Enum.IsDefined(saved.LatchedExpression) || saved.ExpressionVariant < -1 || saved.LatchedVariant < -1
            || saved.ClipPhase < 0 || saved.BlendTime < 0 || saved.PlaybackRateScale < 0
            || saved.EaseInDuration < 0 || saved.HoldDuration < 0 || saved.EaseOutDuration < 0
            || saved.PeakWeight is < 0 or > 1 || saved.EarWeight is < 0 or > 1
            || saved.PersonalityWeight is < 0 or > 1 || saved.ReactiveExpressionMax is < 0 or > 1
            || saved.EyePitchDeg is < -90 or > 90 || saved.EyeLookAngleDeg is < 0 or > 90
            || (saved.ClipActive && (string.IsNullOrWhiteSpace(saved.ClipSource) || string.IsNullOrWhiteSpace(saved.ClipLabel))))
            throw new InvalidOperationException("Invalid saved kitten animation configuration.");
        saved.Tuning?.Validate();
    }

    private SavedAnimations CaptureAnimations() => new()
    {
        SelectedKittenId = _selectedKittenId, ExpressionVariant = _context?.ExpressionVariant ?? -1,
        ClipLabel = _driver.ForcedLabel,
        ClipActive = _driver.OverrideActive, ClipPaused = _driver.Paused,
        ClipPhase = KittenPlaybackPhase.Capture(_driver.TargetModel, _driver.ForcedAnimation),
        LatchedExpression = _expressions.Latch ? _expressions.Current : KittenExpressionController.ExpressionType.None,
        LatchedVariant = _expressions.CurrentVariantIndex,
        LatchedClipId = CaptureLatchedClipId(),
        ClipSource = _context?.Catalog.Groups.SelectMany(g => g.Entries)
            .FirstOrDefault(e => ReferenceEquals(e.Animation, _driver.ForcedAnimation) && e.Label == _driver.ForcedLabel)?.Source,
        Tuning = SavedAnimationTuning.Capture(),
        BlendTime = _driver.BlendTime,
        PlaybackRateScale = _driver.PlaybackRateScale,
        OverrideEarWeight = _driver.OverrideEarWeight,
        EarWeight = _driver.EarWeight,
        OverrideEyeLookAngle = _driver.OverrideEyeLookAngle,
        EyeLookAngleDeg = _driver.EyeLookAngleDeg,
        OverrideEyePitch = _driver.OverrideEyePitch,
        EyePitchDeg = _driver.EyePitchDeg,
        OverridePersonalityWeight = _driver.OverridePersonalityWeight,
        PersonalityWeight = _driver.PersonalityWeight,
        LimitReactiveExpression = _driver.LimitReactiveExpression,
        ReactiveExpressionMax = _driver.ReactiveExpressionMax,
        EaseInDuration = _expressions.EaseInDuration,
        HoldDuration = _expressions.HoldDuration,
        EaseOutDuration = _expressions.EaseOutDuration,
        PeakWeight = _expressions.PeakWeight,
        Latch = _expressions.Latch,
    };

    private void ResetAnimations()
    {
        Unbind();
        _driver.Reset();
        _selectedKittenId = null;
        _originalTuning?.Apply();
        ApplyAnimationSettings(new SavedAnimations());
    }

    private void RestoreAnimations(SavedAnimations saved, SaveRestoreContext context)
    {
        context.Require(!saved.ClipPhase.HasValue || (saved.ClipPhase.Value >= 0 && KittenPlaybackPhase.IsAvailable),
            "Saved animation phase or native playback fields are unavailable.");
        _selectedKittenId = saved.SelectedKittenId;
        saved.Tuning?.Apply();
        ApplyAnimationSettings(saved);
        Update(0);
        if (_context != null) _context.ExpressionVariant = saved.ExpressionVariant;
        if (_context != null && saved.ClipSource != null)
        {
            var matches = _context.Catalog.Groups.SelectMany(g => g.Entries)
                .Where(e => e.Source == saved.ClipSource && e.Label == saved.ClipLabel).ToArray();
            if (matches.Length == 1) _driver.RestorePlayback(matches[0], saved.ClipActive, saved.ClipPaused, saved.ClipPhase);
            else context.Warn($"Saved animation clip unavailable or ambiguous: {saved.ClipLabel}.");
        }
        if (_context != null && saved.LatchedExpression != KittenExpressionController.ExpressionType.None && saved.Latch)
        {
            var variants = KittenExpressionController.GetVariants(_context.Avatar, saved.LatchedExpression);
            int variantIndex = saved.LatchedClipId == null ? -1
                : variants?.FindIndex(v => v != null && v.Id.ToString() == saved.LatchedClipId) ?? -1;
            if (variants == null || variantIndex < 0)
                context.Warn("Saved latched expression variant is unavailable.");
            else
            {
                _expressions.Trigger(_context.Avatar, saved.LatchedExpression, variantIndex, _random);
                _expressions.Update(_expressions.EaseInDuration);
            }
        }
        if (_selectedKittenId != null && _context == null)
            context.Warn($"Animation target '{_selectedKittenId}' is unavailable.");
        // Unlatched one-shot expressions stay stopped. Forced looping clips/frozen poses
        // and latched faces are durable scene setup, so they resume on the normal pose pass.
    }

    private string? CaptureLatchedClipId()
    {
        if (!_expressions.Latch || _context == null) return null;
        var variants = KittenExpressionController.GetVariants(_context.Avatar, _expressions.Current);
        int index = _expressions.CurrentVariantIndex;
        return variants != null && index >= 0 && index < variants.Count ? variants[index]?.Id.ToString() : null;
    }

    private void ApplyAnimationSettings(SavedAnimations saved)
    {
        _driver.BlendTime = saved.BlendTime;
        _driver.PlaybackRateScale = saved.PlaybackRateScale;
        _driver.OverrideEarWeight = saved.OverrideEarWeight;
        _driver.EarWeight = saved.EarWeight;
        _driver.OverrideEyeLookAngle = saved.OverrideEyeLookAngle;
        _driver.EyeLookAngleDeg = saved.EyeLookAngleDeg;
        _driver.OverrideEyePitch = saved.OverrideEyePitch;
        _driver.EyePitchDeg = saved.EyePitchDeg;
        _driver.OverridePersonalityWeight = saved.OverridePersonalityWeight;
        _driver.PersonalityWeight = saved.PersonalityWeight;
        _driver.LimitReactiveExpression = saved.LimitReactiveExpression;
        _driver.ReactiveExpressionMax = saved.ReactiveExpressionMax;
        _expressions.EaseInDuration = saved.EaseInDuration;
        _expressions.HoldDuration = saved.HoldDuration;
        _expressions.EaseOutDuration = saved.EaseOutDuration;
        _expressions.PeakWeight = saved.PeakWeight;
        _expressions.Latch = saved.Latch;
    }

    public sealed class SavedAnimations
    {
        public string? SelectedKittenId { get; set; }
        public string? ClipLabel { get; set; }
        public string? ClipSource { get; set; }
        public bool ClipActive { get; set; }
        public bool ClipPaused { get; set; }
        public float? ClipPhase { get; set; }
        public KittenExpressionController.ExpressionType LatchedExpression { get; set; }
        public int LatchedVariant { get; set; } = -1;
        public string? LatchedClipId { get; set; }
        public int ExpressionVariant { get; set; } = -1;
        public SavedAnimationTuning? Tuning { get; set; }
        public float BlendTime { get; set; } = 0.15f;
        public float PlaybackRateScale { get; set; } = 1f;
        public bool OverrideEarWeight { get; set; }
        public float EarWeight { get; set; } = 1f;
        public bool OverrideEyeLookAngle { get; set; }
        public float EyeLookAngleDeg { get; set; } = 30f;
        public bool OverrideEyePitch { get; set; }
        public float EyePitchDeg { get; set; }
        public bool OverridePersonalityWeight { get; set; }
        public float PersonalityWeight { get; set; } = 1f;
        public bool LimitReactiveExpression { get; set; }
        public float ReactiveExpressionMax { get; set; } = 1f;
        public float EaseInDuration { get; set; } = 0.25f;
        public float HoldDuration { get; set; } = 1.5f;
        public float EaseOutDuration { get; set; } = 0.25f;
        public float PeakWeight { get; set; } = 1f;
        public bool Latch { get; set; }
    }
}
