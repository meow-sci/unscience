using System;
using System.Collections.Generic;
using HarmonyLib;
using KSA;

namespace MeowSci.PyroLib;

/// <summary>Catalog of the game's registered <see cref="VolumetricExhaustTemplate"/>s.</summary>
public static class PlumeTemplates
{
    // Fallback list (the game's shipped ExhaustAssets.xml) used only if the reflection lookup fails.
    private static readonly string[] KnownIds =
        { "EngineALarge", "EngineAMed", "EngineACompact", PlumeTemplateIds.Auxiliary, "RCS", "MmuRcsVac" };

    private static string[]? _cachedIds;

    /// <summary>Ids of every registered exhaust template. Cached after the first successful lookup.</summary>
    public static string[] GetTemplateIds()
    {
        if (_cachedIds != null) return _cachedIds;
        try
        {
            var list = GetTemplateList();
            if (list != null && list.Count > 0)
            {
                var ids = new List<string>(list.Count);
                foreach (var t in list) ids.Add(t.Id);
                _cachedIds = ids.ToArray();
                return _cachedIds;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"pyro: template list reflection failed, using built-in ids: {ex.Message}");
        }
        var fallback = new List<string>();
        foreach (var id in KnownIds)
            if (VolumetricExhaustTemplate.Get(id) != null) fallback.Add(id);
        _cachedIds = fallback.ToArray();
        return _cachedIds;
    }

    /// <summary>Reads the game's internal <c>VolumetricExhaustTemplate.References</c> collection.</summary>
    public static List<VolumetricExhaustTemplate>? GetTemplateList()
    {
        var field = AccessTools.Field(typeof(VolumetricExhaustTemplate), "References");
        var collection = field?.GetValue(null) as SerializedCollection<VolumetricExhaustTemplate>;
        return collection?.GetList();
    }

    public static VolumetricExhaustTemplate? Get(string id) => VolumetricExhaustTemplate.Get(id);

    /// <summary>Normalizes data saved against KSA builds that still shipped Vernier/Turbine templates.</summary>
    public static string NormalizeId(string? id)
    {
        return PlumeTemplateIds.Normalize(id, IsAvailable);
    }

    private static bool IsAvailable(string id)
    {
        try { return Get(id) != null; }
        catch { return false; }
    }

    /// <summary>Creates a fresh game-side instance bound to the template. Returns null if the id is unknown.</summary>
    public static VolumetricExhaustInstance? CreateInstance(string templateId)
    {
        string resolvedId = NormalizeId(templateId);
        var reference = new VolumetricExhaustReference { Id = resolvedId };
        reference.Load();
        if (reference.Template == null) return null;
        return new VolumetricExhaustInstance(reference);
    }
}
