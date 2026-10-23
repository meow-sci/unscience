using System;
using System.Linq;
using KSA;
using MeowSci.DohLib.Materials;
using MeowSci.KsaAbstractions;

namespace MeowSci.DohLib.Spawning;

/// <summary>Despawn, prune and native destruction of doh-spawned kittens.</summary>
public sealed partial class KittenSpawner
{
    /// <summary>Despawns a kitten by ID and releases its materials when no other kitten shares them.</summary>
    public bool Despawn(string kittenId)
    {
        try
        {
            var entry = _registry.Get(kittenId);
            if (entry == null) return false;

            // Use the kitten object captured at registration so a renamed kitten still resolves;
            // never fall back to a non-kitten vehicle that merely shares the name.
            var kitten = entry.Vehicle as KittenEva ?? VehicleProvider.FindVehicle(kittenId) as KittenEva;
            if (kitten != null && !kitten.IsDisposed)
                DestroyKitten(kitten);

            // Also reached when the game already destroyed the kitten, so the entry and its
            // GPU material slots are never stranded.
            _registry.Unregister(kittenId);
            ReleaseIfUnused(entry.MaterialSet);
            Console.WriteLine($"doh: Despawned '{kittenId}'");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: Despawn error for '{kittenId}': {ex.Message}");
            return false;
        }
    }

    /// <summary>Despawns all kittens spawned by this mod.</summary>
    public void DespawnAll()
    {
        var ids = _registry.KittenIds.ToList();
        foreach (var id in ids)
            Despawn(id);
    }

    /// <summary>
    /// Drops registry entries whose kitten the game already destroyed (e.g. it boarded a vehicle)
    /// and returns their GPU material slots to the pool. Returns the number of entries pruned.
    /// </summary>
    public int PruneDisposed()
    {
        var disposed = _registry.GetDisposed();
        if (disposed == null) return 0;

        foreach (var entry in disposed)
        {
            _registry.Unregister(entry.KittenId);
            ReleaseIfUnused(entry.MaterialSet);
        }
        Console.WriteLine($"doh: Pruned {disposed.Count} kitten(s) the game already removed");
        return disposed.Count;
    }

    private void ReleaseIfUnused(KittenMaterialSet? set)
    {
        if (set != null && !_registry.IsMaterialSetInUse(set))
            _materialFactory.Release(set);
    }

    /// <summary>
    /// Removes a kitten through the game's own teardown, as KSA does for boarding (rev 5607).
    /// <c>Universe.DestroyVehicle</c> hands off cameras following it (including the character-portrait
    /// camera, rev 5486), clears it as the controlled vehicle and as other vessels' target, moves its
    /// particle emitters to the parent body, and then disposes it, which also deregisters it.
    /// <see cref="CrewDisposition.Boarded"/> is the only disposition that leaves crew records alone:
    /// EndMission/Kill would end or kill a roster entry with the kitten's name, but doh kittens are not crew.
    /// </summary>
    private static void DestroyKitten(Vehicle kitten)
    {
        // Dispose releases physics shapes (same registry lock as spawn), so the vehicle solver must be
        // idle. KSA destroys vehicles in PrepareFrame before it queues the nearest-orbit hover job, which
        // walks CurrentSystem.All; this runs from the UI pass, after that job was queued.
        WaitForVehicleSolverIdle();
        JobSystems.NearestOrbitAndPerformanceWorker?.Wait();
        Universe.DestroyVehicle(kitten, CrewDisposition.Boarded);
    }

    /// <summary>
    /// The Vehicle base constructor registers the kitten in system.All before KittenEva builds its
    /// renderable, so a throw there leaves a registered kitten without a renderable, which the game
    /// then updates and draws every frame. Remove it again.
    /// </summary>
    internal static void RemoveOrphanedKitten(CelestialSystem system, string kittenName)
    {
        if (!system.All.TryGet(kittenName, out Astronomical? orphan) || orphan == null) return;

        if (orphan is Vehicle vehicle)
        {
            try { DestroyKitten(vehicle); }
            catch (Exception ex) { Console.WriteLine($"doh: Failed to destroy orphaned '{kittenName}': {ex.Message}"); }
        }

        // The teardown can throw on the half-built kitten before its Dispose deregisters it.
        if (system.All.TryGet(kittenName, out Astronomical? remaining) && ReferenceEquals(remaining, orphan))
        {
            try { system.Deregister(orphan); }
            catch (Exception ex) { Console.WriteLine($"doh: Failed to deregister orphaned '{kittenName}': {ex.Message}"); }
        }
        Console.WriteLine($"doh: Removed partially constructed kitten '{kittenName}'");
    }

    /// <summary>
    /// Blocks until the game's background vehicle physics step (VehicleUpdateTask, kicked
    /// from Program.PrepareFrame and running concurrently with the UI pass) has finished.
    /// Constructing or disposing a Vehicle mutates the shared shapes registry, which the
    /// game locks while that step runs. PrepareFrame would wait on this same scheduler
    /// next frame anyway, so this only moves the wait earlier.
    /// </summary>
    private static void WaitForVehicleSolverIdle()
    {
        JobSystems.VehicleSolver?.Wait();
    }
}
