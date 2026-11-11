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
        SpawnNameContinuationSaveChecks();
        PaintMigrationChecks();
        PresetSnapshotChecks();
        var submod = new DohSubmod();
        NativeKillChecks(submod);
        ConstructionRollbackChecks();
        DefaultOffsetChecks();
        RosterIntegrationChecks();
        RosterSaveChecks();

        WarPaintService.Shared.Reset();
        WarPaintService.Shared.Update();
        if (System.IO.Directory.Exists(MeowSci.KsaAbstractions.KsaPaths.ModDataDir))
            System.IO.Directory.Delete(MeowSci.KsaAbstractions.KsaPaths.ModDataDir, recursive: true);
        Console.WriteLine($"PASS: {_checks} DOH naming, roster, save/restore, migration and native kill checks.");
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
