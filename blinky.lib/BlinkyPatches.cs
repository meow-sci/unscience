using System;
using HarmonyLib;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.BlinkyLib;

/// <summary>Harmony patch helpers for blinky render-skip behaviour.</summary>
public static class BlinkyPatches
{
    private const string FilterOwner = "blinky";

    /// <summary>
    /// Hides pixel-engine meshes unless <see cref="BlinkyPatchState.RenderPixelParts"/> is set.
    /// Pixel parts stay in the part tree and keep working; only their instances are dropped from
    /// the per-frame part instance lists (KSA 5482 removed the per-module render hooks).
    /// </summary>
    public static void Apply(Harmony harmony)
    {
        PartRenderFilter.Register(harmony, FilterOwner, ShouldHide);
        Console.WriteLine("blinky.lib: patches applied");
    }

    public static void Remove(Harmony harmony)
    {
        PartRenderFilter.Unregister(harmony, FilterOwner);
        Console.WriteLine("blinky.lib: patches removed");
    }

    // Pixel parts: Id starts with "pixel_" or the part is registered in a live grid.
    private static bool ShouldHide(Part fullPart) =>
        !BlinkyPatchState.RenderPixelParts && BlinkyGridManager.IsPixelPart(fullPart);
}
