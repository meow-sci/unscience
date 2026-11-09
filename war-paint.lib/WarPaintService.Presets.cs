using System;
using System.Collections.Generic;
using System.Linq;
using KSA;

namespace MeowSci.WarPaintLib;

public sealed partial class WarPaintService
{
    public ActivePaint? ApplyPreset(KittenEva kitten, string name) => Presets.TryGet(name, out var settings)
        ? Apply(kitten, settings, name) : Fail($"Paint preset unavailable: {name}.");

    /// <summary>Preview a draft on every current assignment, keeping each target's private materials.</summary>
    public bool PreviewPreset(string name, PaintSettings settings)
    {
        var errors = new List<string>();
        foreach (var paint in GetActivePaints().Where(p => p.PresetName == name))
            if (Apply(paint.Kitten, settings, name) == null)
                errors.Add($"{paint.VehicleId}: {LastError}");
        LastError = errors.Count == 0 ? null : string.Join("\n", errors);
        return errors.Count == 0;
    }

    /// <summary>Persist deletion before removing assignments; a failed disk write leaves paint intact.</summary>
    public bool DeletePreset(string name)
    {
        LastError = null;
        if (!Presets.Delete(name))
        { LastError = Presets.LastError ?? $"Paint preset unavailable: {name}."; return false; }
        foreach (var paint in _active.Values.Where(p => p.PresetName == name).ToArray()) Unset(paint.Kitten);
        return true;
    }

    /// <summary>Give old API/save recipes stable names, without replacing any existing library entry.</summary>
    public string? ImportPreset(PaintSettings settings, string preferredName)
    {
        foreach (string existing in Presets.Names)
            if (Presets.TryGet(existing, out var recipe) && recipe.EquivalentTo(settings)) return existing;
        string stem = preferredName.Length > 110 ? preferredName[..110] : preferredName;
        string name = stem;
        for (int suffix = 1; Presets.Names.Contains(name); suffix++) name = $"{stem} ({suffix})";
        return Presets.Save(name, settings) ? name : null;
    }

    /// <summary>
    /// Describe material roles using original native bindings, without allocating or painting a kitten.
    /// Existing preset rows win; newly discovered roles inherit the preset's whole-material tint.
    /// </summary>
    public PaintSettings DescribeMaterials(PaintSettings settings)
    {
        var result = settings.Clone();
        // Startup discovery may run before the native material bridge is available. Retry its
        // idempotent initialization too, rather than only retrying a permanently empty asset map.
        if (_shutdown || !_backend.Initialize()) return result;
        var roles = result.Materials.Where(m => m.Slot >= 0).Select(m => (m.Source, m.Slot)).ToHashSet();
        foreach (var kitten in VehicleKittens())
        {
            var bindings = _active.TryGetValue(kitten, out var paint) ? paint.Bindings : _backend.CaptureBindings(kitten);
            if (bindings == null) continue;
            var seen = new HashSet<int>();
            foreach (var binding in bindings)
                for (int slot = 0; slot < binding.OriginalIndices.Length; slot++)
                {
                    int handle = binding.OriginalIndices[slot];
                    if (handle < 0 || !seen.Add(handle)) continue;
                    if (roles.Contains((binding.Source, slot))) continue;
                    string? name = binding.Source == "Fur" ? "CatFur" : _backend.GetMaterialName(handle);
                    if (name == null || !roles.Add((binding.Source, slot))) continue;
                    // A legacy name-addressed row already describes this slot.
                    if (result.Materials.Any(m => m.Source == binding.Source && m.Slot < 0 && m.Name == name)) continue;
                    result.Materials.Add(new() { Source = binding.Source, Name = name, Slot = slot, Color = settings.Tint });
                }
        }
        return result;
    }

    internal static IEnumerable<KittenEva> VehicleKittens() => MeowSci.KsaAbstractions.VehicleProvider
        .GetAllVehicles(includeDebris: true).OfType<KittenEva>().Where(IsCurrent)
        .OrderBy(k => k.Id, StringComparer.Ordinal);
}
