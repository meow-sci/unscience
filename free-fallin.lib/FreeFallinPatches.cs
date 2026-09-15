using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSA;

namespace MeowSci.FreeFallinLib;

public static class FreeFallinPatches
{
    private static readonly FieldInfo RenderableField = AccessTools.Field(typeof(ChuteRenderable), "_renderable")
        ?? throw new MissingFieldException(typeof(ChuteRenderable).FullName, "_renderable");
    private static readonly FieldInfo MaterialIndicesField = AccessTools.Field(typeof(AnimatedRenderable), "MaterialIndices")
        ?? throw new MissingFieldException(typeof(AnimatedRenderable).FullName, "MaterialIndices");
    private static readonly List<ObservedCanopy> Observed = new();
    private static ConditionalWeakTable<AnimatedRenderable, ObservedCanopy> Seen = new();
    private static readonly MethodInfo Target = AccessTools.Method(typeof(ChuteRenderable), nameof(ChuteRenderable.Draw))
        ?? throw new MissingMethodException(typeof(ChuteRenderable).FullName, nameof(ChuteRenderable.Draw));
    private static readonly MethodInfo Prefix = AccessTools.Method(typeof(FreeFallinPatches), nameof(BeforeDraw))!;

    private sealed class ObservedCanopy
    {
        public readonly WeakReference<AnimatedRenderable> Renderable;
        public readonly int OriginalHandle;
        public int LastAppliedHandle = -1;

        public ObservedCanopy(AnimatedRenderable renderable, int originalHandle)
        {
            Renderable = new WeakReference<AnimatedRenderable>(renderable);
            OriginalHandle = originalHandle;
        }
    }

    public static void Apply(Harmony harmony)
    {
        CanopyProjectionShaders.Apply(harmony);
        harmony.Patch(Target, prefix: new HarmonyMethod(Prefix));
    }

    public static void Remove(Harmony harmony)
    {
        RestoreObserved();
        harmony.Unpatch(Target, Prefix);
        CanopyProjectionShaders.Remove(harmony);
    }

    public static void RestoreStock()
    {
        RestoreObserved();
        CanopyMaterialController.Disable();
    }

    private static void BeforeDraw(ChuteRenderable __instance)
    {
        if (!CanopyMaterialController.Enabled) return;
        if (RenderableField.GetValue(__instance) is not AnimatedRenderable renderable) return;
        Track(renderable);
        SetHandle(renderable, CanopyMaterialController.CurrentMaterialHandle);
    }

    private static void Track(AnimatedRenderable renderable)
    {
        if (Seen.TryGetValue(renderable, out _)) return;
        if (MaterialIndicesField.GetValue(renderable) is not int[] indices || indices.Length == 0) return;
        var observed = new ObservedCanopy(renderable, indices[0]);
        Seen.Add(renderable, observed);
        Observed.Add(observed);
    }

    private static void RestoreObserved()
    {
        for (int i = Observed.Count - 1; i >= 0; i--)
        {
            ObservedCanopy observed = Observed[i];
            if (observed.Renderable.TryGetTarget(out AnimatedRenderable? renderable)
                && MaterialIndicesField.GetValue(renderable) is int[] indices
                && indices.Length > 0
                && observed.LastAppliedHandle >= 0
                && indices[0] == observed.LastAppliedHandle)
            {
                indices[0] = observed.OriginalHandle;
            }
        }

        // Drop both weak references and identity markers. A later Apply must capture each native
        // material afresh, including after KSA recreates a canopy with a different selected style.
        Observed.Clear();
        Seen = new ConditionalWeakTable<AnimatedRenderable, ObservedCanopy>();
    }

    internal static void ReplaceObserved(int handle)
    {
        for (int i = Observed.Count - 1; i >= 0; i--)
        {
            if (!Observed[i].Renderable.TryGetTarget(out AnimatedRenderable? renderable))
            {
                Observed.RemoveAt(i);
                continue;
            }
            SetHandle(renderable, handle);
        }
    }

    private static void SetHandle(AnimatedRenderable renderable, int handle)
    {
        if (MaterialIndicesField.GetValue(renderable) is not int[] indices || indices.Length == 0) return;
        indices[0] = handle;
        if (Seen.TryGetValue(renderable, out ObservedCanopy? observed))
            observed.LastAppliedHandle = handle;
    }
}
