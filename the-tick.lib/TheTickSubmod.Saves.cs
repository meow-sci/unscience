using System;
using System.Collections.Generic;
using System.Linq;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.TheTickLib;

public sealed partial class TheTickSubmod : ISaveParticipantSource
{
    /// <summary>Sidecar record: protected vehicle IDs. Patch state itself is process-global and never saved.</summary>
    public const string SaveId = "the-tick";

    public IEnumerable<ISaveParticipant> SaveParticipants => new ISaveParticipant[]
    {
        new SaveParticipant<string[]>(SaveId, CaptureVehicles, ResetProtection, (state, context) =>
        {
            context.Require(state.Length == 0 || TheTickPatches.IsApplied,
                "The Tick patches are unavailable; saved indestructibility could not be restored.");
            foreach (string id in state)
            {
                var vehicle = VehicleProvider.FindVehicle(id);
                if (vehicle == null || vehicle.IsDisposed)
                {
                    context.Warn($"Missing or ambiguous indestructible vessel '{id}'.");
                    continue;
                }
                TickProtection.Add(vehicle);
            }
        }, order: 20, validate: ValidateVehicles)
    };

    private static string[] CaptureVehicles()
    {
        var vehicles = TickProtection.Snapshot().Where(vehicle => !vehicle.IsDisposed).ToArray();
        // Refuse to write a record that could not uniquely rebind on load.
        foreach (var vehicle in vehicles)
            if (!ReferenceEquals(vehicle, VehicleProvider.FindVehicle(vehicle.Id)))
                throw new InvalidOperationException($"Indestructible vessel '{vehicle.Id}' is missing or ambiguous.");
        var ids = vehicles.Select(vehicle => vehicle.Id).Order(StringComparer.Ordinal).ToArray();
        ValidateVehicles(ids);
        return ids;
    }

    private static void ValidateVehicles(string[] ids)
    {
        if (ids.Length > 10000 || ids.Any(string.IsNullOrWhiteSpace)
            || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidOperationException("Invalid saved indestructible vessels.");
    }

    private void ResetProtection()
    {
        TickProtection.Clear();
        ResetPicker();
    }
}
