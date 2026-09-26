using System;
using System.Linq;

namespace MeowSci.DohLib.Materials;

/// <summary>
/// Naming and release of doh-owned GPU materials. The game's material pool is fixed-size
/// (see MaterialSystemAccessor.Pool.cs), so every cloned set must be released once no
/// spawned kitten renders with it any more.
/// </summary>
public sealed partial class MaterialFactory
{
    /// <summary>Asset name of the cloned fur material for a set prefix.</summary>
    public static string FurAssetName(string prefix) => $"{prefix}_fur";

    /// <summary>
    /// Returns a set's GPU material slots to the game's pool. Idempotent. The caller must ensure
    /// no live kitten still renders with the set.
    /// </summary>
    public void Release(KittenMaterialSet set)
    {
        if (set.IsReleased) return;

        int freed = 0;
        foreach (string name in set.AssetNames)
        {
            if (MaterialSystemAccessor.DestroyMaterial(name))
                freed++;
        }

        set.IsReleased = true;
        _createdSets.Remove(set);
        Console.WriteLine($"doh: Released material set '{set.Id}' ({freed}/{set.AssetNames.Count} GPU slots freed)");
    }

    /// <summary>Releases every material set this factory created. Call on unload.</summary>
    public void Cleanup()
    {
        foreach (var set in _createdSets.ToArray())
            Release(set);
        _createdSets.Clear();
    }

    /// <summary>
    /// Next unused set prefix. Skips names still present in the pool (for example left behind by
    /// an earlier instance of the mod), which CreateObject would otherwise reject.
    /// </summary>
    private string NextPrefix()
    {
        string prefix;
        do
        {
            prefix = $"doh_{_nextMaterialId++:D4}";
        } while (MaterialSystemAccessor.GetExistingMaterialHandle($"{prefix}_m0") >= 0
            || MaterialSystemAccessor.GetExistingMaterialHandle(FurAssetName(prefix)) >= 0);
        return prefix;
    }
}
