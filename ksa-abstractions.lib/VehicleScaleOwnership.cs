using System.Runtime.CompilerServices;
using KSA;

namespace MeowSci.KsaAbstractions;

/// <summary>Prevents two live tools from overwriting the same vessel's scale.</summary>
public static class VehicleScaleOwnership
{
    private sealed record Owner(string Name);
    private static readonly ConditionalWeakTable<Vehicle, Owner> Owners = new();
    public static string? GetOwner(Vehicle vehicle) => Owners.TryGetValue(vehicle, out var owner) ? owner.Name : null;
    public static bool TryAcquire(Vehicle vehicle, string name)
    {
        if (Owners.TryGetValue(vehicle, out var owner)) return owner.Name == name;
        Owners.Add(vehicle, new(name));
        return true;
    }
    public static void Release(Vehicle vehicle, string name)
    {
        if (GetOwner(vehicle) == name) Owners.Remove(vehicle);
    }
}
