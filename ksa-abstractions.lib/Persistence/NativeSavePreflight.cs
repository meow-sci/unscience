using System;
using System.Collections.Generic;
using System.IO;
using KSA;

namespace MeowSci.KsaAbstractions.Persistence;

/// <summary>
/// Checks native reconstruction inputs before any current-world ownership is released.
/// Refuses only data that native <c>Universe.DeserializeSave</c> cannot survive. Vehicles that KSA
/// itself skips are returned as diagnostics instead of refusing the whole save.
/// </summary>
/// <remarks>
/// Since KSA 5632/5633, <c>CelestialSystem.DeserializeSave(CelestialSystemData, ISet&lt;string&gt;)</c> skips a
/// vehicle whose parent body is missing or not an <c>IParentBody</c>, a vehicle that
/// <c>PartInstance.FindMissingParts</c> reports (alert <c>AlertVehicleMissingParts</c>), and a vehicle whose
/// construction throws, for example on an uninstalled character (alert <c>AlertVehicleChangedParts</c>).
/// <c>Universe.DeserializeSave</c> then returns the crew of skipped vehicles to the roster.
/// </remarks>
public static class NativeSavePreflight
{
    private const int MaxVehicles = 100000;
    private const int MaxTreeDepth = 256;
    private const int MaxTreeParts = 200000;

    /// <summary>Throws <see cref="InvalidDataException"/> for unsurvivable data; returns one diagnostic per vehicle KSA will skip.</summary>
    public static IReadOnlyList<string> Validate(UniverseData data)
    {
        ValidateGlobals(data);
        var system = data.CelestialSystems[0];
        var skipped = new List<string>();
        // Native KeyHash.Make lowercases identities before hashing.
        var vehicles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var templates = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var vehicle in system.Vehicles)
        {
            // LookupCollection.Register logs and ignores blank or duplicate ids, leaving a constructed but
            // unregistered vehicle in its parent's Children list.
            if (vehicle == null || string.IsNullOrWhiteSpace(vehicle.Id) || !vehicles.Add(vehicle.Id))
                throw new InvalidDataException("Invalid or duplicate native vehicle identifier.");
            // Native dereferences ParentBody.Id outside its try/catch, after the current vehicles are destroyed.
            if (vehicle.ParentBody == null)
                throw new InvalidDataException($"Vehicle '{vehicle.Id}' has no parent body record.");
            var reasons = new List<string>();
            if (!HasParentBody(vehicle.ParentBody.Id))
                reasons.Add($"parent body '{vehicle.ParentBody.Id}' is missing");
            if (!string.IsNullOrEmpty(vehicle.Character) && !HasCharacter(vehicle.Character))
                reasons.Add($"character '{vehicle.Character}' is not installed");
            var missingParts = new SortedSet<string>(StringComparer.Ordinal);
            if (vehicle.RootPartInstance != null)
                CollectMissingTemplates(vehicle.RootPartInstance, templates, missingParts, vehicle.Id);
            if (missingParts.Count != 0)
                reasons.Add($"part templates are not installed ({string.Join(", ", missingParts)})");
            if (reasons.Count != 0)
                skipped.Add($"KSA will not load vehicle '{vehicle.Id}': {string.Join("; ", reasons)}. "
                    + "Unscience state for it is reported as unresolved and retained.");
        }
        return skipped;
    }

    private static void ValidateGlobals(UniverseData data)
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
        if (system.Vehicles == null || system.Vehicles.Count > MaxVehicles)
            throw new InvalidDataException("Invalid native vehicle table.");
    }

    private static bool HasParentBody(string? id) =>
        !string.IsNullOrWhiteSpace(id) && Universe.CurrentSystem!.Get(id) is IParentBody;

    private static bool HasCharacter(string id)
    {
        try { return ModLibrary.Get<CharacterReference>(id) != null; }
        catch (Exception) { return false; } // ModLibrary.Get throws for an unknown id.
    }

    private static bool HasTemplate(string id)
    {
        try { return ModLibrary.Get<PartTemplate>(id) != null; }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Bounded, acyclic walk of the saved part tree. Native <c>CollectMissingParts</c> recurses through
    /// <c>Children</c> without null or cycle checks, so a malformed tree is still refused here.
    /// </summary>
    private static void CollectMissingTemplates(PartInstance root, Dictionary<string, bool> templates,
        ISet<string> missing, string vehicleId)
    {
        var pending = new Stack<(PartInstance Part, int Depth)>();
        var visited = new HashSet<PartInstance>(ReferenceEqualityComparer.Instance);
        pending.Push((root, 0));
        while (pending.TryPop(out var item))
        {
            if (item.Part == null || item.Depth > MaxTreeDepth || !visited.Add(item.Part) || visited.Count > MaxTreeParts)
                throw new InvalidDataException($"Invalid or oversized part tree in '{vehicleId}'.");
            string id = item.Part.InstanceOf;
            if (string.IsNullOrWhiteSpace(id)) missing.Add("(unnamed)");
            else
            {
                if (!templates.TryGetValue(id, out bool installed)) templates[id] = installed = HasTemplate(id);
                if (!installed) missing.Add(id);
            }
            if (item.Part.Children != null)
                foreach (var child in item.Part.Children) pending.Push((child, item.Depth + 1));
            if (item.Part.SubPartInstances != null)
                foreach (var child in item.Part.SubPartInstances) pending.Push((child, item.Depth + 1));
        }
    }
}
