using System;
using Brutal.Numerics;
using KSA;
using RenderCore;
using RenderCore.Animation;

internal static class BoneAnimRuntimeChecks
{
    public static void Run()
    {
        using var skeleton = new Skeleton(1);
        using var runtime = new BoneAnimRuntime(skeleton);
        var seated = new OneBoneAnimation(1f);
        var standing = new OneBoneAnimation(0f);

        // KittenRenderable's constructor selects its seated clip through SetAnimation.
        // With no current clip, BoneAnimRuntime removes that initial blend.
        runtime.SetAnimation(seated, 0.2f);
        runtime.Update(0f);
        Require(Near(runtime.Transforms[0].Translation.Y, 1f), "initial seated clip samples the seated pose");

        // This is the failure reported in game: at dt=0 the old seated pose is still at
        // blend amount zero, so selecting ground idle with a timed blend remains seated.
        runtime.PlayAnimation(standing, 0.2f);
        runtime.Update(0f);
        Require(Near(runtime.Transforms[0].Translation.Y, 1f), "zero-delta timed crossfade keeps the prior seated pose");

        // The production appearance prefix now restarts the selected ground idle with no
        // blend. Verify the native runtime samples the upright pose immediately at dt=0.
        runtime.PlayAnimation(standing, 0f);
        runtime.Update(0f);
        Require(Near(runtime.Transforms[0].Translation.Y, 0f), "zero-blend PlayAnimation applies standing pose at zero delta");
        Console.WriteLine("PASS: native BoneAnimRuntime reproduces zero-delta seated blend and verifies immediate standing restart");
    }

    private static bool Near(float actual, float expected) => MathF.Abs(actual - expected) < 1e-6f;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("BoneAnimRuntime: " + message);
    }

    private sealed class OneBoneAnimation(float y) : IAnimation
    {
        public float AnimLength => 1f;

        public void Sample(float time, Span<TransformTRS> output)
        {
            if (output.Length != 1)
                throw new InvalidOperationException($"expected one-bone sample buffer, got {output.Length}");
            output[0] = new TransformTRS
            {
                Translation = new float3(0f, y, 0f),
                Rotation = floatQuat.Identity,
                Scale = float3.One
            };
        }
    }
}
