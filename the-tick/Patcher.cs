using System;
using HarmonyLib;
using MeowSci.KsaAbstractions;
using MeowSci.TheTickLib;

namespace MeowSci.TheTick;

internal static class Patcher
{
    private static Harmony? _harmony = new Harmony("the-tick");

    public static void Patch()
    {
        try
        {
            if (_harmony != null)
            {
                HotkeyGuard.Patch(_harmony);
                TheTickPatches.Apply(_harmony);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"the-tick: Error applying patches: {ex.Message}");
        }
    }

    public static void Unload()
    {
        try
        {
            if (_harmony != null)
            {
                HotkeyGuard.Unpatch(_harmony);
                TheTickPatches.Remove(_harmony);
            }
            _harmony?.UnpatchAll("the-tick");
            _harmony = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"the-tick: Error removing patches: {ex.Message}");
        }
    }
}
