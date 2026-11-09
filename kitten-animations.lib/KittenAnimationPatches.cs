using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using KSA;

namespace MeowSci.KittenAnimationsLib;

/// <summary>
/// Harmony hook that lets the mod own the kitten's body animation.
/// </summary>
/// <remarks>
/// KittenRenderable.UpdateRenderData re-selects the clip every frame from the locomotion state and
/// then immediately calls AnimatedRenderable.UpdateAnimation to sample it. A prefix on
/// UpdateAnimation is therefore the last point in the frame where an override still lands, and the
/// only one that survives grounded locomotion (idle/walk/run/jump/tumble/ladder/swim) — those modes
/// call SetAnimation unconditionally, so anything set from a StarMap callback is discarded.
///
/// The prefix runs for every AnimatedRenderable in the scene; the registry filters by reference to each
/// registered kitten body model and returns immediately for anything else.
/// </remarks>
public static class KittenAnimationPatches
{
    private static MethodInfo? _updateAnimation;
    private static MethodInfo? _prefix;

    /// <summary>Live body models and their independent drivers. Owned by the submod.</summary>
    public static IReadOnlyDictionary<AnimatedRenderable, KittenAnimationDriver>? Drivers { get; set; }

    public static void Apply(Harmony harmony)
    {
        _updateAnimation = AccessTools.Method(typeof(AnimatedRenderable), nameof(AnimatedRenderable.UpdateAnimation));
        if (_updateAnimation == null)
            throw new MissingMethodException(typeof(AnimatedRenderable).FullName, nameof(AnimatedRenderable.UpdateAnimation));

        _prefix = AccessTools.Method(typeof(KittenAnimationPatches), nameof(UpdateAnimationPrefix));
        if (_prefix == null)
            throw new MissingMethodException(typeof(KittenAnimationPatches).FullName, nameof(UpdateAnimationPrefix));

        harmony.Patch(_updateAnimation, prefix: new HarmonyMethod(_prefix));

        Console.WriteLine("kitten-animations.lib: patches applied");
    }

    public static void Remove(Harmony harmony)
    {
        if (_updateAnimation != null && _prefix != null)
            harmony.Unpatch(_updateAnimation, _prefix);

        _updateAnimation = null;
        _prefix = null;
        Drivers = null;

        Console.WriteLine("kitten-animations.lib: patches removed");
    }

    private static void UpdateAnimationPrefix(AnimatedRenderable __instance, ref double dt)
    {
        if (Drivers != null && Drivers.TryGetValue(__instance, out var driver))
            driver.ApplyBeforePose(__instance, ref dt);
    }
}
