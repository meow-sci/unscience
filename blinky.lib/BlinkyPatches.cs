using System;
using System.Reflection;
using HarmonyLib;
using KSA;

namespace MeowSci.BlinkyLib;

/// <summary>Manual Harmony patch helpers for blinky render-skip behaviour.</summary>
public static class BlinkyPatches
{
    private static MethodInfo? _partModelUpdateRenderData;
    private static MethodInfo? _partModelDynamicUpdateRenderData;
    private static MethodInfo? _partModelGlassUpdateRenderData;

    private static MethodInfo? _partModelPrefix;
    private static MethodInfo? _partModelDynamicPrefix;
    private static MethodInfo? _partModelGlassPrefix;

    public static void Apply(Harmony harmony)
    {
        
        _partModelPrefix = typeof(BlinkyPatches).GetMethod(nameof(PartModelModulePrefix), BindingFlags.NonPublic | BindingFlags.Static)!;
        _partModelDynamicPrefix = typeof(BlinkyPatches).GetMethod(nameof(PartModelDynamicModulePrefix), BindingFlags.NonPublic | BindingFlags.Static)!;
        _partModelGlassPrefix = typeof(BlinkyPatches).GetMethod(nameof(PartModelGlassModulePrefix), BindingFlags.NonPublic | BindingFlags.Static)!;

        _partModelUpdateRenderData = AccessTools.Method(typeof(PartModelModule), nameof(PartModelModule.UpdateRenderData));
        _partModelDynamicUpdateRenderData = AccessTools.Method(typeof(PartModelDynamicModule), nameof(PartModelDynamicModule.UpdateRenderData));
        _partModelGlassUpdateRenderData = AccessTools.Method(typeof(PartModelGlassModule), nameof(PartModelGlassModule.UpdateRenderData));

        harmony.Patch(_partModelUpdateRenderData, prefix: new HarmonyMethod(_partModelPrefix));
        harmony.Patch(_partModelDynamicUpdateRenderData, prefix: new HarmonyMethod(_partModelDynamicPrefix));
        harmony.Patch(_partModelGlassUpdateRenderData, prefix: new HarmonyMethod(_partModelGlassPrefix));

        Console.WriteLine("blinky.lib: patches applied");
    }

    public static void Remove(Harmony harmony)
    {
        if (_partModelUpdateRenderData != null && _partModelPrefix != null)
            harmony.Unpatch(_partModelUpdateRenderData, _partModelPrefix);
        if (_partModelDynamicUpdateRenderData != null && _partModelDynamicPrefix != null)
            harmony.Unpatch(_partModelDynamicUpdateRenderData, _partModelDynamicPrefix);
        if (_partModelGlassUpdateRenderData != null && _partModelGlassPrefix != null)
            harmony.Unpatch(_partModelGlassUpdateRenderData, _partModelGlassPrefix);

        _partModelUpdateRenderData = null;
        _partModelDynamicUpdateRenderData = null;
        _partModelGlassUpdateRenderData = null;
        _partModelPrefix = null;
        _partModelDynamicPrefix = null;
        _partModelGlassPrefix = null;

        Console.WriteLine("blinky.lib: patches removed");
    }

    // Skip UpdateRenderData for pixel engine parts (Id starts with "pixel_").
    // Prefix returning false prevents the original from running so the part
    // is not submitted to the GPU while remaining functional in the part tree.

    private static bool PartModelModulePrefix(PartModelModule __instance)
    {
        if (BlinkyPatchState.RenderPixelParts) return true;
        return !BlinkyGridManager.IsPixelPart(__instance.Parent.FullPart);
    }

    private static bool PartModelDynamicModulePrefix(PartModelDynamicModule __instance)
    {
        if (BlinkyPatchState.RenderPixelParts) return true;
        return !BlinkyGridManager.IsPixelPart(__instance.Parent.FullPart);
    }

    private static bool PartModelGlassModulePrefix(PartModelGlassModule __instance)
    {
        if (BlinkyPatchState.RenderPixelParts) return true;
        return !BlinkyGridManager.IsPixelPart(__instance.Parent.FullPart);
    }
}
