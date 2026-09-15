using System;
using HarmonyLib;
using MeowSci.KsaAbstractions;

namespace MeowSci.DentWizard;

internal static class Patcher
{
    private static Harmony? _harmony;

    public static void Patch()
    {
        try
        {
            _harmony ??= new Harmony("dent-wizard");
            if (_harmony != null)
            {
                HotkeyGuard.Patch(_harmony);
                PhysicsFrameHook.Apply(_harmony);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"dent-wizard: Error applying patches: {ex.Message}");
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
            }
            _harmony = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"dent-wizard: Error removing patches: {ex.Message}");
        }
    }

}
