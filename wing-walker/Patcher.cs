using System;
using HarmonyLib;
using MeowSci.KsaAbstractions;
using MeowSci.WingWalkerLib;

namespace MeowSci.WingWalker;

internal static class Patcher
{
    private static Harmony? _harmony;

    public static void Patch()
    {
        _harmony = new Harmony("MeowSci.WingWalker");
        try
        {
            HotkeyGuard.Patch(_harmony);
            WingWalkerPatches.Apply(_harmony);
        }
        catch { Unload(); throw; }
    }

    public static void Unload()
    {
        if (_harmony == null) return;
        try { WingWalkerPatches.Remove(_harmony); }
        catch (Exception ex) { Console.WriteLine($"wing-walker: unpatch: {ex}"); }
        finally
        {
            try { HotkeyGuard.Unpatch(_harmony); }
            catch (Exception ex) { Console.WriteLine($"wing-walker: hotkey guard cleanup: {ex}"); }
            _harmony = null;
        }
    }
}
