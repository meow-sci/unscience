using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using MeowSci.KsaAbstractions;

namespace MeowSci.GarrysTorchLib;

public sealed class SavedWeldAnimation
{
    public float3 StartPosition { get; set; }
    public float3 StartRotation { get; set; }
    public float3 StartScale { get; set; }
    public float3 TargetPosition { get; set; }
    public float3 TargetRotation { get; set; }
    public float3 TargetScale { get; set; }
    public double Duration { get; set; }
    public double Elapsed { get; set; }
    public EasingType Easing { get; set; }
    public double PowerStart { get; set; }
    public double PowerEnd { get; set; }

    internal void Validate()
    {
        if (!Finite(StartPosition) || !Finite(StartRotation) || !WeldScale.IsValid(StartScale)
            || !Finite(TargetPosition) || !Finite(TargetRotation) || !WeldScale.IsValid(TargetScale)
            || !double.IsFinite(Duration) || Duration <= 0 || Duration > 1e9
            || !double.IsFinite(Elapsed) || Elapsed < 0 || Elapsed > Duration || !Enum.IsDefined(Easing)
            || !double.IsFinite(PowerStart) || PowerStart <= 0 || !double.IsFinite(PowerEnd) || PowerEnd <= 0)
            throw new InvalidOperationException("Invalid saved weld animation.");
    }
    private static bool Finite(float3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

public partial class WeldAnimation
{
    internal SavedWeldAnimation CaptureSaved() => new()
    {
        StartPosition = StartPosition, StartRotation = StartRotation, StartScale = StartScale,
        TargetPosition = TargetPosition, TargetRotation = TargetRotation, TargetScale = TargetScale,
        Duration = DurationSeconds, Elapsed = ElapsedSeconds, Easing = Easing,
        PowerStart = EasingPowerStart, PowerEnd = EasingPowerEnd
    };

    internal static WeldAnimation FromSaved(SavedWeldAnimation saved)
    {
        saved.Validate();
        return new(saved.StartPosition, saved.StartRotation, saved.StartScale,
            saved.TargetPosition, saved.TargetRotation, saved.TargetScale,
            saved.Duration, saved.Easing, saved.PowerStart, saved.PowerEnd) { ElapsedSeconds = saved.Elapsed };
    }
}

public partial class WeldAnimationManager
{
    internal List<SavedWeldAnimation> CaptureSaved(WeldEntry weld)
    {
        var result = new List<SavedWeldAnimation>();
        if (_active.TryGetValue(weld, out var active)) result.Add(active.CaptureSaved());
        if (_queues.TryGetValue(weld, out var queue)) result.AddRange(queue.Select(a => a.CaptureSaved()));
        return result;
    }

    internal void RestoreSaved(WeldEntry weld, List<SavedWeldAnimation> saved)
    {
        var animations = saved.Select(WeldAnimation.FromSaved).ToArray();
        CancelAll(weld);
        if (animations.Length == 0) return;
        _active.Add(weld, animations[0]);
        if (animations.Length > 1) _queues.Add(weld, new Queue<WeldAnimation>(animations.Skip(1)));
    }
}
