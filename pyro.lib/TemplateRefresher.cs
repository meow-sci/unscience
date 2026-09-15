using System;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.PyroLib;

/// <summary>
/// After a shared <see cref="VolumetricExhaustTemplate"/> is edited, pushes the change to every live instance
/// that reads it — pyro's own plumes and every real engine nozzle in the current system — the same way the
/// game's built-in "Volumetric Exhausts" debug editor does. The GPU template buffer itself is rebuilt by the
/// renderer every frame, so colours/noise/brightness show up without any further work.
/// </summary>
public static class TemplateRefresher
{
    public static void NotifyTemplateChanged(VolumetricExhaustTemplate template, PyroSubmod submod)
    {
        foreach (var plume in submod.Plumes)
        {
            if (plume.Instance?.Template == template)
                plume.Instance.OnSettingsChanged();
        }

        try
        {
            foreach (var vehicle in VehicleProvider.GetAllVehicles())
                RefreshVehicleNozzles(vehicle, template);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"pyro: could not refresh engine nozzles after template edit: {ex.Message}");
        }
    }

    private static void RefreshVehicleNozzles(Vehicle vehicle, VolumetricExhaustTemplate template)
    {
        var enumerator = vehicle.Parts.RocketNozzles.ModulesAndAllStates.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var current = enumerator.Current;
            var instance = current.FxState.VolumetricExhaust;
            if (instance == null || instance.Template != template) continue;
            instance.OnSettingsChanged();
            current.Module.RecomputeVolumetricExhaustLimits(in instance);
        }
    }
}
