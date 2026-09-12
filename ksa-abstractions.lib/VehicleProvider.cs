using System.Collections.Generic;
using System.Linq;
using KSA;

namespace MeowSci.KsaAbstractions;

/// <summary>Static helpers to get vehicles — wraps Program.ControlledVehicle and Universe.CurrentSystem.All.</summary>
public static class VehicleProvider
{
    /// <summary>Returns the currently player-controlled vehicle, or null if none.</summary>
    public static Vehicle? GetControlledVehicle() => Program.ControlledVehicle;

    /// <summary>
    /// Returns the vehicles in the current system, or an empty list if unavailable.
    /// </summary>
    /// <param name="includeDebris">
    /// KSA 2026.9.7.5402 added structural part failure, which sheds fragments as real
    /// <see cref="Vehicle"/> objects flagged <c>IsDebris</c>. They live in the same system
    /// collection as crewed craft, so they would otherwise fill every mod's vehicle picker.
    /// They are excluded by default; pass true when debris is a legitimate target.
    /// </param>
    public static List<Vehicle> GetAllVehicles(bool includeDebris = false) =>
        Universe.CurrentSystem?.All.UnsafeAsList()
            .OfType<Vehicle>()
            .Where(v => includeDebris || !v.IsDebris)
            .ToList() ?? new List<Vehicle>();

    /// <summary>Finds a vehicle by id, or null if none or more than one matches. Debris is searched too, so an id
    /// held from before a part failure still resolves.</summary>
    public static Vehicle? FindVehicle(string vehicleId)
    {
        Vehicle? match = null;
        foreach (var vehicle in GetAllVehicles(includeDebris: true))
        {
            if (vehicle.Id != vehicleId) continue;
            if (match != null) return null; // Ambiguous identities must never retarget saved edits.
            match = vehicle;
        }
        return match;
    }
}
