using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using KSA;

namespace MeowSci.TheTickLib;

/// <summary>
/// Live registry of indestructible vehicles, keyed by exact object identity. Read by the UI and by
/// physics workers through <see cref="TheTickPatches"/>; save replay rebinds it after reconstruction.
/// </summary>
public static class TickProtection
{
    /// <summary>
    /// Effective crash tolerance reported for every part of a protected vehicle. The largest finite
    /// double keeps downstream arithmetic (dent depth = pressure / tolerance) finite instead of NaN.
    /// </summary>
    public const double UnbreakableTolerancePascals = double.MaxValue;

    private static readonly ConcurrentDictionary<Vehicle, byte> Vehicles =
        new(ReferenceEqualityComparer.Instance);

    public static Vehicle[] Snapshot() => Vehicles.Keys.ToArray();
    public static bool Contains(Vehicle vehicle) => !vehicle.IsDisposed && Vehicles.ContainsKey(vehicle);
    public static bool Add(Vehicle vehicle) => !vehicle.IsDisposed && Vehicles.TryAdd(vehicle, 0);
    public static bool Remove(Vehicle vehicle) => Vehicles.TryRemove(vehicle, out _);
    public static void Clear() => Vehicles.Clear();

    /// <summary>Drops disposed vehicles and vehicles no longer present in the live world.</summary>
    public static void Prune(IEnumerable<Vehicle> liveVehicles)
    {
        if (Vehicles.IsEmpty) return;
        var live = new HashSet<Vehicle>(liveVehicles, ReferenceEqualityComparer.Instance);
        foreach (var vehicle in Vehicles.Keys)
            if (vehicle.IsDisposed || !live.Contains(vehicle))
                Remove(vehicle);
    }
}
