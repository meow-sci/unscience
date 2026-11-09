using System;
using KSA;

namespace MeowSci.DohLib.Spawning;

public sealed partial class KittenSpawner
{
    /// <summary>
    /// Rolls back a failed constructor, which registers its base Vehicle before building the avatar.
    /// This is only for partially constructed objects; live kittens use the native input queue.
    /// </summary>
    internal static void RemoveOrphanedKitten(CelestialSystem system, string kittenName,
        KittenRosterEntryData? createdRosterEntry = null)
    {
        try
        {
            if (!system.All.TryGet(kittenName, out Astronomical? orphan) || orphan is not KittenEva) return;
            if (orphan is KittenEva kitten)
            {
                try
                {
                    WaitForVehicleSolverIdle();
                    JobSystems.NearestOrbitAndPerformanceWorker?.Wait();
                    // A rejected name must never end an unrelated roster entry's mission.
                    Universe.DestroyVehicle(kitten, CrewDisposition.Boarded);
                }
                catch (Exception ex) { Console.WriteLine($"doh: Failed to destroy orphaned '{kittenName}': {ex.Message}"); }
            }
            // A half-built avatar may throw before native disposal deregisters it.
            if (system.All.TryGet(kittenName, out Astronomical? remaining) && ReferenceEquals(remaining, orphan))
            {
                try { system.Deregister(orphan); }
                catch (Exception ex) { Console.WriteLine($"doh: Failed to deregister orphaned '{kittenName}': {ex.Message}"); }
            }
            Console.WriteLine($"doh: Removed partially constructed kitten '{kittenName}'");
        }
        finally
        {
            // Remove only this transaction's new row, never any earlier crew/history entry.
            if (createdRosterEntry != null) Universe.KittenRoster.Kittens.Remove(createdRosterEntry);
        }
    }

    // Constructing a Vehicle mutates the shapes registry held by the background physics step.
    private static void WaitForVehicleSolverIdle() => JobSystems.VehicleSolver?.Wait();
}
