using System;
using System.Collections.Generic;
using KSA;

namespace MeowSci.KsaAbstractions.Persistence;

/// <summary>Save-stable tree address. Runtime InstanceId and ordinary Part.Id are not persisted by KSA.</summary>
public sealed class SavedPartReference
{
    public string VehicleId { get; set; } = "";
    public int[] TreePath { get; set; } = Array.Empty<int>();
    public int[] SubPartPath { get; set; } = Array.Empty<int>();
    public string TemplateId { get; set; } = "";
    public string VehicleTopology { get; set; } = "";
    private static Dictionary<Vehicle, PartIdentitySnapshot>? _operation;

    /// <summary>Use on the game thread around an immutable capture/rebind phase. Never hold across frames.</summary>
    public static IDisposable BeginOperation()
    {
        var previous = _operation;
        _operation = new();
        return new OperationScope(previous);
    }

    private sealed class OperationScope(Dictionary<Vehicle, PartIdentitySnapshot>? previous) : IDisposable
    {
        public void Dispose() => _operation = previous;
    }

    private static PartIdentitySnapshot Snapshot(Vehicle vehicle)
    {
        if (_operation == null) return new(vehicle);
        if (!_operation.TryGetValue(vehicle, out var snapshot)) _operation[vehicle] = snapshot = new(vehicle);
        return snapshot;
    }

    public static SavedPartReference Capture(Vehicle vehicle, Part part)
    {
        var snapshot = Snapshot(vehicle);
        if (!snapshot.Addresses.TryGetValue(part, out var path))
            throw new InvalidOperationException($"Part does not belong to {vehicle.Id}.");
        return new() { VehicleId = vehicle.Id, TreePath = path.Tree, SubPartPath = path.Sub,
            TemplateId = part.Template.Id, VehicleTopology = snapshot.Topology };
    }

    public Part? Resolve()
    {
        if (TreePath == null || SubPartPath == null || TreePath.Length > 256 || SubPartPath.Length > 64) return null;
        var vehicle = VehicleProvider.FindVehicle(VehicleId);
        if (vehicle == null || vehicle.IsDisposed || Snapshot(vehicle).Topology != VehicleTopology) return null;
        Part part = vehicle.Parts.Root;
        foreach (int index in TreePath)
        {
            if (index < 0 || index >= part.TreeChildren.Count) return null;
            part = part.TreeChildren[index];
        }
        foreach (int index in SubPartPath)
        {
            if (index < 0 || index >= part.SubParts.Length) return null;
            part = part.SubParts[index];
        }
        return part.Template.Id == TemplateId ? part : null;
    }
}
