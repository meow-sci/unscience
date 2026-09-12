using System;
using System.Reflection;
using KSA;

namespace MeowSci.KittenAnimationsLib;

/// <summary>Only the selected clip's clock is persisted, never native pose buffers or renderer handles.</summary>
internal static class KittenPlaybackPhase
{
    private static readonly FieldInfo? Runtime = typeof(AnimatedRenderable).GetField("RuntimeAnim", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? Clock = typeof(BoneAnimRuntime).GetField("TimeSinceTransition", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? Animation = typeof(BoneAnimRuntime).GetField("CurrentAnimation", BindingFlags.Instance | BindingFlags.NonPublic);
    public static bool IsAvailable => Runtime != null && Clock != null && Animation != null;

    public static float? Capture(AnimatedRenderable? model, IAnimation? expected)
    {
        if (model == null || expected == null) return null;
        if (!IsAvailable) throw new InvalidOperationException("Kitten animation playback fields are unavailable in this game build.");
        var runtime = Runtime?.GetValue(model) as BoneAnimRuntime;
        if (runtime == null || !ReferenceEquals(Animation?.GetValue(runtime), expected)) return null;
        return Clock?.GetValue(runtime) is float time && float.IsFinite(time) ? time : null;
    }

    public static void Restore(AnimatedRenderable model, float time)
    {
        if (!float.IsFinite(time) || time < 0) throw new InvalidOperationException("Invalid saved kitten animation phase.");
        var runtime = Runtime?.GetValue(model) as BoneAnimRuntime;
        if (runtime == null || Clock == null) throw new InvalidOperationException("Kitten animation playback clock is unavailable in this game build.");
        Clock.SetValue(runtime, time);
        // FreezeAnimation can suppress Update entirely, so explicitly sample the restored
        // pose before the normal skinning pass consumes it. No GPU resources are allocated.
        runtime.SampleCurrentAnimation();
    }
}
