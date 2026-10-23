using System;
using System.Collections.Generic;
using System.Linq;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

/// <summary>KSA 5638 native-load preflight: structural refusals and vehicles KSA skips.</summary>
internal static partial class Program
{
    private static void NativePreflight()
    {
        var data = new UniverseData();
        data.CelestialSystems[0].Vehicles.Add(new VehicleData());
        data.CelestialSystems[0].Vehicles.Add(new VehicleData { Id = "VEHICLE" });
        int failures = 0;
        NativeSaveHooks.LoadFailed = ex => { Check(ex is System.IO.InvalidDataException, "controlled preflight error"); failures++; };
        var save = new UncompressedSave { UniverseData = data };
        bool rejected = false;
        try { LoadNow(save); }
        catch (System.IO.InvalidDataException ex) { rejected = ex.Message.Contains("duplicate"); }
        Check(rejected && failures == 1, "file preflight reports one failure");
        Equal("preflight", "native read", "finished");
        Check(NativeSaveHooks.LastLoadError is System.IO.InvalidDataException, "visible failure recorded");
        Check(!PhysicsFrameHook.IsReplayingWorldChange, "failed preflight releases guard");
        Trace.Events.Clear();
        data.CelestialSystems[0].Vehicles.RemoveAt(1);
        LoadNow(save);
        Check(Trace.Events.Contains("reset") && NativeSaveHooks.LastLoadError == null, "valid data permits normal replay");

        // Data native DeserializeSave cannot survive (it dereferences outside its per-vehicle try/catch,
        // or leaves an unregistered vehicle behind) is still refused before reset.
        var cycle = new PartInstance();
        cycle.Children = new() { cycle };
        PartInstance deep = new();
        for (int i = 0; i < 300; i++) deep = new PartInstance { Children = new() { deep } };
        foreach (Action<UniverseData> corrupt in new Action<UniverseData>[]
        {
            d => d.GameTime = null,
            d => d.Camera = null,
            d => d.KittenRoster = null,
            d => d.GameTime!.Valid = false,
            d => d.Camera!.Following = null,
            d => d.Camera!.MapInverted = null,
            d => d.Camera!.CameraMode = (CameraMode)999,
            d => d.Camera!._positionRaw = new() { Valid = false },
            d => d.CelestialSystems[0].Id.Id = "different system",
            d => d.CelestialSystems.Add(new()),
            d => d.CelestialSystems[0].Vehicles[0].ParentBody = null!,
            d => d.CelestialSystems[0].Vehicles[0].Id = " ",
            d => d.CelestialSystems[0].Vehicles.Add(new VehicleData { Id = "VEHICLE" }),
            d => d.CelestialSystems[0].Vehicles.Add(d.CelestialSystems[0].Vehicles[0]),
            d => d.CelestialSystems[0].Vehicles[0].RootPartInstance = new() { Children = new() { null! } },
            d => d.CelestialSystems[0].Vehicles[0].RootPartInstance = cycle,
            d => d.CelestialSystems[0].Vehicles[0].RootPartInstance = deep
        })
        {
            var invalid = new UniverseData();
            invalid.CelestialSystems[0].Vehicles.Add(new VehicleData());
            corrupt(invalid);
            rejected = false;
            try { NativeSavePreflight.Validate(invalid); }
            catch (System.IO.InvalidDataException) { rejected = true; }
            Check(rejected, "unsurvivable native data rejected");
        }
        var freeCamera = new UniverseData();
        Check(freeCamera.Camera!.Following!.Id == "", "fixture has an unfollowed free camera");
        Check(NativeSavePreflight.Validate(freeCamera).Count == 0, "free camera loads without diagnostics");
    }

