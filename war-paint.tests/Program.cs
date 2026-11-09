using System;
using System.IO;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.WarPaintLib;

internal static partial class WarPaintChecks
{
    private static int _checks;
    private static readonly float4 Red = new(1, 0, 0, 1);
    private static readonly float4 Blue = new(0, 0, 1, 1);
    private static readonly string TestDirectory = Path.Combine(Path.GetTempPath(), "war-paint-checks-" + Guid.NewGuid().ToString("N"));

    private static void Main()
    {
        Directory.CreateDirectory(TestDirectory);
        try
        {
            Ownership();
            PresetPersistence();
            PresetEditing();
            PresetWriteFailures();
            NamedSceneRoundTrip();
            SceneRoundTrip();
            InvalidTargets();
            Compatibility();
            NativeOwnership();
            Console.WriteLine($"PASS: War Paint {_checks} ownership, preset, real-adapter scene and identity checks.");
        }
        finally { Directory.Delete(TestDirectory, recursive: true); }
    }

    private static WarPaintService NewService(out FakeBackend backend)
    {
        backend = new();
        return new WarPaintService(backend, new PaintPresetLibrary(Path.Combine(TestDirectory, Guid.NewGuid() + ".json")));
    }

    private static KittenEva Add(string id, string character = "Calico")
    {
        var kitten = new KittenEva(id, character);
        Universe.CurrentSystem!.All.Items.Add(kitten);
        return kitten;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static void Ownership()
    {
        Universe.CurrentSystem = new();
        var service = NewService(out var backend);
        var first = Add("first"); var second = Add("second");
        Check(service.Apply(first, Red) != null && service.Apply(second, Blue) != null, "paint two live kittens");
        Check(!first.Materials.Intersect(second.Materials).Any(), "every kitten owns distinct private slots");
        Check(service.UpdateMaterial(first, "CharacterModel", "stock/1", Blue), "edit a single material live");
        var snapshot = service.Snapshot(first)!;
        Check(snapshot.Materials.Single(m => m.Name == "stock/1").Color == Blue
            && snapshot.Materials.Single(m => m.Name == "stock/2").Color == Red, "individual color leaves sibling material alone");
        snapshot.Materials[0].Color = float4.One;
        Check(service.Snapshot(first)!.Materials[0].Color == Blue, "paint snapshots are detached from live state");
        Check(service.UpdateTint(second, float4.One)
            && service.Snapshot(first)!.Materials.Single(m => m.Name == "stock/2").Color == Red, "tint changes target only");
        Check(service.UpdateTint(first, Red) && service.Snapshot(first)!.Materials.All(m => m.Color == Red), "whole tint replaces individual colors");
        first.Materials[1] = 999; // Another owner changed a binding after War Paint applied.
        Check(service.Unset(first) && first.Materials.SequenceEqual(new[] { 1, 999 }), "unset restores only still-owned bindings");
        Check(backend.Events.IndexOf("restore:fixture_0") < backend.Events.IndexOf("release:fixture_0"), "restore original slots before returning private handles");
        Check(!service.Unset(first) && backend.Released == 1, "repeated unset never double releases");
        second.IsDisposed = true;
        backend.Events.Clear();
        service.Update();
        Check(!backend.Events.Any(e => e.StartsWith("restore:")) && backend.Released == 2, "disposed native target is pruned without touching old bindings");
        service.Cleanup(); service.Cleanup();
        Check(backend.Released == 2, "repeated shutdown owns no extra resources");
    }

    private static void PresetPersistence()
    {
        string path = Path.Combine(TestDirectory, "presets.json");
        var library = new PaintPresetLibrary(path);
        var settings = new PaintSettings { Tint = Red };
        Check(library.Save("Red team", settings), "save a named preset");
        settings.Tint = Blue;
        Check(library.TryGet("Red team", out var stored) && stored.Tint == Red, "saving copies caller data");
        stored.Tint = Blue;
        Check(library.TryGet("Red team", out stored) && stored.Tint == Red, "loading copies library data");
        var restarted = new PaintPresetLibrary(path);
        Check(restarted.TryGet("Red team", out stored) && stored.Tint == Red, "presets survive process/library restart");
        Check(!restarted.Save(" ", settings), "blank names are rejected");
        Check(!restarted.Save("invalid", new PaintSettings { Tint = new(float.NaN, 1, 1, 1) }), "nonfinite preset rejected");
        Check(restarted.Save("Red team", new PaintSettings { Tint = Blue }), "named replacement persists");
        Check(new PaintPresetLibrary(path).TryGet("Red team", out stored) && stored.Tint == Blue, "replacement survives restart");
        Check(restarted.Delete("Red team") && !new PaintPresetLibrary(path).TryGet("Red team", out _), "preset deletion persists");
    }

    private static void SceneRoundTrip()
    {
        Universe.CurrentSystem = new();
        var service = NewService(out var backend);
        var kitten = Add("saved");
        service.Apply(kitten, Red);
        service.UpdateMaterial(kitten, "CharacterModel", "stock/1", Blue);
        var coordinator = new SceneSaveCoordinator(new[] { service.SaveParticipant });
        var document = coordinator.Capture();
        Check(document.Features.ContainsKey("war-paint"), "active paints have their own scene record");
        Check(!document.Features["war-paint"].State.GetRawText().Contains("fixture_"), "scene recipes contain no process-local material set names");
        for (int pass = 0; pass < 2; pass++)
        {
            coordinator.PrepareLoad(document); coordinator.ResetWorld();
            Check(kitten.Materials.SequenceEqual(new[] { 1, 2 }), "scene reset restores old bindings synchronously");
            kitten.IsDisposed = true;
            Universe.CurrentSystem = new(); kitten = Add("saved");
            coordinator.RestoreWorld(); coordinator.FinishLoad();
            Check(coordinator.RetainedCount == 0 && service.GetActivePaints().Count == 1, "repeat restore binds exactly one reconstructed target");
            Check(service.Snapshot(kitten)!.Materials.Single(m => m.Name == "stock/1").Color == Blue, "individual color survives real adapter replay");
        }
        Check(backend.Created == 1 && backend.Reused == 2, "same native source handles safely reuse cached private slots");
        coordinator.PrepareLoad(null); coordinator.ResetWorld();
        kitten.IsDisposed = true; Universe.CurrentSystem = new();
        coordinator.RestoreWorld(); coordinator.FinishLoad(); service.Update();
        Check(service.GetActivePaints().Count == 0 && backend.Released == 1, "vanilla/new scene clears paint and retires unused cache");
        coordinator.PrepareLoad(document); coordinator.ResetWorld();
        coordinator.RestoreWorld(); coordinator.FinishLoad();
        Check(coordinator.RetainedCount == 1 && coordinator.Messages.Any(m => m.Contains("saved")), "missing target is diagnosed and original record retained");
        Check(coordinator.Capture().Features["war-paint"].State.GetRawText() == document.Features["war-paint"].State.GetRawText(), "unresolved paint is preserved on next save");
        service.Cleanup();
    }

    private static void InvalidTargets()
    {
        Universe.CurrentSystem = new();
        var service = NewService(out var backend);
        var stale = Add("same");
        Universe.CurrentSystem.All.Items.Clear(); var current = Add("same");
        Check(service.Apply(stale, Red) == null && service.Apply(current, Red) != null, "stale same-ID references never retarget new kitten");
        Add("same");
        Check(service.Apply(current, Red) == null, "ambiguous vehicle IDs are refused");
        Check(service.Apply(current, new PaintSettings { Tint = new(1, 1, float.PositiveInfinity, 1) }) == null, "invalid recipe cannot touch native state");
        Universe.CurrentSystem.All.Items.Clear(); current.IsDisposed = true;
        var failed = Add("failed"); backend.FailCreate = true;
        Check(service.Apply(failed, Red) == null && failed.Materials.SequenceEqual(new[] { 1, 2 }), "allocation failure leaves original kitten bindings intact");
        service.Cleanup();
    }
}
