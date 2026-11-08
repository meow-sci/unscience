using System.Collections.Generic;
using System.Linq;
using MeowSci.WarPaintLib.Materials;

namespace MeowSci.WarPaintLib;

/// <summary>Rebind a detached character's private fur clone after native source-slot allocation.</summary>
internal static class KittenMaterialReuse
{
    internal static bool TryRemapFur(IReadOnlyList<KittenMaterialBinding> bindings, KittenMaterialSet materials)
    {
        int[] nonFur = bindings.Where(b => b.Source != "Fur").SelectMany(b => b.OriginalIndices).ToArray();
        var furSlots = bindings.Where(b => b.Source == "Fur")
            .SelectMany(b => b.OriginalIndices.Select((handle, slot) => (Handle: handle, Slot: slot)))
            .Where(p => p.Handle >= 0 && !nonFur.Contains(p.Handle)).DistinctBy(p => p.Handle).ToArray();
        var savedFur = materials.Materials.Where(m => m.Source == "Fur").ToArray();
        if (!nonFur.Where(h => h >= 0).All(h => materials.HandleMap.TryGetValue(h, out int clone)
                && savedFur.All(f => f.Handle != clone))
            || furSlots.Length != savedFur.Length
            || !furSlots.All(p => savedFur.Count(f => f.Slot == p.Slot) == 1)) return false;

        // Validate completely before changing a reusable set. A mismatch must leave the old
        // recipe intact so its owner can release it safely or try another reconstructed target.
        foreach (int oldSource in materials.HandleMap.Where(p => savedFur.Any(f => f.Handle == p.Value))
            .Select(p => p.Key).ToArray()) materials.HandleMap.Remove(oldSource);
        foreach (var slot in furSlots)
            materials.HandleMap[slot.Handle] = savedFur.Single(f => f.Slot == slot.Slot).Handle;
        return true;
    }
}
