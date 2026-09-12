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

    public static SavedPartReference Capture(Vehicle vehicle, Part part)
    {
        var path = new List<int>();
        var sub = new List<int>();
        if (!FindTree(vehicle.Parts.Root, part, path, sub, 0))
            throw new InvalidOperationException($"Part does not belong to {vehicle.Id}.");
        return new() { VehicleId = vehicle.Id, TreePath = path.ToArray(), SubPartPath = sub.ToArray(), TemplateId = part.Template.Id };
    }

    public Part? Resolve()
    {
        if (TreePath == null || SubPartPath == null || TreePath.Length > 256 || SubPartPath.Length > 64) return null;
        var vehicle = VehicleProvider.FindVehicle(VehicleId);
        if (vehicle == null || vehicle.IsDisposed) return null;
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

    private static bool FindTree(Part current, Part target, List<int> path, List<int> sub, int depth)
    {
        if (depth > 256) throw new InvalidOperationException("Part tree exceeds save depth limit.");
        if (FindSub(current, target, sub, 0)) return true;
        for (int i = 0; i < current.TreeChildren.Count; i++)
        {
            path.Add(i);
            if (FindTree(current.TreeChildren[i], target, path, sub, depth + 1)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    private static bool FindSub(Part current, Part target, List<int> path, int depth)
    {
        if (depth > 64) throw new InvalidOperationException("Subpart tree exceeds save depth limit.");
        if (ReferenceEquals(current, target)) return true;
        for (int i = 0; i < current.SubParts.Length; i++)
        {
            path.Add(i);
            if (FindSub(current.SubParts[i], target, path, depth + 1)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }
}
