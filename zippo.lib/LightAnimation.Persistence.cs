using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using MeowSci.KsaAbstractions;

namespace MeowSci.ZippoLib;

public sealed class SavedLightAnimation
{
    public float3 StartColor { get; set; }
    public float3 EndColor { get; set; }
    public float StartIntensity { get; set; }
    public float EndIntensity { get; set; }
    public double Duration { get; set; }
    public double Elapsed { get; set; }
    public EasingType Easing { get; set; }
    public double PowerStart { get; set; }
    public double PowerEnd { get; set; }

    internal void Validate()
    {
        if (!Finite(StartColor) || !Finite(EndColor) || !float.IsFinite(StartIntensity) || !float.IsFinite(EndIntensity)
            || !double.IsFinite(Duration) || Duration <= 0 || Duration > 1e9
            || !double.IsFinite(Elapsed) || Elapsed < 0 || Elapsed > Duration || !Enum.IsDefined(Easing)
            || !double.IsFinite(PowerStart) || PowerStart <= 0 || !double.IsFinite(PowerEnd) || PowerEnd <= 0)
            throw new InvalidOperationException("Invalid saved light transition.");
    }
    private static bool Finite(float3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

public partial class LightAnimation
{
    internal SavedLightAnimation CaptureSaved() => new()
    {
        StartColor = StartColor, EndColor = EndColor, StartIntensity = StartIntensity, EndIntensity = EndIntensity,
        Duration = DurationSeconds, Elapsed = ElapsedSeconds, Easing = Easing,
        PowerStart = EasingPowerStart, PowerEnd = EasingPowerEnd
    };

    internal static LightAnimation FromSaved(SavedLightAnimation saved)
    {
        saved.Validate();
        return new(saved.StartColor, saved.EndColor, saved.StartIntensity, saved.EndIntensity,
            saved.Duration, saved.Easing, saved.PowerStart, saved.PowerEnd) { ElapsedSeconds = saved.Elapsed };
    }
}

public partial class LightAnimationManager
{
    internal List<SavedLightAnimation> CaptureSaved(string partKey)
    {
        var result = new List<SavedLightAnimation>();
        if (_active.TryGetValue(partKey, out var active)) result.Add(active.CaptureSaved());
        if (_queues.TryGetValue(partKey, out var queue)) result.AddRange(queue.Select(a => a.CaptureSaved()));
        return result;
    }

    internal void RestoreSaved(string partKey, List<SavedLightAnimation> saved)
    {
        var animations = saved.Select(LightAnimation.FromSaved).ToArray();
        CancelAll(partKey);
        if (animations.Length == 0) return;
        _active.Add(partKey, animations[0]);
        if (animations.Length > 1) _queues.Add(partKey, new Queue<LightAnimation>(animations.Skip(1)));
    }
}
