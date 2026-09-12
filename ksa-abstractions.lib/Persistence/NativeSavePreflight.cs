using System;
using System.Collections.Generic;
using System.IO;
using KSA;

namespace MeowSci.KsaAbstractions.Persistence;

/// <summary>Reject known native reconstruction failures before releasing any current-world ownership.</summary>
public static class NativeSavePreflight
{
    public static void Validate(UniverseData data)
    {
        try
        {
            // UniverseData.IsValid rejects an unfollowed/free camera although native saves
            // explicitly support it. Validate the actual reconstruction inputs instead.
            if (data.GameTime == null || !data.GameTime.IsValid() || data.Camera == null || data.KittenRoster == null
                || data.Camera.Following == null || data.Camera.TidalLocking == null
                || data.Camera.MapInverted == null || data.Camera.MapPreviouslyControlled == null
                || data.Camera._positionRaw?.IsValid() == false || data.Camera._rotationRaw?.IsValid() == false
                || data.Camera._scaleRaw?.IsValid() == false || !Enum.IsDefined(data.Camera.CameraMode))
                throw new InvalidDataException("This save has invalid game time, camera, or kitten roster data.");
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        { throw new InvalidDataException("This save has invalid game time or camera data.", ex); }
        if (data.CelestialSystems == null || data.CelestialSystems.Count != 1)
            throw new InvalidDataException("This save must contain exactly one celestial system.");
        var system = data.CelestialSystems[0];
        if (system?.Id == null || Universe.CurrentSystem == null
            || !string.Equals(system.Id.Id, Universe.CurrentSystem.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This save belongs to a different celestial system. Load the matching system before this save.");
        if (system.Vehicles == null || system.Vehicles.Count > 100000)
            throw new InvalidDataException("Invalid native vehicle table.");
        // Native KeyHash.Make lowercases identities before hashing.
        var vehicles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var templates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var vehicle in system.Vehicles)
        {
            if (vehicle == null || string.IsNullOrWhiteSpace(vehicle.Id) || !vehicles.Add(vehicle.Id))
                throw new InvalidDataException("Invalid or duplicate native vehicle identifier.");
            if (vehicle.ParentBody == null || Universe.CurrentSystem.Get(vehicle.ParentBody.Id) is not IParentBody)
                throw new InvalidDataException($"Vehicle '{vehicle.Id}' needs missing parent body '{vehicle.ParentBody?.Id}'.");
            if (!string.IsNullOrEmpty(vehicle.Character))
            {
                try
                {
                    if (ModLibrary.Get<CharacterReference>(vehicle.Character) == null) throw new InvalidOperationException();
                }
                catch (Exception ex)
                { throw new InvalidDataException($"Install the character '{vehicle.Character}' required by '{vehicle.Id}' before loading this save.", ex); }
            }
            if (vehicle.RootPartInstance != null) ValidateParts(vehicle.RootPartInstance, templates, vehicle.Id);
        }
    }

    private static void ValidateParts(PartInstance root, HashSet<string> templates, string vehicleId)
    {
        var pending = new Stack<(PartInstance Part, int Depth)>();
        var visited = new HashSet<PartInstance>(ReferenceEqualityComparer.Instance);
        pending.Push((root, 0));
        while (pending.TryPop(out var item))
        {
            if (item.Part == null || item.Depth > 256 || !visited.Add(item.Part) || visited.Count > 200000)
                throw new InvalidDataException($"Invalid or oversized part tree in '{vehicleId}'.");
            string id = item.Part.InstanceOf;
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException($"Unnamed part template in '{vehicleId}'.");
            if (templates.Add(id))
            {
                try
                {
                    if (ModLibrary.Get<PartTemplate>(id) == null) throw new InvalidOperationException();
                }
                catch (Exception ex)
                { throw new InvalidDataException($"Install the part mod providing '{id}' required by '{vehicleId}' before loading this save.", ex); }
            }
            if (item.Part.Children != null)
                foreach (var child in item.Part.Children) pending.Push((child, item.Depth + 1));
            if (item.Part.SubPartInstances != null)
                foreach (var child in item.Part.SubPartInstances) pending.Push((child, item.Depth + 1));
        }
    }
}
