using System;
using HarmonyLib;

namespace MeowSci.IronManLib;

/// <summary>Passive hooks; changes are gated by explicit session activation or saved anchor metadata.</summary>
public static class IronManPatches
{
    public static bool Ready { get; private set; }

    public static void Apply(Harmony harmony)
    {
        try
        {
            IronManConnectorPatches.Apply(harmony);
            IronManEditorPatches.Apply(harmony);
            IronManFlightPatches.Apply(harmony);
            IronManFlightComputerPatches.Apply(harmony);
            IronManControlFramePatches.Apply(harmony);
            IronManEditorOrientationPatches.Apply(harmony);
            IronManRcsOrientationPatches.Apply(harmony);
            Ready = true;
        }
        catch
        {
            Remove(harmony);
            throw;
        }
    }

    public static void Remove(Harmony harmony)
    {
        Ready = false;
        RemoveSafely(() => IronManRcsOrientationPatches.Remove(harmony));
        RemoveSafely(() => IronManEditorOrientationPatches.Remove(harmony));
        RemoveSafely(() => IronManControlFramePatches.Remove(harmony));
        RemoveSafely(() => IronManFlightComputerPatches.Remove(harmony));
        if (!IronManConnectorUnload.PrepareForUnload())
        {
            Console.WriteLine("iron-man: retaining passive hooks because attached nodes could not be safely converted for unload");
            return;
        }
        RemoveSafely(() => IronManFlightPatches.Remove(harmony));
        RemoveSafely(() => IronManEditorPatches.Remove(harmony));
        RemoveSafely(() => IronManConnectorPatches.Remove(harmony));
    }

    private static void RemoveSafely(Action action)
    {
        try { action(); }
        catch (Exception ex) { Console.WriteLine($"iron-man: removing patches: {ex}"); }
    }
}
