using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using KSA;

namespace MeowSci.KsaAbstractions.Persistence;

/// <summary>One traversal per vehicle per save/restore transaction, even for large pixel grids.</summary>
internal sealed class PartIdentitySnapshot
{
    internal sealed record Address(int[] Tree, int[] Sub);
    public Dictionary<Part, Address> Addresses { get; } = new();
    public string Topology { get; }

    public PartIdentitySnapshot(Vehicle vehicle)
    {
        var signature = new StringBuilder();
        VisitTree(vehicle.Parts.Root, new(), signature);
        Topology = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature.ToString())));
    }

    private void VisitTree(Part part, List<int> tree, StringBuilder signature)
    {
        if (tree.Count > 256) throw new InvalidOperationException("Part tree exceeds save depth limit.");
        signature.Append('T').Append(part.TreeChildren.Count).Append(':');
        VisitSub(part, tree, new(), signature);
        for (int i = 0; i < part.TreeChildren.Count; i++)
        {
            tree.Add(i);
            VisitTree(part.TreeChildren[i], tree, signature);
            tree.RemoveAt(tree.Count - 1);
        }
    }

    private void VisitSub(Part part, List<int> tree, List<int> sub, StringBuilder signature)
    {
        if (sub.Count > 64 || Addresses.Count >= 200000) throw new InvalidOperationException("Part identity exceeds save limits.");
        if (!Addresses.TryAdd(part, new(tree.ToArray(), sub.ToArray())))
            throw new InvalidOperationException("A part appears more than once in the vehicle tree.");
        string template = part.Template.Id;
        signature.Append('P').Append(template.Length).Append(':').Append(template).Append(':').Append(part.SubParts.Length).Append(':');
        for (int i = 0; i < part.SubParts.Length; i++)
        {
            sub.Add(i);
            VisitSub(part.SubParts[i], tree, sub, signature);
            sub.RemoveAt(sub.Count - 1);
        }
    }
}
