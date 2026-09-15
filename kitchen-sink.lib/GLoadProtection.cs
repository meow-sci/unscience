using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using KSA;

namespace MeowSci.KitchenSinkLib;

/// <summary>Live vehicle identities shared by the UI and physics workers; save replay rebinds them.</summary>
internal static class GLoadProtection
{
    private static readonly ConcurrentDictionary<Vehicle, byte> Vehicles =
        new(ReferenceEqualityComparer.Instance);

    public static Vehicle[] Snapshot() => Vehicles.Keys.ToArray();
    public static bool Contains(Vehicle vehicle) => !vehicle.IsDisposed && Vehicles.ContainsKey(vehicle);
    public static bool Add(Vehicle vehicle) => !vehicle.IsDisposed && Vehicles.TryAdd(vehicle, 0);
    public static bool Remove(Vehicle vehicle) => Vehicles.TryRemove(vehicle, out _);
    public static void Clear() => Vehicles.Clear();

    public static void Prune(IEnumerable<Vehicle> liveVehicles)
    {
        if (Vehicles.IsEmpty) return;
        var live = new HashSet<Vehicle>(liveVehicles, ReferenceEqualityComparer.Instance);
        foreach (var vehicle in Vehicles.Keys)
            if (vehicle.IsDisposed || !live.Contains(vehicle))
                Remove(vehicle);
    }
}
