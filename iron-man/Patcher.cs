using System;
using HarmonyLib;
using MeowSci.IronManLib;
using MeowSci.KsaAbstractions;

namespace MeowSci.IronMan;

internal static class Patcher
{
    private static Harmony? _harmony;

    public static void Patch()
    {
        _harmony = new Harmony("MeowSci.IronMan");
        try
        {
            HotkeyGuard.Patch(_harmony);
            PhysicsFrameHook.Apply(_harmony);
            IronManPatches.Apply(_harmony);
        }
        catch { Unload(); throw; }
    }

    public static void Unload()
    {
        if (_harmony == null) return;
        try
        {
            IronManPatches.Remove(_harmony);
            PhysicsFrameHook.Remove(_harmony);
            HotkeyGuard.Unpatch(_harmony);
        }
        catch (Exception ex) { Console.WriteLine($"iron-man: unpatch: {ex}"); }
        finally { _harmony = null; }
    }
}
