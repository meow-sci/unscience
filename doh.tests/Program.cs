using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Numerics;
using KSA;
using MeowSci.DohLib;
using MeowSci.DohLib.Materials;
using MeowSci.KsaAbstractions.Persistence;

internal static class Checks
{
    private static readonly float4 Red = new(1, 0, 0, 1);
    private static readonly float4 Blue = new(0, 0, 1, 1);
    private static readonly float4 Green = new(0, 1, 0, 1);
    private static readonly string[] Ids = { "Kitten_doh_1", "Kitten_doh_2", "Kitten_doh_3", "Kitten_doh_4" };
    private static int _checks;

    private static void Main()
    {
        var submod = new DohSubmod();
        var coordinator = new SceneSaveCoordinator(submod.SaveParticipants);

        // World A: kittens 1+2 share one batch set, 3 has its own set, 4 is untinted.
        var world = Kittens(Ids);
        SetWorld(world.Append(new Vehicle("Rocket")).ToArray());
        var shared = submod.Factory.CreateSet(Red);
        var unique = submod.Factory.CreateSet(Blue);
        submod.Track(world[0], "Calico", shared);
        submod.Track(world[1], "Calico", shared);
        submod.Track(world[2], "Tabby", unique);
        submod.Track(world[3], "Calico", null);
        unique.Materials[0].Color = Green;
        unique.Materials[0].ApplyColor();

        var saved = RoundTrip(coordinator.Capture());
        var kittens = SavedKittens(saved);
        Require(kittens.Count == 4, "capture includes every live spawned kitten");
        Require(kittens[0].MaterialGroup != null && kittens[0].MaterialGroup == kittens[1].MaterialGroup,
            "a shared batch saves one material group");
        Require(kittens[2].MaterialGroup != kittens[0].MaterialGroup, "a unique kitten saves its own group");
        Require(kittens[3].MaterialGroup == null && kittens[3].Tint == null, "an untinted kitten saves no group");

        // Same save again: reconstructed kittens rebind onto the detached sets, no new GPU slots.
        submod.SelectedVehicle = world[0];
        Load(coordinator, saved, Kittens(Ids));
        Require(submod.SelectedVehicle == null, "reset clears the spawn target selection");
        Require(submod.Spawner.Clones == 0 && MaterialSystemAccessor.Destroyed.Count == 0,
            "same-save reload reuses every detached set and releases nothing");
        Require(ReferenceEquals(Set(submod, 0), shared) && ReferenceEquals(Set(submod, 1), shared),
            "the restored batch still shares one set");
        Require(ReferenceEquals(Set(submod, 2), unique), "the unique kitten keeps its distinct set");
        Require(Set(submod, 2)!.Materials[0].Color == Green, "per-material colors are restored");
        Require(Set(submod, 3) == null && submod.Registry.Count == 4, "the untinted kitten is rebound without materials");

        // Legacy payload without MaterialGroup: kittens must never be merged onto one set.
        var legacy = StripGroups(saved);
        Load(coordinator, legacy, Kittens(Ids));
        var split = Set(submod, 1)!;
        Require(ReferenceEquals(Set(submod, 0), shared) && !ReferenceEquals(split, shared),
            "legacy kittens restore with their own sets, even if they shared one before");
        Require(submod.Spawner.Clones == 1, "only the second formerly-shared kitten allocates a new set");

        // A save with a missing kitten: its detached set and any set nothing rebinds are released.
        Load(coordinator, saved, Kittens(Ids[0], Ids[1], Ids[3]));
        Require(coordinator.Messages.Any(message => message.Contains("DOH kitten missing: Kitten_doh_3")),
            "a missing kitten is reported");
        Require(ReferenceEquals(Set(submod, 0), shared) && ReferenceEquals(Set(submod, 1), shared),
            "the saved group re-merges the batch onto one set");
        Require(unique.IsReleased && split.IsReleased && !shared.IsReleased,
            "detached sets no kitten rebinds are released after restore");
        Require(OwnedBy(unique).All(MaterialSystemAccessor.Destroyed.Contains)
            && OwnedBy(split).All(MaterialSystemAccessor.Destroyed.Contains), "released sets free their GPU slots");
        Require(submod.Factory.CreatedSets.Count == 1, "only the rebound set stays allocated");

        // Vanilla load: the native load destroyed the kittens; their sets are freed on the next frame.
        Load(coordinator, null, new Vehicle("Rocket"));
        Require(submod.Registry.Count == 0 && !shared.IsReleased, "reset without restore keeps detached sets until the next frame");
        submod.NextFrame();
        Require(shared.IsReleased && submod.Factory.CreatedSets.Count == 0, "stale detached sets are released on the next frame");

        // Released sets are never reused, even when the same save is loaded again.
        int clonesBefore = submod.Spawner.Clones;
        Load(coordinator, saved, Kittens(Ids));
        Require(submod.Spawner.Clones == clonesBefore + 2, "a fresh world allocates one set per saved group");
        Require(ReferenceEquals(Set(submod, 0), Set(submod, 1)) && !Set(submod, 0)!.IsReleased,
            "a fresh world still shares the batch set");
        Require(Set(submod, 2)!.Materials[0].Color == Green, "colors restore onto newly allocated sets");

        // Release is idempotent.
        var once = submod.Factory.CreateSet(Red);
        submod.Factory.Release(once);
        int destroyed = MaterialSystemAccessor.Destroyed.Count;
        submod.Factory.Release(once);
        Require(MaterialSystemAccessor.Destroyed.Count == destroyed, "releasing twice frees nothing twice");

        // A blank group is malformed: rejected before reset and retained for a compatible build.
        var bad = WithGroup(saved, " ");
        Load(coordinator, bad, Kittens(Ids));
        Require(coordinator.RetainedCount == 1 && submod.Registry.Count == 0, "a blank material group is rejected and retained");

        Console.WriteLine($"PASS: {_checks} DOH save/restore and material release checks.");
    }

