using System;

namespace MeowSci.PyroLib;

/// <summary>Stable template ID migration rules for saved Pyro data.</summary>
public static class PlumeTemplateIds
{
    public const string Auxiliary = "EngineAAuxiliary";

    private static readonly string[] LegacyAliases = { "EngineAVernier", "EngineATurbine" };

    /// <summary>
    /// Retains a legacy ID when a content mod still provides it. Otherwise maps KSA's removed IDs to the
    /// verified replacement. Unknown IDs are returned unchanged so callers can report an explicit failure.
    /// </summary>
    public static string Normalize(string? id, Func<string, bool> isAvailable)
    {
        string candidate = string.IsNullOrWhiteSpace(id) ? "EngineALarge" : id.Trim();
        if (isAvailable(candidate)) return candidate;
        foreach (string legacy in LegacyAliases)
        {
            if (string.Equals(candidate, legacy, StringComparison.Ordinal) && isAvailable(Auxiliary))
                return Auxiliary;
        }
        return candidate;
    }

    public static bool IsLegacyAlias(string? id)
    {
        return string.Equals(id, LegacyAliases[0], StringComparison.Ordinal)
            || string.Equals(id, LegacyAliases[1], StringComparison.Ordinal);
    }
}
