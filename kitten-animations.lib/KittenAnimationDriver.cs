using System;
using KSA;

namespace MeowSci.KittenAnimationsLib;

/// <summary>
/// Holds the mod's animation overrides and stamps them onto the kitten model at the one point in the
/// frame where they survive.
///
/// KittenRenderable.UpdateRenderData re-picks the body clip every frame from the locomotion state
/// (idle/walk/run/jump/tumble/ladder/swim/MMU blend), so a clip set from a StarMap callback is
/// overwritten before it is ever sampled. <see cref="ApplyBeforePose"/> runs from a Harmony prefix on
/// AnimatedRenderable.UpdateAnimation — after the game has finished choosing, before the pose is
/// evaluated — so whatever it sets is what gets rendered.
/// </summary>
public sealed class KittenAnimationDriver
{
    private float? _savedPlaybackPhase;
    private bool _wasOverriding;
    private bool _restartRequested;
    private float? _originalEarWeight;
    private float? _originalEyeLookAngle;
    private float? _originalPersonalityWeight;
    private bool _earOverrideApplied;
    private bool _eyeLookOverrideApplied;
    private bool _personalityOverrideApplied;

    /// <summary>The kitten body model the overrides apply to.</summary>
    public AnimatedRenderable? TargetModel { get; private set; }

    /// <summary>The game's own animation processors on that model.</summary>
    public KittenAnimProcessors? Processors { get; private set; }

    // --- clip override ---

    public bool OverrideActive { get; set; }
    public IAnimation? ForcedAnimation { get; private set; }
    public string ForcedLabel { get; private set; } = string.Empty;

    /// <summary>Cross-fade time (s) used when the forced clip changes.</summary>
    public float BlendTime { get; set; } = 0.15f;

    /// <summary>Multiplies the animation delta time, on top of the game's own playback rate.</summary>
    public float PlaybackRateScale { get; set; } = 1f;

    /// <summary>Freezes the forced clip on its current frame.</summary>
    public bool Paused { get; set; }

    // --- animation processor strength ---

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

    /// <summary>Starts forcing an animation, restarting it from the top.</summary>
    public void Play(AnimationEntry entry)
    {
        _savedPlaybackPhase = null;
        ForcedAnimation = entry.Animation;
        ForcedLabel = entry.Label;
        OverrideActive = true;
        _restartRequested = true;
    }

    /// <summary>Restarts the current forced clip from its first frame.</summary>
    public void Restart()
    {
        _savedPlaybackPhase = null;
        _restartRequested = true;
    }

    /// <summary>Rebind durable forced playback; its pose is sampled on the next normal render update.</summary>
    public void RestorePlayback(AnimationEntry entry, bool active, bool paused, float? phase)
    {
        Play(entry);
        OverrideActive = active;
        Paused = paused;
        _savedPlaybackPhase = phase;
    }

    /// <summary>Hands the body animation back to the game, keeping the selected clip for re-enabling.</summary>
    public void Release()
    {
        OverrideActive = false;
        Paused = false;
    }

    /// <summary>Forgets the selected clip as well as releasing control.</summary>
    public void ClearClip()
    {
        _savedPlaybackPhase = null;
        Release();
        ForcedAnimation = null;
        ForcedLabel = string.Empty;
        _restartRequested = false;
    }

    /// <summary>Binds the driver to one kitten and records the persistent values it may override.</summary>
    public void BindTarget(AnimatedRenderable model, KittenAnimProcessors processors)
    {
        if (ReferenceEquals(TargetModel, model) && ReferenceEquals(Processors, processors)) return;

        UnbindTarget();
        TargetModel = model;
        Processors = processors;
        _originalEarWeight = processors.Ear?.ExpressionWeight;
        _originalEyeLookAngle = processors.Eye?.MaxLookAtAngle;
        _originalPersonalityWeight = processors.Personality?.ExpressionWeight;
    }

    /// <summary>Releases the current kitten and restores persistent processor values owned by the mod.</summary>
    public void UnbindTarget()
    {
        RestorePersistentProcessorOverrides();

        if (_wasOverriding && TargetModel != null)
            TargetModel.FreezeAnimation = false;

        TargetModel = null;
        Processors = null;
        _originalEarWeight = null;
        _originalEyeLookAngle = null;
        _originalPersonalityWeight = null;
        _earOverrideApplied = false;
        _eyeLookOverrideApplied = false;
        _personalityOverrideApplied = false;
        _wasOverriding = false;
    }

