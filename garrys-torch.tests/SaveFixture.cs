using System;
using System.Collections.Generic;
using System.Linq;
using KSA;
namespace MeowSci.KsaAbstractions.Persistence;
// Only native object lookup is substituted; production snapshot and JSON code are linked.
public sealed class SavedPartReference
{
    public static Vehicle? Current;
    public int[] Path { get; set; } = Array.Empty<int>();
    public static SavedPartReference Capture(Vehicle vehicle, Part part)
    {
        var parts = vehicle.Parts.Parts.ToArray();
        for (int i = 0; i < parts.Length; i++)
        {
            var path = new List<int> { i };
            if (Find(parts[i], part, path)) return new() { Path = path.ToArray() };
        }
        throw new InvalidOperationException("Part missing from fixture vehicle.");
    }
    private static bool Find(Part part, Part target, List<int> path)
    {
        if (part == target) return true;
        for (int i = 0; i < part.SubParts.Count; i++)
        {
            path.Add(i);
            if (Find(part.SubParts[i], target, path)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }
    public Part? Resolve()
    {
        var parts = Current?.Parts.Parts.ToArray();
        if (parts == null || Path.Length == 0 || Path[0] >= parts.Length) return null;
        var part = parts[Path[0]];
        foreach (int index in Path.Skip(1))
        {
            if (index >= part.SubParts.Count) return null;
            part = part.SubParts[index];
        }
        return part;
    }
}
