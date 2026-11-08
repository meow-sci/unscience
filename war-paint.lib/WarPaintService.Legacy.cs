using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.WarPaintLib;

public sealed partial class WarPaintService
{
    /// <summary>
    /// Migrate Humble Arteest's old global material map onto the exact live kittens whose
    /// native bindings used those assets. Shared native materials are never written.
    /// </summary>
    public bool ImportLegacyColors(Dictionary<string, float4> colors, SaveRestoreContext context)
    {
        if (colors.Count == 0) return true;
        if (_shutdown || !_backend.Initialize())
        { context.Warn("Legacy kitten colors could not migrate: material system unavailable."); return false; }
        bool complete = true;
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kitten in VehicleProvider.GetAllVehicles(includeDebris: true).OfType<KittenEva>().Where(k => !k.IsDisposed))
        {
            _active.TryGetValue(kitten, out var active);
            var bindings = active?.Bindings ?? _backend.CaptureBindings(kitten);
            if (bindings == null) continue;
            var recipe = active == null ? new PaintSettings() : Snapshot(kitten)!;
            var matching = new List<(string Name, string Source, int Slot, float4 Color)>();
            var seen = new HashSet<int>();
            foreach (var binding in bindings)
                for (int slot = 0; slot < binding.OriginalIndices.Length; slot++)
                {
                    int handle = binding.OriginalIndices[slot];
                    if (!seen.Add(handle)) continue;
                    string? name = _backend.GetMaterialName(handle);
                    if (name != null && colors.TryGetValue(name, out var color))
                        matching.Add((name, binding.Source, slot, color));
                }
            if (matching.Count == 0) continue;
            foreach (var item in matching)
            {
                recipe.Materials.RemoveAll(m => m.Source == item.Source && m.Slot == item.Slot);
                recipe.Materials.Add(new PaintMaterialSettings
                    { Name = item.Name, Source = item.Source, Slot = item.Slot, Color = item.Color });
            }
            if (Apply(kitten, recipe) == null)
            {
                context.Warn($"Legacy kitten colors could not migrate for {kitten.Id}: {LastError}");
                complete = false;
            }
            else foreach (var item in matching) resolved.Add(item.Name);
        }
        foreach (string missing in colors.Keys.Where(name => !resolved.Contains(name)))
        {
            context.Warn($"Legacy kitten material has no resolved live target: {missing}.");
            complete = false;
        }
        return complete;
    }
}
