using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.Numerics;
using KSA;
using MeowSci.DohLib;
using MeowSci.WarPaintLib;
using MeowSci.WarPaintLib.Materials;
using MeowSci.DohLib.Spawning;
using MeowSci.KsaAbstractions.Persistence;

internal static partial class Checks
{
    private static readonly float4 Red = new(1, 0, 0, 1);
    private static readonly float4 Blue = new(0, 0, 1, 1);
    private static readonly float4 Green = new(0, 1, 0, 1);
    private static readonly string[] Ids = { "Kitten_doh_1", "Kitten_doh_2", "Kitten_doh_3", "Kitten_doh_4" };
    private static int _checks;

    private static void Main()
    {
        NamingChecks();
        SpawnNameSaveChecks();
        PaintMigrationChecks();
        PresetSnapshotChecks();
        var submod = new DohSubmod();
        TeardownChecks(submod);
        QueuedTeardownChecks(submod);
        DefaultOffsetChecks();

        WarPaintService.Shared.Reset();
        WarPaintService.Shared.Update();
        if (System.IO.Directory.Exists(MeowSci.KsaAbstractions.KsaPaths.ModDataDir))
            System.IO.Directory.Delete(MeowSci.KsaAbstractions.KsaPaths.ModDataDir, recursive: true);
        Console.WriteLine($"PASS: {_checks} DOH naming, save/restore, migration and deferred teardown checks.");
    }

    /// <summary>The real KittenSpawner teardown routes every removal through Universe.DestroyVehicle.</summary>
    private static void TeardownChecks(DohSubmod submod)
    {
        var batch = Kittens("Kitten_doh_7", "Kitten_doh_8");
        var boarded = new KittenEva("Kitten_doh_9");
        var rocket = new Vehicle("Kitten_doh_6"); // a non-kitten that merely shares a registered name
        SetWorld(batch.Cast<Vehicle>().Append(boarded).Append(rocket).ToArray());
        var batchSet = submod.Paint(batch[0], Red);
        var secondSet = submod.Paint(batch[1], Red);
        var boardedSet = submod.Paint(boarded, Blue);
        submod.Track(batch[0], "Calico", batchSet);
        submod.Track(batch[1], "Calico", secondSet);
        submod.Track(boarded, "Tabby", boardedSet);
        submod.Registry.Register(rocket.Id, "Calico", null);
        Program.ControlledVehicle = batch[0];
        Trace.Events.Clear();

        NativeKittenPaintBackend.FailRestore = true;
        Require(!submod.Spawner.Despawn(batch[0].Id) && !batch[0].IsDisposed && !batchSet.IsReleased
            && submod.Registry.Get(batch[0].Id) != null && WarPaintService.Shared.TryGetPaint(batch[0], out _),
            "failed original-binding restoration preserves paint ownership and prevents native destruction");
        NativeKittenPaintBackend.FailRestore = false;
        Require(submod.Spawner.Despawn(batch[0].Id), "despawn succeeds");
        Require(Trace.Events.SequenceEqual(new[] { "restore paint " + batchSet.Id, "release paint " + batchSet.Id, "wait vehicle solver", "wait nearest orbit", "destroy Kitten_doh_7 Boarded",
            "deregister Kitten_doh_7" }), "despawn joins the world readers, then uses DestroyVehicle without touching crew records: "
            + string.Join(", ", Trace.Events));
        Require(batch[0].IsDisposed && Program.ControlledVehicle == null && submod.Registry.Get(batch[0].Id) == null,
            "the game teardown releases control and doh drops the entry");
        Require(batchSet.IsReleased && !secondSet.IsReleased, "despawn releases only that kitten's centrally owned paint");
        Require(submod.Spawner.Despawn(batch[1].Id) && secondSet.IsReleased, "a second kitten releases its own independent set");

        Trace.Events.Clear();
        boarded.IsDisposed = true; // destroyed by the game, e.g. it boarded a vessel
        Require(submod.Spawner.Despawn(boarded.Id) && Trace.Events.SequenceEqual(new[] { "release paint " + boardedSet.Id }) && boardedSet.IsReleased
            && submod.Registry.Get(boarded.Id) == null, "a kitten the game destroyed is not torn down twice, but its slots are freed");

        Trace.Events.Clear();
        Require(submod.Spawner.Despawn(rocket.Id) && !rocket.IsDisposed && Trace.Events.Count == 0,
            "a non-kitten vehicle with a registered name is never destroyed");

        var orphan = new KittenEva("Kitten_doh_10");
        var stubborn = new KittenEva("Kitten_doh_11");
        Universe.CurrentSystem!.All.UnsafeAsList().AddRange(new[] { orphan, stubborn });
        KittenSpawner.RemoveOrphanedKitten(Universe.CurrentSystem, orphan.Id);
        Require(orphan.IsDisposed && !Universe.CurrentSystem.All.TryGet(orphan.Id, out _)
            && Trace.Events.Contains("destroy Kitten_doh_10 Boarded"), "a half-built kitten is removed through the game teardown");
        Universe.FailDestroy = true;
        KittenSpawner.RemoveOrphanedKitten(Universe.CurrentSystem, stubborn.Id);
        Universe.FailDestroy = false;
        Require(!Universe.CurrentSystem.All.TryGet(stubborn.Id, out _), "a half-built kitten is deregistered even when the teardown throws");
    }

    private static KittenEva[] Kittens(params string[] ids) => ids.Select(id => new KittenEva(id)).ToArray();

    private static KittenMaterialSet? Set(DohSubmod submod, int index) =>
        VehicleProviderKitten(Ids[index]) is { } kitten && WarPaintService.Shared.TryGetPaint(kitten, out var paint) ? paint.MaterialSet : null;

    private static KittenEva? VehicleProviderKitten(string id) => MeowSci.KsaAbstractions.VehicleProvider.FindVehicle(id) as KittenEva;

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
