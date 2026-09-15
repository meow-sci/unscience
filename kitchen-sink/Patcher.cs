using System;
using HarmonyLib;
using MeowSci.KitchenSinkLib;
using MeowSci.KsaAbstractions;

namespace MeowSci.KitchenSink;

internal static class Patcher
{
    private static Harmony? _harmony = new Harmony("kitchen-sink");

    public static void Patch()
    {
        try
        {
            if (_harmony != null)
            {
                HotkeyGuard.Patch(_harmony);
                IvaForceRender.Patch(_harmony);
                GLoadProtectionPatches.Apply(_harmony);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"kitchen-sink: Error applying patches: {ex.Message}");
        }
    }

    public static void Unload()
    {
        try
        {
            if (_harmony != null)
            {
                HotkeyGuard.Unpatch(_harmony);
                GLoadProtectionPatches.Remove(_harmony);
                IvaForceRender.Unpatch(_harmony);
            }
            _harmony?.UnpatchAll("kitchen-sink");
            _harmony = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"kitchen-sink: Error removing patches: {ex.Message}");
        }
    }
}
