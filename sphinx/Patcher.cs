using System;
using HarmonyLib;
using Brutal.Numerics;
using KSA;
using MeowSci.SphinxLib;
using MeowSci.KsaAbstractions;

namespace MeowSci.Sphinx;

[HarmonyPatch]
internal static class Patcher
{
    private static Harmony? _harmony = new Harmony("sphinx");

    public static void Patch()
    {
        try
        {
            _harmony?.PatchAll(typeof(Patcher).Assembly);
            if (_harmony != null) { HotkeyGuard.Patch(_harmony); SphinxPatches.Apply(_harmony); }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"sphinx: Error applying patches: {ex.Message}");
        }
    }

    public static void Unload()
    {
        try
        {
            if (_harmony != null) { SphinxPatches.Remove(_harmony); HotkeyGuard.Unpatch(_harmony); }
            _harmony?.UnpatchAll("sphinx");
            _harmony = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"sphinx: Error removing patches: {ex.Message}");
        }
    }

}
