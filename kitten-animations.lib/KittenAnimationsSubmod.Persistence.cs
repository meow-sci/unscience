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
        ValidateTarget(saved);
        saved.Tuning?.Validate();
        if (saved.Kittens == null) return; // Explicit migration of the legacy single-target payload.
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in saved.Kittens)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.KittenId) || !ids.Add(target.KittenId))
                throw new InvalidOperationException("Invalid or duplicate saved kitten identity.");
            ValidateTarget(target);
        }
    }

    private static void ValidateTarget(SavedKittenAnimation saved)
    {
        if (!Enum.IsDefined(saved.LatchedExpression) || saved.ExpressionVariant < -1 || saved.LatchedVariant < -1
            || saved.ClipPhase < 0 || saved.BlendTime < 0 || saved.PlaybackRateScale < 0
            || saved.EaseInDuration < 0 || saved.HoldDuration < 0 || saved.EaseOutDuration < 0
            || saved.PeakWeight is < 0 or > 1 || saved.EarWeight is < 0 or > 1
            || saved.PersonalityWeight is < 0 or > 1 || saved.ReactiveExpressionMax is < 0 or > 1
            || saved.EyePitchDeg is < -90 or > 90 || saved.EyeLookAngleDeg is < 0 or > 90
            || (saved.ClipActive && (string.IsNullOrWhiteSpace(saved.ClipSource) || string.IsNullOrWhiteSpace(saved.ClipLabel))))
            throw new InvalidOperationException("Invalid saved kitten animation configuration.");
    }

    private SavedAnimations CaptureAnimations() => new()
    {
        SelectedKittenId = _selectedKittenId,
        Tuning = SavedAnimationTuning.Capture(),
        Kittens = _targets.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Value.Context != null ? CaptureTarget(p.Value.Context)
                : p.Value.Pending ?? new SavedKittenAnimation { KittenId = p.Key }).ToList(),
    };

    private void ResetAnimations()
    {
        foreach (var target in _targets.Values) ReleaseTarget(target);
        _targets.Clear();
        _drivers.Clear();
        _context = null;
        _selectedKittenId = null;
        _originalTuning?.Apply();
    }

    private void RestoreAnimations(SavedAnimations saved, SaveRestoreContext context)
    {
        _selectedKittenId = saved.SelectedKittenId;
        saved.Tuning?.Apply();
        if (saved.Kittens != null)
        {
            foreach (var target in saved.Kittens) RestoreSavedTarget(target.KittenId, target, context);
        }
        else
        {
            // Old v1 records did not save the actual target in follow-controlled mode.
            // Resolve that legacy choice once; new records always carry the exact vehicle id.
            string? id = saved.SelectedKittenId ?? KittenAvatarAccessor.GetControlledKitten()?.Id;
            if (id != null) RestoreSavedTarget(id, saved, context);
            else context.Warn("Legacy animation target is unavailable.");
        }
        Update(0);
    }

    private void RestoreSavedTarget(string id, SavedKittenAnimation saved, SaveRestoreContext context)
    {
        var kitten = KittenAvatarAccessor.FindKitten(id);
        var avatar = KittenAvatarAccessor.GetAvatar(kitten?.Renderable);
        var target = new Target();
        _targets.Add(id, target);
        if (kitten == null || avatar == null)
        {
            // Retain detached intent for a later exact-id rebind. Never use the active vehicle.
            saved.KittenId = id;
            target.Pending = saved;
            context.Warn($"Animation target '{id}' is unavailable or ambiguous.");
            return;
        }
        BindTarget(target, kitten, avatar);
        RestoreTarget(saved, target.Context!, context);
    }

    public sealed class SavedAnimations : SavedKittenAnimation
    {
        public string? SelectedKittenId { get; set; }
        public SavedAnimationTuning? Tuning { get; set; }
        // Missing/null means a legacy single-target v1 record; an empty list explicitly means none.
        public List<SavedKittenAnimation>? Kittens { get; set; }
    }

    public class SavedKittenAnimation
    {
        public string KittenId { get; set; } = "";
        public string? ClipLabel { get; set; }
        public string? ClipSource { get; set; }
        public bool ClipActive { get; set; }
        public bool ClipPaused { get; set; }
        public float? ClipPhase { get; set; }
        public KittenExpressionController.ExpressionType LatchedExpression { get; set; }
        public int LatchedVariant { get; set; } = -1;
        public string? LatchedClipId { get; set; }
        public int ExpressionVariant { get; set; } = -1;
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
