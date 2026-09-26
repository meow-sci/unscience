using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;

namespace MeowSci.DohLib.Materials;

/// <summary>Per-material tracking for individual color editing.</summary>
public sealed class MaterialEntry
{
    public string Name { get; init; } = "";
    public string Source { get; set; } = "";
    public int Handle { get; init; }
    public float4 Color { get; set; }

    /// <summary>Write this material's individual color to the GPU.</summary>
    public bool ApplyColor()
    {
        if (Handle < 0) return false;
        return MaterialSystemAccessor.WriteAlbedoColor(Handle, Color);
    }
}

/// <summary>
/// Holds per-kitten GPU material handles created by MaterialFactory.
/// Every material the kitten uses is cloned to a unique GPU slot so
/// tinting one kitten doesn't affect others.
/// </summary>
public sealed class KittenMaterialSet
{
    /// <summary>Unique identifier for this material set (e.g., "doh_0042").</summary>
    public string Id { get; }

    /// <summary>The AlbedoColor tint applied to all materials.</summary>
    public float4 TintColor { get; private set; }

    /// <summary>GPU material handle for the body mesh.</summary>
    public int BodyMaterialHandle { get; init; }

    /// <summary>GPU material handle for the head mesh.</summary>
    public int HeadMaterialHandle { get; init; }

    /// <summary>GPU material handle for the eye mesh.</summary>
    public int EyeMaterialHandle { get; init; }

    /// <summary>GPU material handle for the fur mesh.</summary>
    public int FurMaterialHandle { get; init; }

    /// <summary>
    /// Maps old shared GPU handle → new unique per-kitten handle.
    /// Used by ApplyMaterialSetToKitten to replace every entry in MaterialIndices.
    /// </summary>
    public Dictionary<int, int> HandleMap { get; set; } = new();

    /// <summary>
    /// All unique per-kitten material handles created for this kitten.
    /// Used for recoloring — WriteAlbedoColor is called on each.
    /// </summary>
    public List<int> AllMaterialHandles { get; } = new();

    /// <summary>Individual material entries for per-material color editing.</summary>
    public List<MaterialEntry> Materials { get; } = new();

    /// <summary>
    /// GPU material asset names this set owns in the game's fixed-size material pool.
    /// MaterialFactory.Release frees exactly these.
    /// </summary>
    public List<string> AssetNames { get; } = new();

    /// <summary>True once the GPU slots were returned to the pool; a released set must never be reused.</summary>
    public bool IsReleased { get; internal set; }

    public KittenMaterialSet(string id, float4 tintColor)
    {
        Id = id;
        TintColor = tintColor;
    }

    /// <summary>
    /// Updates the AlbedoColor tint on ALL unique per-kitten materials.
    /// Writes directly to the GPU buffer for immediate visual update.
    /// </summary>
    public bool UpdateTint(float4 newColor)
    {
        TintColor = newColor;
        bool ok = true;

        if (Materials.Count > 0)
        {
            foreach (var entry in Materials)
            {
                entry.Color = newColor;
                if (entry.Handle >= 0)
                    ok &= MaterialSystemAccessor.WriteAlbedoColor(entry.Handle, newColor);
            }
        }
        else
        {
            foreach (int handle in AllMaterialHandles)
            {
                if (handle >= 0)
                    ok &= MaterialSystemAccessor.WriteAlbedoColor(handle, newColor);
            }
        }

        return ok;
    }
}
