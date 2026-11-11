using System;
using HarmonyLib;
using MeowSci.KsaAbstractions;
using MeowSci.YouDidntSeeAnythingLib;

namespace MeowSci.YouDidntSeeAnything;

internal static class Patcher
{
    private static Harmony? _harmony;
    public static void Patch()
    {
        _harmony = new Harmony("you-didnt-see-anything");
        try { HotkeyGuard.Patch(_harmony); HiddenUiVisibilityPatches.Apply(_harmony); }
        catch { Unload(); throw; }
    }

    public static void Unload()
    {
        if (_harmony == null) return;
        try { HiddenUiVisibilityPatches.Remove(_harmony); }
        catch (Exception ex) { Console.WriteLine($"you-didnt-see-anything: Patch removal failed: {ex}"); }
        try { HotkeyGuard.Unpatch(_harmony); }
        finally { _harmony = null; }
    }
}
