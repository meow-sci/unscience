using System;
using System.Collections.Generic;
using System.Linq;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.KitchenSinkLib;

public sealed partial class KitchenSinkSubmod : ISaveParticipantSource
{
    public IEnumerable<ISaveParticipant> SaveParticipants => new ISaveParticipant[]
    {
        // Preserve the existing boolean record so older saves still restore IVA visibility.
        new SaveParticipant<bool>("kitchen-sink", () => IvaForceRender.Enabled,
            () => IvaForceRender.Enabled = false, (state, _) => IvaForceRender.Enabled = state, order: 20),
        new SaveParticipant<string[]>("kitchen-sink-g-load", CaptureGLoadVehicles,
            ResetGLoadProtection, (state, context) =>
            {
                context.Require(state.Length == 0 || GLoadProtectionPatches.IsApplied,
                    "G-load protection patch is unavailable; saved protection could not be restored.");
                foreach (string id in state)
                {
                    var vehicle = VehicleProvider.FindVehicle(id);
                    if (vehicle == null || vehicle.IsDisposed)
                    {
                        context.Warn($"Missing or ambiguous G-load protection vehicle '{id}'.");
                        continue;
                    }
                    GLoadProtection.Add(vehicle);
                }
            }, order: 20, validate: ValidateGLoadVehicles)
    };

    private static string[] CaptureGLoadVehicles()
    {
        var vehicles = GLoadProtection.Snapshot().Where(vehicle => !vehicle.IsDisposed).ToArray();
        // Avoid producing an apparently valid save that cannot uniquely restore its targets.
        foreach (var vehicle in vehicles)
            if (!ReferenceEquals(vehicle, VehicleProvider.FindVehicle(vehicle.Id)))
                throw new InvalidOperationException($"G-load protection vehicle '{vehicle.Id}' is missing or ambiguous.");
        var ids = vehicles.Select(vehicle => vehicle.Id).Order(StringComparer.Ordinal).ToArray();
        ValidateGLoadVehicles(ids);
        return ids;
    }

    private static void ValidateGLoadVehicles(string[] ids)
    {
        if (ids.Length > 10000 || ids.Any(string.IsNullOrWhiteSpace)
            || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidOperationException("Invalid saved G-load protection vehicles.");
    }

    private void ResetGLoadProtection()
    {
        GLoadProtection.Clear();
        ResetGLoadPicker();
    }
}
