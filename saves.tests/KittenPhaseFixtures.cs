using System;
using MeowSci.KittenAnimationsLib;

namespace KSA
{
    public interface IAnimation { }

    public class BoneAnimRuntime
    {
        protected float TimeSinceTransition;
        protected IAnimation? CurrentAnimation;
        public float SampledTime { get; private set; }
        public void Select(IAnimation animation, float time) { CurrentAnimation = animation; TimeSinceTransition = time; }
        public void SampleCurrentAnimation() => SampledTime = TimeSinceTransition;
    }

    public class AnimatedRenderable
    {
        protected BoneAnimRuntime RuntimeAnim = new();
        public BoneAnimRuntime FixtureRuntime => RuntimeAnim;
    }
}

internal static class KittenPhaseTests
{
    private sealed class Clip : KSA.IAnimation { }
    public static void Run()
    {
        var clip = new Clip();
        var otherClip = new Clip();
        var model = new KSA.AnimatedRenderable();
        model.FixtureRuntime.Select(clip, 1.75f);
        Check(KittenPlaybackPhase.Capture(model, clip) == 1.75f, "capture selected clock");
        Check(KittenPlaybackPhase.Capture(model, otherClip) == null, "do not capture another animation's clock");
        var loaded = new KSA.AnimatedRenderable();
        loaded.FixtureRuntime.Select(clip, 0);
        KittenPlaybackPhase.Restore(loaded, 1.75f);
        Check(loaded.FixtureRuntime.SampledTime == 1.75f, "frozen pose sampled even without next animation Update");
        Check(KittenPlaybackPhase.Capture(loaded, clip) == 1.75f, "phase survives repeated save/load");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            bool rejected = false;
            try { KittenPlaybackPhase.Restore(loaded, invalid); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "invalid phase rejected");
        }
        Console.WriteLine("Kitten playback phase: identity, frozen sampling, repeatability and invalid inputs passed.");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
