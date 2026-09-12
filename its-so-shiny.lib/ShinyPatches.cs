using System;
using System.Reflection;
using HarmonyLib;
using KSA;

namespace MeowSci.ItsSoShinyLib;

/// <summary>Harmony patch helpers for its-so-shiny render-skip behaviour on shiny light parts.</summary>
public static class ShinyPatches
{
    private static MethodInfo? _partModelUpdateRenderData;
    private static MethodInfo? _partModelDynamicUpdateRenderData;
    private static MethodInfo? _partModelGlassUpdateRenderData;

    private static MethodInfo? _partModelPrefix;
    private static MethodInfo? _partModelDynamicPrefix;
    private static MethodInfo? _partModelGlassPrefix;

    public static void Apply(Harmony harmony)
    {
        _partModelPrefix        = typeof(ShinyPatches).GetMethod(nameof(PartModelModulePrefix),        BindingFlags.NonPublic | BindingFlags.Static)!;
        _partModelDynamicPrefix = typeof(ShinyPatches).GetMethod(nameof(PartModelDynamicModulePrefix), BindingFlags.NonPublic | BindingFlags.Static)!;
        _partModelGlassPrefix   = typeof(ShinyPatches).GetMethod(nameof(PartModelGlassModulePrefix),   BindingFlags.NonPublic | BindingFlags.Static)!;

        _partModelUpdateRenderData        = AccessTools.Method(typeof(PartModelModule),        nameof(PartModelModule.UpdateRenderData));
        _partModelDynamicUpdateRenderData = AccessTools.Method(typeof(PartModelDynamicModule), nameof(PartModelDynamicModule.UpdateRenderData));
        _partModelGlassUpdateRenderData   = AccessTools.Method(typeof(PartModelGlassModule),   nameof(PartModelGlassModule.UpdateRenderData));

        harmony.Patch(_partModelUpdateRenderData,        prefix: new HarmonyMethod(_partModelPrefix));
        harmony.Patch(_partModelDynamicUpdateRenderData, prefix: new HarmonyMethod(_partModelDynamicPrefix));
        harmony.Patch(_partModelGlassUpdateRenderData,   prefix: new HarmonyMethod(_partModelGlassPrefix));

        Console.WriteLine("its-so-shiny.lib: render-skip patches applied");
    }

    public static void Remove(Harmony harmony)
    {
        if (_partModelUpdateRenderData != null && _partModelPrefix != null)
            harmony.Unpatch(_partModelUpdateRenderData, _partModelPrefix);
        if (_partModelDynamicUpdateRenderData != null && _partModelDynamicPrefix != null)
            harmony.Unpatch(_partModelDynamicUpdateRenderData, _partModelDynamicPrefix);
        if (_partModelGlassUpdateRenderData != null && _partModelGlassPrefix != null)
            harmony.Unpatch(_partModelGlassUpdateRenderData, _partModelGlassPrefix);

        _partModelUpdateRenderData        = null;
        _partModelDynamicUpdateRenderData = null;
        _partModelGlassUpdateRenderData   = null;
        _partModelPrefix        = null;
        _partModelDynamicPrefix = null;
        _partModelGlassPrefix   = null;

        Console.WriteLine("its-so-shiny.lib: render-skip patches removed");
    }

    // For shiny_ parts: always render if RenderShinyParts is set, or if the light is currently active.
    // This lets the emissive mesh appear exactly when the light is on and hide when it's off.
    private static bool ShouldRenderShinyPart(Part fullPart)
    {
        if (!ShinyGridManager.IsPixelPart(fullPart)) return true;
        if (ShinyPatchState.RenderShinyParts) return true;
        var ls = fullPart.LightSwitch;
        if (ls == null) return true; // no switch — can't determine state, render to be safe
        return ls.LightIsActive;
    }

    private static bool PartModelModulePrefix(PartModelModule __instance)
        => ShouldRenderShinyPart(__instance.Parent.FullPart);

    private static bool PartModelDynamicModulePrefix(PartModelDynamicModule __instance)
        => ShouldRenderShinyPart(__instance.Parent.FullPart);

    private static bool PartModelGlassModulePrefix(PartModelGlassModule __instance)
        => ShouldRenderShinyPart(__instance.Parent.FullPart);
}
