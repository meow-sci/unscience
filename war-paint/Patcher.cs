using System;
using HarmonyLib;
using MeowSci.KsaAbstractions;
using MeowSci.WarPaintLib;

namespace MeowSci.WarPaint;

[HarmonyPatch]
internal static class Patcher
{
    private static Harmony? _harmony;

    public static void Patch()
    {
        try
        {
            _harmony ??= new Harmony("war-paint");
            HotkeyGuard.Patch(_harmony);
            KittenVisorPatches.Apply(_harmony);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"war-paint: Error applying patches: {ex.Message}");
        }
    }

    public static void Unload()
    {
        try
        {
            if (_harmony != null)
            {
                HotkeyGuard.Unpatch(_harmony);
                KittenVisorPatches.Remove(_harmony);
            }
            _harmony?.UnpatchAll("war-paint");
            _harmony = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"war-paint: Error removing patches: {ex.Message}");
        }
    }

}
