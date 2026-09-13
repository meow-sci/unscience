using KSA;
using System;

namespace MeowSci.KittenAnimationsLib;

/// <summary>Only animation-facing global tuning; physics parameters remain outside this feature.</summary>
public sealed class SavedAnimationTuning
{
    public void Validate()
    {
        foreach (float value in new[] { AnimBlendTime, IdleSpeedThreshold, PlaybackRateMin, PlaybackRateMax,
            WalkClipNominalSpeed, RunClipNominalSpeed, LadderNominalSpeed, TumbleNominalSpeed,
            MoonwalkWalkNominalSpeed, MoonwalkRunNominalSpeed, MoonwalkStartGravity, MoonwalkFullGravity,
            MoonwalkPlaybackScale, NominalSwimAnimSpeed, SwimBlendFullSpeed, SwimBlendHalfLife,
            SwimEyePitchFactor, JumpLandDuration, JumpLandBounceIgnoreTime })
            if (value < 0) throw new InvalidOperationException("Animation tuning cannot use negative durations/rates.");
        if (PlaybackRateMin > PlaybackRateMax || LadderEyePitchDeg is < -90 or > 90)
            throw new InvalidOperationException("Invalid animation playback bounds or ladder eye angle.");
    }
    public float AnimBlendTime { get; set; }
    public float IdleSpeedThreshold { get; set; }
    public float PlaybackRateMin { get; set; }
    public float PlaybackRateMax { get; set; }
    public float WalkClipNominalSpeed { get; set; }
    public float RunClipNominalSpeed { get; set; }
    public float LadderNominalSpeed { get; set; }
    public float TumbleNominalSpeed { get; set; }
    public float MoonwalkWalkNominalSpeed { get; set; }
    public float MoonwalkRunNominalSpeed { get; set; }
    public float MoonwalkStartGravity { get; set; }
    public float MoonwalkFullGravity { get; set; }
    public float MoonwalkPlaybackScale { get; set; }
    public float NominalSwimAnimSpeed { get; set; }
    public float SwimBlendFullSpeed { get; set; }
    public float SwimBlendHalfLife { get; set; }
    public float SwimEyePitchFactor { get; set; }
    public float JumpLandDuration { get; set; }
    public float JumpLandBounceIgnoreTime { get; set; }
    public float LadderEyePitchDeg { get; set; }

    public static SavedAnimationTuning Capture()
    {
        var source = KittenLocomotionTuning.Current;
        return new()
        {
            AnimBlendTime = source.AnimBlendTime,
            IdleSpeedThreshold = source.IdleSpeedThreshold,
            PlaybackRateMin = source.PlaybackRateMin,
            PlaybackRateMax = source.PlaybackRateMax,
            WalkClipNominalSpeed = source.WalkClipNominalSpeed,
            RunClipNominalSpeed = source.RunClipNominalSpeed,
            LadderNominalSpeed = source.LadderNominalSpeed,
            TumbleNominalSpeed = source.TumbleNominalSpeed,
            MoonwalkWalkNominalSpeed = source.MoonwalkWalkNominalSpeed,
            MoonwalkRunNominalSpeed = source.MoonwalkRunNominalSpeed,
            MoonwalkStartGravity = source.MoonwalkStartGravity,
            MoonwalkFullGravity = source.MoonwalkFullGravity,
            MoonwalkPlaybackScale = source.MoonwalkPlaybackScale,
            NominalSwimAnimSpeed = source.NominalSwimAnimSpeed,
            SwimBlendFullSpeed = source.SwimBlendFullSpeed,
            SwimBlendHalfLife = source.SwimBlendHalfLife,
            SwimEyePitchFactor = source.SwimEyePitchFactor,
            JumpLandDuration = source.JumpLandDuration,
            JumpLandBounceIgnoreTime = source.JumpLandBounceIgnoreTime,
            LadderEyePitchDeg = source.LadderEyePitchDeg,
        };
    }

    public void Apply()
    {
        ref var target = ref KittenLocomotionTuning.Current;
        target.AnimBlendTime = AnimBlendTime;
        target.IdleSpeedThreshold = IdleSpeedThreshold;
        target.PlaybackRateMin = PlaybackRateMin;
        target.PlaybackRateMax = PlaybackRateMax;
        target.WalkClipNominalSpeed = WalkClipNominalSpeed;
        target.RunClipNominalSpeed = RunClipNominalSpeed;
        target.LadderNominalSpeed = LadderNominalSpeed;
        target.TumbleNominalSpeed = TumbleNominalSpeed;
        target.MoonwalkWalkNominalSpeed = MoonwalkWalkNominalSpeed;
        target.MoonwalkRunNominalSpeed = MoonwalkRunNominalSpeed;
        target.MoonwalkStartGravity = MoonwalkStartGravity;
        target.MoonwalkFullGravity = MoonwalkFullGravity;
        target.MoonwalkPlaybackScale = MoonwalkPlaybackScale;
        target.NominalSwimAnimSpeed = NominalSwimAnimSpeed;
        target.SwimBlendFullSpeed = SwimBlendFullSpeed;
        target.SwimBlendHalfLife = SwimBlendHalfLife;
        target.SwimEyePitchFactor = SwimEyePitchFactor;
        target.JumpLandDuration = JumpLandDuration;
        target.JumpLandBounceIgnoreTime = JumpLandBounceIgnoreTime;
        target.LadderEyePitchDeg = LadderEyePitchDeg;
    }
}
