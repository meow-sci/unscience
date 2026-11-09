using System;
using KSA;

namespace MeowSci.DohLib.Spawning;

public sealed partial class KittenSpawner
{
    /// <summary>
    /// Rolls back a failed constructor, which registers its base Vehicle before building the avatar.
    /// This is only for partially constructed objects; live kittens use the native input queue.
    /// </summary>
    internal static void RemoveOrphanedKitten(CelestialSystem system, string kittenName)
    {
        if (!system.All.TryGet(kittenName, out Astronomical? orphan) || orphan == null) return;
        if (orphan is KittenEva kitten)
        {
            try
            {
                WaitForVehicleSolverIdle();
                JobSystems.NearestOrbitAndPerformanceWorker?.Wait();
                Universe.DestroyVehicle(kitten);
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

    // Constructing a Vehicle mutates the shapes registry held by the background physics step.
    private static void WaitForVehicleSolverIdle() => JobSystems.VehicleSolver?.Wait();
}
