using System;
using HarmonyLib;
using MeowSci.KsaAbstractions;
using MeowSci.HumbleArteestLib;

namespace MeowSci.HumbleArteest;

[HarmonyPatch]
internal static class Patcher
{
    private static Harmony? _harmony;

    public static void Patch()
    {
        try
        {
            _harmony = new Harmony("humble-arteest");
            HotkeyGuard.Patch(_harmony);
            VehiclePaintPatches.Apply(_harmony);
            EngineEmissivePatches.Apply(_harmony);
            Console.WriteLine("humble-arteest: Harmony patches applied");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"humble-arteest: Error applying patches: {ex.Message}");
        }
    }

    public static void Unload()
    {
        try
        {
            VehiclePaint.Cleanup();
            EngineEmissive.Cleanup();

            if (_harmony != null)
            {
                EngineEmissivePatches.Remove(_harmony);
                VehiclePaintPatches.Remove(_harmony);
                HotkeyGuard.Unpatch(_harmony);
            }
            _harmony = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"humble-arteest: Error removing patches: {ex.Message}");
        }
    }
}