    /// <summary>Clears every override and forgets the resolved model. Called on unload / kitten change.</summary>
    public void Reset()
    {
        _savedPlaybackPhase = null;
        UnbindTarget();
        Release();
        ForcedAnimation = null;
        ForcedLabel = string.Empty;
        _restartRequested = false;
        PlaybackRateScale = 1f;
        OverrideEarWeight = false;
        OverrideEyeLookAngle = false;
        OverrideEyePitch = false;
        OverridePersonalityWeight = false;
        LimitReactiveExpression = false;
    }

    /// <summary>
    /// Applies the mod's overrides to <paramref name="model"/> if it is the kitten body. Called from the
    /// Harmony prefix on every AnimatedRenderable in the scene, so it stays cheap and never throws.
    /// </summary>
    public void ApplyBeforePose(AnimatedRenderable model, ref double dt)
    {
        var target = TargetModel;
        if (target == null || !ReferenceEquals(model, target)) return;

        try
        {
            ApplyProcessorOverrides();
            ApplyClipOverride(model, ref dt);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"kitten-animations: Error applying animation override: {ex.Message}");
            Reset();
        }
    }

    private void ApplyProcessorOverrides()
    {
        var processors = Processors;
        if (processors == null) return;

        if (processors.Ear != null)
        {
            if (OverrideEarWeight)
            {
                processors.Ear.ExpressionWeight = EarWeight;
                _earOverrideApplied = true;
            }
            else if (_earOverrideApplied && _originalEarWeight.HasValue)
            {
                processors.Ear.ExpressionWeight = _originalEarWeight.Value;
                _earOverrideApplied = false;
            }
        }

        if (processors.Eye != null)
        {
            if (OverrideEyeLookAngle)
            {
                processors.Eye.MaxLookAtAngle = EyeLookAngleDeg;
                _eyeLookOverrideApplied = true;
            }
            else if (_eyeLookOverrideApplied && _originalEyeLookAngle.HasValue)
            {
                processors.Eye.MaxLookAtAngle = _originalEyeLookAngle.Value;
                _eyeLookOverrideApplied = false;
            }

            // The game rewrites LookPitchOffsetDeg every frame (0 unless on a ladder or swimming).
            if (OverrideEyePitch)
                processors.Eye.LookPitchOffsetDeg = EyePitchDeg;
        }

        if (processors.Personality != null)
        {
            if (OverridePersonalityWeight)
            {
                processors.Personality.ExpressionWeight = PersonalityWeight;
                _personalityOverrideApplied = true;
            }
            else if (_personalityOverrideApplied && _originalPersonalityWeight.HasValue)
            {
                processors.Personality.ExpressionWeight = _originalPersonalityWeight.Value;
                _personalityOverrideApplied = false;
            }
        }

        // The reactive face is driven from vehicle acceleration; we can only cap it after the fact.
        if (LimitReactiveExpression && processors.Reactive != null)
            processors.Reactive.ExpressionWeight = Math.Min(processors.Reactive.ExpressionWeight, ReactiveExpressionMax);
    }

    private void ApplyClipOverride(AnimatedRenderable model, ref double dt)
    {
        if (!OverrideActive || ForcedAnimation == null)
        {
            if (_wasOverriding)
            {
                model.FreezeAnimation = false;
                _wasOverriding = false;
            }
            return;
        }

        if (_restartRequested)
        {
            model.PlayAnimation(ForcedAnimation, _savedPlaybackPhase.HasValue ? 0 : BlendTime);
            if (_savedPlaybackPhase.HasValue)
            {
                KittenPlaybackPhase.Restore(model, _savedPlaybackPhase.Value);
                _savedPlaybackPhase = null;
            }
            _restartRequested = false;
        }
        else
        {
            // No-op when the clip is already current, so this is safe to call every frame.
            model.SetAnimation(ForcedAnimation, BlendTime);
        }

        model.FreezeAnimation = Paused;
        _wasOverriding = true;

        if (PlaybackRateScale != 1f)
            dt *= PlaybackRateScale;
    }

    private void RestorePersistentProcessorOverrides()
    {
        var processors = Processors;
        if (processors == null) return;

        if (_earOverrideApplied && processors.Ear != null && _originalEarWeight.HasValue)
            processors.Ear.ExpressionWeight = _originalEarWeight.Value;

        if (_eyeLookOverrideApplied && processors.Eye != null && _originalEyeLookAngle.HasValue)
            processors.Eye.MaxLookAtAngle = _originalEyeLookAngle.Value;

        if (_personalityOverrideApplied && processors.Personality != null && _originalPersonalityWeight.HasValue)
            processors.Personality.ExpressionWeight = _originalPersonalityWeight.Value;
    }
}
