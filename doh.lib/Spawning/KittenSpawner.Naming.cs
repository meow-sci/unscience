using System;
using System.Globalization;
using System.Numerics;
using KSA;

namespace MeowSci.DohLib.Spawning;

public sealed partial class KittenSpawner
{
    /// <summary>Preserves available single names; otherwise advances past the highest native suffix.</summary>
    internal string GenerateKittenName(CelestialSystem system, SpawnRequest request)
    {
        bool automatic = string.IsNullOrWhiteSpace(request.VehicleName);
        string prefix = automatic ? SpawnRequest.DefaultNamePrefix : request.VehicleName!;
        BigInteger next = BigInteger.Zero;
        if (request.Count == 1 && !automatic)
        {
            if (!IsOccupied(system, prefix)) return prefix;

            // Retrying "Crew_7" should produce "Crew_8", not "Crew_70". Batch inputs,
            // however, are literal prefixes even when they end in digits.
            int suffixStart = prefix.Length;
            while (suffixStart > 0 && prefix[suffixStart - 1] is >= '0' and <= '9') suffixStart--;
            next = suffixStart < prefix.Length
                ? BigInteger.Parse(prefix.AsSpan(suffixStart), NumberStyles.None, CultureInfo.InvariantCulture) + 1
                : BigInteger.One;
            prefix = prefix[..suffixStart];
        }

        // Native reconstruction, renames and roster-only (including KIA/retired) names all
        // affect the next suffix. Derive it afresh rather than saving or caching a counter.
        foreach (var body in system.All.UnsafeAsList())
            AdvancePastSuffix(body.Id, prefix, ref next);
        foreach (var kitten in Universe.KittenRoster.Kittens)
            AdvancePastSuffix(kitten.Name, prefix, ref next);

        string name;
        do
        {
            name = prefix + next.ToString(CultureInfo.InvariantCulture);
            next++;
        } while (IsOccupied(system, name));
        return name;
    }

    private static void AdvancePastSuffix(string name, string prefix, ref BigInteger next)
    {
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && BigInteger.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var suffix)
            && suffix >= next)
            next = suffix + 1;
    }

    /// <summary>
    /// KSA 5669+ binds an EVA kitten to the roster entry with the same (case-insensitive) name for
    /// radiation dose, sickness and death, so a name held by a rostered kitten at home is taken too.
    /// </summary>
    private static bool IsOccupied(CelestialSystem system, string name)
        => system.All.TryGet(name, out _) || Universe.KittenRoster.Find(name) != null;
}
