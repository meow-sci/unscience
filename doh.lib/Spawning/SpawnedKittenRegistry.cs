using System.Collections.Generic;
using System.Linq;
using MeowSci.DohLib.Materials;

namespace MeowSci.DohLib.Spawning;

/// <summary>
/// Registry of all kittens spawned by the doh mod.
/// Maintains references for despawning, recoloring, and enumeration.
/// </summary>
public sealed class SpawnedKittenRegistry
{
    private readonly Dictionary<string, SpawnedKittenEntry> _kittens = new();

    /// <summary>All tracked kitten IDs.</summary>
    public IReadOnlyCollection<string> KittenIds => _kittens.Keys;

    /// <summary>Number of tracked kittens.</summary>
    public int Count => _kittens.Count;

    /// <summary>Registers a newly spawned kitten.</summary>
    public void Register(string kittenId, string characterId, KittenMaterialSet? materialSet)
    {
        _kittens[kittenId] = new SpawnedKittenEntry
        {
            KittenId = kittenId,
            CharacterId = characterId,
            Vehicle = MeowSci.KsaAbstractions.VehicleProvider.FindVehicle(kittenId),
            MaterialSet = materialSet
        };
    }

    /// <summary>Unregisters a kitten (after despawn).</summary>
    public void Unregister(string kittenId)
    {
        _kittens.Remove(kittenId);
    }

    /// <summary>Gets entry for a specific kitten.</summary>
    public SpawnedKittenEntry? Get(string kittenId)
    {
        return _kittens.TryGetValue(kittenId, out var entry) ? entry : null;
    }

    /// <summary>Lists all tracked kittens.</summary>
    public IReadOnlyList<SpawnedKittenEntry> GetAll()
    {
        return _kittens.Values.ToList();
    }

    /// <summary>True when any tracked kitten still renders with the given material set.</summary>
    public bool IsMaterialSetInUse(KittenMaterialSet set)
    {
        return _kittens.Values.Any(k => ReferenceEquals(k.MaterialSet, set));
    }

    /// <summary>
    /// Entries whose kitten the game has already disposed, or null when there are none.
    /// Allocation-free in the common case, so it is safe to call every frame.
    /// </summary>
    public List<SpawnedKittenEntry>? GetDisposed()
    {
        List<SpawnedKittenEntry>? disposed = null;
        foreach (var entry in _kittens.Values)
        {
            if (entry.Vehicle is { IsDisposed: true })
                (disposed ??= new List<SpawnedKittenEntry>()).Add(entry);
        }
        return disposed;
    }

    /// <summary>Clears all entries.</summary>
    public void Clear()
    {
        _kittens.Clear();
    }
}

public sealed class SpawnedKittenEntry
{
    /// <summary>Runtime identity retained so a renamed kitten still captures its current native save ID.</summary>
    public KSA.Vehicle? Vehicle { get; init; }
    public string KittenId { get; init; } = "";
    public string CharacterId { get; init; } = "";
    public KittenMaterialSet? MaterialSet { get; init; }
}
