using System;
using HarmonyLib;
using MeowSci.KsaAbstractions;

namespace MeowSci.SaveYourself;

internal static class Patcher
{
    private static Harmony? _harmony = new Harmony("save-yourself");

    public static void Patch()
    {
        try
        {
            if (_harmony != null)
                HotkeyGuard.Patch(_harmony);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"save-yourself: Error applying patches: {ex.Message}");
        }
    }

    public static void Unload()
    {
        try
        {
            if (_harmony != null)
                HotkeyGuard.Unpatch(_harmony);
            _harmony?.UnpatchAll("save-yourself");
            _harmony = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"save-yourself: Error removing patches: {ex.Message}");
        }
    }
}
