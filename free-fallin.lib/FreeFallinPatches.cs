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
    private static readonly List<WeakReference<AnimatedRenderable>> Observed = new();
    private static readonly ConditionalWeakTable<AnimatedRenderable, SeenMarker> Seen = new();
    private static readonly MethodInfo Target = AccessTools.Method(typeof(ChuteRenderable), nameof(ChuteRenderable.Draw))
        ?? throw new MissingMethodException(typeof(ChuteRenderable).FullName, nameof(ChuteRenderable.Draw));
    private static readonly MethodInfo Prefix = AccessTools.Method(typeof(FreeFallinPatches), nameof(BeforeDraw))!;

    private sealed class SeenMarker { }

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
        Seen.Add(renderable, new SeenMarker());
        Observed.Add(new WeakReference<AnimatedRenderable>(renderable));
    }

    private static void RestoreObserved()
    {
        int stock = CanopyMaterialController.ResolveStockHandle();
        if (stock < 0) return;
        ReplaceObserved(stock);
    }

    internal static void ReplaceObserved(int handle)
    {
        for (int i = Observed.Count - 1; i >= 0; i--)
        {
            if (!Observed[i].TryGetTarget(out AnimatedRenderable? renderable)) { Observed.RemoveAt(i); continue; }
            SetHandle(renderable, handle);
        }
    }

    private static void SetHandle(AnimatedRenderable renderable, int handle)
    {
        if (MaterialIndicesField.GetValue(renderable) is int[] indices && indices.Length > 0) indices[0] = handle;
    }
}
