using System;
using HarmonyLib;
using MeowSci.KsaAbstractions;
using MeowSci.DohLib.Placement;

namespace MeowSci.Doh;

[HarmonyPatch]
internal static class Patcher
{
    private static Harmony? _harmony = new Harmony("doh");

    public static void Patch()
    {
        try
        {
            _harmony?.PatchAll(typeof(Patcher).Assembly);
            if (_harmony != null)
            {
                HotkeyGuard.Patch(_harmony);
                PhysicsFrameHook.Apply(_harmony);
                PlacementInputPatches.Apply(_harmony);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: Error applying patches: {ex.Message}");
        }
    }

    public static void Unload()
    {
        try
        {
            if (_harmony != null)
            {
                HotkeyGuard.Unpatch(_harmony);
                PlacementInputPatches.Remove(_harmony);
                PhysicsFrameHook.Remove(_harmony);
            }
            _harmony?.UnpatchAll("doh");
            _harmony = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: Error removing patches: {ex.Message}");
        }
    }
}