    private static void SkippedVehiclePreflight()
    {
        var data = SkippedVehicleSave();
        var warnings = new List<string>();
        NativeSaveHooks.LoadWarning = message => { warnings.Add(message); Trace.Events.Add("load warning"); };
        int failures = 0;
        NativeSaveHooks.LoadFailed = _ => failures++;
        LoadNow(new UncompressedSave { UniverseData = data });
        Equal("preflight", "native read", "join nearest-orbit-and-performance", "join orbit", "join vehicle", "join cloth", "clear pending",
            "reset", "clear pending", "load warning", "load warning", "load warning",
            "join nearest-orbit-and-performance", "join orbit", "join vehicle", "join cloth", "native destroy",
            "native skipped no-parts", "native skipped no-body", "native skipped no-character", "native reconstructed",
            "restore", "native menus closed", "finished");
        Check(failures == 0 && NativeSaveHooks.LastLoadError == null, "skipped vehicles are not a load failure");
        Check(Universe.LoadedVehicles.SequenceEqual(new[] { "loaded" }), "native reconstructs the remaining vehicles");
        Check(warnings[0].Contains("'no-parts'") && warnings[0].Contains("missing-part, missing-subpart"), "missing templates named once each");
        Check(warnings[1].Contains("'no-body'") && warnings[1].Contains("parent body 'missing body'"), "missing parent named");
        Check(warnings[2].Contains("'no-character'") && warnings[2].Contains("character 'missing character'"), "missing character named");
        Check(warnings.All(w => w.Contains("reported as unresolved")), "warnings explain the sidecar consequence");

        // Direct DeserializeSave has no file transaction; the warnings still follow its reset exactly once.
        warnings.Clear();
        Trace.Events.Clear();
        Universe.DeserializeSave(data);
        PhysicsFrameHook.ReplayPending();
        Check(warnings.Count == 3 && Trace.Events.IndexOf("reset") < Trace.Events.IndexOf("load warning"), "direct load warns after reset");
    }

    private static void SkippedVehicleRoundTrip()
    {
        var bound = new List<string>();
        var participant = new SaveParticipant<List<string>>("fixture-vehicles", () => new() { "loaded", "no-parts" },
            bound.Clear, (ids, context) =>
            {
                foreach (string id in ids)
                    if (Universe.LoadedVehicles.Contains(id)) bound.Add(id);
                    else context.Warn($"Missing vehicle {id}.");
            });
        var coordinator = new SceneSaveCoordinator(new[] { participant });
        var document = SaveJson.FromElement<SaveDocument>(SaveJson.ToElement(coordinator.Capture()));
        NativeSaveHooks.Loading = _ => coordinator.PrepareLoad(document);
        NativeSaveHooks.Resetting = coordinator.ResetWorld;
        NativeSaveHooks.Restoring = coordinator.RestoreWorld;
        NativeSaveHooks.LoadFinished = coordinator.FinishLoad;
        NativeSaveHooks.LoadWarning = coordinator.Report;
        var save = new UncompressedSave { UniverseData = SkippedVehicleSave() };
        for (int load = 1; load <= 2; load++)
        {
            LoadNow(save);
            Check(bound.SequenceEqual(new[] { "loaded" }), "only the reconstructed vehicle is rebound, never a substitute");
            Check(coordinator.Messages.Count(m => m.StartsWith("KSA will not load vehicle 'no-parts'")) == 1
                && coordinator.Messages.Count(m => m == "fixture-vehicles: Missing vehicle no-parts.") == 1,
                "preflight and participant diagnostics reported once per load");
            Check(coordinator.RetainedCount == 1, "the unresolved record is retained for a later compatible load");
        }
        var resaved = coordinator.Capture();
        Check(resaved.Features["fixture-vehicles"].State.GetRawText() == document.Features["fixture-vehicles"].State.GetRawText(),
            "a later save keeps the original record instead of dropping the skipped vehicle");
    }

    private static UniverseData SkippedVehicleSave()
    {
        var data = new UniverseData();
        data.CelestialSystems[0].Vehicles.AddRange(new[]
        {
            new VehicleData { Id = "loaded", RootPartInstance = new() },
            new VehicleData
            {
                Id = "no-parts",
                RootPartInstance = new()
                {
                    InstanceOf = "missing-part",
                    Children = new() { new() { InstanceOf = "missing-part" } },
                    SubPartInstances = new() { new() { InstanceOf = "missing-subpart" } }
                }
            },
            new VehicleData { Id = "no-body", ParentBody = new() { Id = "missing body" } },
            new VehicleData { Id = "no-character", Character = "missing character" }
        });
        return data;
    }
}
