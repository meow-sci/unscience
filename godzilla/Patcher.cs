using System;
using HarmonyLib;
using Brutal.Numerics;
using KSA;
using MeowSci.GarrysTorchLib;
using MeowSci.KsaAbstractions;

namespace MeowSci.Godzilla;

[HarmonyPatch]
internal static class Patcher
{
    private static Harmony? _harmony = new Harmony("godzilla");

    public static void Patch()
    {
        try
        {
            _harmony?.PatchAll(typeof(Patcher).Assembly);
            if (_harmony != null)
            {
                HotkeyGuard.Patch(_harmony);
                PhysicsFrameHook.Apply(_harmony);
                KittenScalePatches.Apply(_harmony);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"godzilla: Error applying patches: {ex.Message}");
        }
    }

    public static void Unload()
    {
        try
        {
            if (_harmony != null)
            {
                HotkeyGuard.Unpatch(_harmony);
                PhysicsFrameHook.Remove(_harmony);
                KittenScalePatches.Remove(_harmony);
            }
            _harmony?.UnpatchAll("godzilla");
            _harmony = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"godzilla: Error removing patches: {ex.Message}");
        }
    }

}
