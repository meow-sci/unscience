using System;
using System.Collections.Generic;
using MeowSci.KsaAbstractions.Persistence;

internal static class PartReferenceTests
{
    public static void Run()
    {
        var old = Craft(10);
        var target = old.Parts.Root.TreeChildren[1].SubParts[1];
        var saved = SaveJson.FromElement<SavedPartReference>(SaveJson.ToElement(SavedPartReference.Capture(old, target)));
        var loaded = Craft(100);
        MeowSci.KsaAbstractions.VehicleProvider.Vehicles.Clear();
        MeowSci.KsaAbstractions.VehicleProvider.Vehicles.Add(loaded);
        var resolved = saved.Resolve();
        Check(ReferenceEquals(resolved, loaded.Parts.Root.TreeChildren[1].SubParts[1]), "duplicate templates resolve by exact tree/subpart address");
        Check(resolved!.RuntimeId != target.RuntimeId, "runtime IDs change across save load");
        loaded.Parts.Root.TreeChildren.Add(new KSA.Part("new-part", 900));
        Check(saved.Resolve() == null, "changed topology refuses an otherwise valid target path");
        loaded.Parts.Root.TreeChildren.RemoveAt(2);
        using (SavedPartReference.BeginOperation())
        {
            Check(ReferenceEquals(saved.Resolve(), resolved), "transaction-scoped identity resolves consistently");
            var recaptured = SavedPartReference.Capture(loaded, resolved);
            Check(recaptured.VehicleTopology == saved.VehicleTopology, "topology survives runtime identity replacement");
        }
        loaded.Parts.Root.TreeChildren[1].SubParts[1].Template.Id = "changed";
        Check(saved.Resolve() == null, "template mismatch does not substitute another part");
        loaded.Parts.Root.TreeChildren.RemoveAt(1);
        Check(saved.Resolve() == null, "missing path stays missing");
        MeowSci.KsaAbstractions.VehicleProvider.Vehicles.Clear();
        Check(saved.Resolve() == null, "missing vehicle does not substitute controlled craft");
        saved.TreePath = new[] { -1 };
        MeowSci.KsaAbstractions.VehicleProvider.Vehicles.Add(Craft(200));
        Check(saved.Resolve() == null, "negative tree index rejected");
        saved.TreePath = Array.Empty<int>(); saved.SubPartPath = Array.Empty<int>(); saved.TemplateId = "root";
        MeowSci.KsaAbstractions.VehicleProvider.Vehicles.Add(Craft(300));
        Check(saved.Resolve() == null, "ambiguous vehicle identity rejected");
        Console.WriteLine("Saved part references: 10 identity checks passed.");
    }

    private static KSA.Vehicle Craft(int firstId)
    {
        var root = new KSA.Part("root", firstId);
        root.TreeChildren.Add(new KSA.Part("same-template", firstId + 1));
        root.TreeChildren.Add(new KSA.Part("same-template", firstId + 2));
        foreach (var child in root.TreeChildren)
            child.Children = new[] { new KSA.Part("same-subpart", firstId + 3), new KSA.Part("same-subpart", firstId + 4) };
        return new() { Id = "craft", Parts = new() { Root = root } };
    }
    private static void Check(bool passed, string message) { if (!passed) throw new Exception(message); }
}

namespace KSA
{
    public sealed class PartTemplate { public string Id { get; set; } = ""; }
    public sealed class Part(string template, int runtimeId)
    {
        public int RuntimeId = runtimeId;
        public PartTemplate Template = new() { Id = template };
        public List<Part> TreeChildren = new();
        public Part[] Children = Array.Empty<Part>();
        public ReadOnlySpan<Part> SubParts => Children;
    }
    public sealed class PartTree { public Part Root = null!; }
    public sealed class Vehicle { public string Id = ""; public PartTree Parts = null!; public bool IsDisposed; }
}
namespace MeowSci.KsaAbstractions
{
    public static class VehicleProvider
    {
        public static List<KSA.Vehicle> Vehicles { get; } = new();
        public static KSA.Vehicle? FindVehicle(string id)
        {
            KSA.Vehicle? match = null;
            foreach (var vehicle in Vehicles)
            {
                if (vehicle.Id != id) continue;
                if (match != null) return null;
                match = vehicle;
            }
            return match;
        }
    }
}