    private static KittenEva[] Kittens(params string[] ids) => ids.Select(id => new KittenEva(id)).ToArray();

    private static KittenMaterialSet? Set(DohSubmod submod, int index) => submod.Registry.Get(Ids[index])?.MaterialSet;

    private static IEnumerable<string> OwnedBy(KittenMaterialSet set) => set.AssetNames;

    private static List<DohSubmod.SavedKitten> SavedKittens(SaveDocument document) =>
        SaveJson.FromElement<DohSubmod.SavedDoh>(document.Features["doh"].State).Kittens;

    private static SaveDocument RoundTrip(SaveDocument document) =>
        SaveJson.FromElement<SaveDocument>(SaveJson.ToElement(document));

    /// <summary>Removes MaterialGroup entirely, as written by builds before shared sets existed.</summary>
    private static SaveDocument StripGroups(SaveDocument document)
    {
        var state = JsonNode.Parse(document.Features["doh"].State.GetRawText())!;
        foreach (var kitten in state["Kittens"]!.AsArray())
            kitten!.AsObject().Remove("MaterialGroup");
        var copy = RoundTrip(document);
        copy.Features["doh"] = new SaveFeature { State = JsonSerializer.SerializeToElement(state) };
        Require(!copy.Features["doh"].State.GetRawText().Contains("MaterialGroup"), "legacy fixture has no group field");
        return copy;
    }

    private static SaveDocument WithGroup(SaveDocument document, string group)
    {
        var saved = SaveJson.FromElement<DohSubmod.SavedDoh>(document.Features["doh"].State);
        saved.Kittens[0].MaterialGroup = group;
        var copy = RoundTrip(document);
        copy.Features["doh"] = new SaveFeature { State = SaveJson.ToElement(saved) };
        return copy;
    }

    private static void Load(SceneSaveCoordinator coordinator, SaveDocument? document, params Vehicle[] vehicles)
    {
        coordinator.PrepareLoad(document);
        coordinator.ResetWorld();
        foreach (var vehicle in Universe.CurrentSystem?.All.UnsafeAsList() ?? new List<Vehicle>())
            vehicle.IsDisposed = true; // native load destroys the old world
        SetWorld(vehicles);
        coordinator.RestoreWorld();
        coordinator.FinishLoad();
    }

    private static void SetWorld(params Vehicle[] vehicles)
    {
        Universe.CurrentSystem = new CelestialSystem();
        Universe.CurrentSystem.All.UnsafeAsList().AddRange(vehicles);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
}
