using System;
using System.Linq;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.DohLib.Spawning;

public sealed partial class KittenSpawner
{
    /// <summary>
    /// Join KSA's normal EVA crew lifecycle. Native starting crew use MarkLaunched + StartMission(EVA).
    /// Existing entries retain mission/health history; only genuinely missing entries are created.
    /// </summary>
    internal static KittenRosterEntryData EnsureRoster(KittenEva kitten, bool requireNew = false)
    {
        if (kitten.IsDisposed || !ReferenceEquals(VehicleProvider.FindVehicle(kitten.Id), kitten))
            throw new InvalidOperationException($"Kitten is not a unique current EVA target: {kitten.Id}.");

        var roster = Universe.KittenRoster;
        var entry = roster.Find(kitten.Id);
        if (entry != null)
        {
            if (requireNew || roster.Kittens.Count(k => k.NameHash == entry.NameHash) != 1
                || !string.Equals(entry.Name, kitten.Id, StringComparison.OrdinalIgnoreCase)
                || entry.Character != kitten.Character.Id || entry.Kia || entry.Retired
                || (entry.AssignedToVehicle && entry.AssignedVehicleId != KittenRosterEntryData.EvaAssignmentId))
                throw new InvalidOperationException($"Roster identity is unavailable or incompatible: {kitten.Id}.");
            if (!entry.AssignedToVehicle) entry.StartMission(KittenRosterEntryData.EvaAssignmentId);
        }
        else
        {
            entry = new KittenRosterEntryData(kitten.Id, kitten.Character.Id);
            entry.StartMission(KittenRosterEntryData.EvaAssignmentId);
            roster.Kittens.Add(entry);
        }
        kitten.MarkLaunched();
        return entry;
    }

    /// <summary>Backfill older tracked EVA kittens without touching native deaths, boarded crew or other kittens.</summary>
    internal void MaintainRoster()
    {
        foreach (var tracked in _registry.GetAll())
        {
            if (tracked.Vehicle is not KittenEva kitten || kitten.IsDisposed
                || !ReferenceEquals(VehicleProvider.FindVehicle(kitten.Id), kitten)) continue;
            try
            {
                EnsureRoster(kitten);
                tracked.LastRosterError = null;
            }
            catch (Exception ex)
            {
                if (tracked.LastRosterError != ex.Message)
                    Console.WriteLine($"doh: Could not integrate roster for '{kitten.Id}': {ex.Message}");
                tracked.LastRosterError = ex.Message;
            }
        }
    }
}
