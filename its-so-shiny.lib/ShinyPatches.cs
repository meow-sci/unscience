using System;
using HarmonyLib;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.ItsSoShinyLib;

/// <summary>Harmony patch helpers for its-so-shiny render-skip behaviour on shiny light parts.</summary>
public static class ShinyPatches
{
    private const string FilterOwner = "its-so-shiny";

    /// <summary>
    /// Hides shiny light-part meshes while their light is off. Parts stay in the part tree; only
    /// their instances are dropped from the per-frame part instance lists (KSA 5482 removed the
    /// per-module render hooks).
    /// </summary>
    public static void Apply(Harmony harmony)
    {
        PartRenderFilter.Register(harmony, FilterOwner, fullPart => !ShouldRenderShinyPart(fullPart));
        Console.WriteLine("its-so-shiny.lib: render-skip patches applied");
    }

    public static void Remove(Harmony harmony)
    {
        PartRenderFilter.Unregister(harmony, FilterOwner);
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
}
