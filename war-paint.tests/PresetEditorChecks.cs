using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using KSA;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.WarPaintLib;

internal static partial class WarPaintChecks
{
    private static void PresetEditing()
    {
        Universe.CurrentSystem = new();
        var service = NewService(out var backend);
        var editor = new PaintPresetEditor(service);
        Check(editor.SelectedName == null && !editor.HasChanges && !editor.Save() && !editor.Preview(),
            "an unselected editor cannot save or paint anonymously");
        Check(!editor.Create(" "), "new preset rejects a blank name");
        Check(editor.Create(" Red team ") && editor.SelectedName == "Red team" && !editor.HasChanges,
            "new preset creates and selects a clean named library entry");
        Check(!editor.Create("Red team") && editor.SelectedName == "Red team", "new preset cannot replace an existing name");
        var first = Add("first"); var second = Add("second"); var other = Add("other");
        editor.DiscoverMaterials();
        Check(editor.Draft.Materials.Count == 2 && !editor.HasChanges && backend.Created == 0
            && first.Materials.SequenceEqual(new[] { 1, 2 }), "material discovery never allocates, paints or dirties a tint-only preset");
        Check(service.Apply(first, editor.Draft, editor.SelectedName) != null
            && service.Apply(second, editor.Draft, editor.SelectedName) != null, "one preset can be assigned to multiple exact kittens");
        Check(editor.Create("Blue team"), "create another preset");
        editor.Draft.Tint = Blue;
        foreach (var m in editor.Draft.Materials) m.Color = Blue;
        Check(editor.HasChanges && editor.Save() && !editor.HasChanges && service.ApplyPreset(other, "Blue team") != null,
            "saving commits all material colors and clears the dirty state");
        Check(editor.Select("Red team") && !editor.HasChanges, "select a saved preset independently of any kitten target");
        editor.Draft.Materials[0].Color = Red;
        Check(editor.HasChanges && editor.Preview(), "editing a preset marks it dirty and previews immediately");
        Check(service.Snapshot(first)!.Materials[0].Color == Red && service.Snapshot(second)!.Materials[0].Color == Red
            && service.Snapshot(other)!.Materials[0].Color == Blue, "preview affects all assigned kittens and no other preset");
        Check(service.Presets.TryGet("Red team", out var stored) && stored.Materials.Count == 0,
            "live previews do not save draft colors to the global library");
        editor.Draft.Materials[0].Color = Brutal.Numerics.float4.One;
        Check(!editor.HasChanges, "returning every color to its saved value disables Save Changes");
        editor.Draft.Materials[0].Color = Red;
        editor.Preview();
        Check(editor.Select("Blue team") && service.Snapshot(first)!.Materials[0].Color == Brutal.Numerics.float4.One
            && service.Snapshot(second)!.Materials[0].Color == Brutal.Numerics.float4.One,
            "switching presets rolls back unsaved previews on every assignment");
        editor.Select("Red team");
        editor.Draft.Materials[0].Color = Red;
        editor.Preview();
        Check(editor.Save() && !editor.HasChanges && service.Presets.TryGet("Red team", out stored)
            && stored.Materials[0].Color == Red, "Save Changes persists the detached recipe");
        editor.Draft.Materials[0].Color = Blue;
        Check(editor.Select("Blue team") && service.Snapshot(first)!.Materials[0].Color == Red,
            "discard returns to the most recently saved preset colors");
        Check(editor.Select("Red team") && editor.Delete() && editor.SelectedName == null && !editor.HasChanges,
            "confirmed deletion removes the preset and clears the editor selection");
        Check(service.GetActivePaints().Count == 1 && service.GetActivePaints()[0].PresetName == "Blue team"
            && first.Materials.SequenceEqual(new[] { 1, 2 }) && second.Materials.SequenceEqual(new[] { 1, 2 }),
            "deletion unsets every matching kitten and preserves unrelated assignments");
        Check(!service.Presets.TryGet("Red team", out _) && new PaintPresetLibrary(service.Presets.FilePath).Names.SequenceEqual(new[] { "Blue team" }),
            "preset deletion survives library restart");
        Check(service.ApplyPreset(first, "missing") == null && first.Materials.SequenceEqual(new[] { 1, 2 }),
            "a missing named preset cannot silently paint a kitten");
        service.Cleanup();
    }

    private static void PresetWriteFailures()
    {
        Universe.CurrentSystem = new();
        var service = NewService(out _);
        var kitten = Add("write-failure");
        var editor = new PaintPresetEditor(service);
        editor.Create("Keep me");
        service.ApplyPreset(kitten, "Keep me");
        editor.Draft.Tint = Red;
        foreach (var material in editor.Draft.Materials) material.Color = Red;
        editor.Preview();
        string path = service.Presets.FilePath;
        File.Delete(path);
        Directory.CreateDirectory(path); // A deterministic replacement failure, without relying on permissions.
        Check(!editor.Save() && editor.HasChanges && service.Snapshot(kitten)!.Tint == Red,
            "a failed save preserves the dirty draft and its live preview");
        Check(!editor.Create("Another preset") && editor.SelectedName == "Keep me" && editor.HasChanges
            && service.Snapshot(kitten)!.Tint == Red, "failed creation preserves the previous preset's unsaved live preview");
        Check(!editor.Delete() && editor.SelectedName == "Keep me" && service.TryGetPaint(kitten, out _),
            "a failed preset deletion leaves selection and kitten assignments intact");
        Directory.Delete(path);
        Check(editor.Save() && !editor.HasChanges && editor.Delete(), "failed preset writes can be retried normally");
        service.Cleanup();
    }

    private static void NamedSceneRoundTrip()
    {
        Universe.CurrentSystem = new();
        var service = NewService(out _);
        var kitten = Add("named");
        service.Presets.Save("Saved name", new PaintSettings { Tint = Red });
        service.ApplyPreset(kitten, "Saved name");
        var editor = new PaintPresetEditor(service);
        editor.Select("Saved name");
        editor.Draft.Materials[0].Color = Blue;
        editor.Preview();
        var coordinator = new SceneSaveCoordinator(new[] { service.SaveParticipant });
        var document = coordinator.Capture();
        var record = SaveJson.FromElement<WarPaintService.SavedWarPaint>(document.Features["war-paint"].State);
        Check(record.Kittens[0].PresetName == "Saved name" && record.Kittens[0].Paint.Materials[0].Color == Blue,
            "scene capture stores preset identity and effective unsaved preview colors");
        for (int pass = 0; pass < 2; pass++)
        {
            coordinator.PrepareLoad(document); coordinator.ResetWorld();
            kitten.IsDisposed = true;
            Universe.CurrentSystem = new(); kitten = Add("named");
            coordinator.RestoreWorld(); coordinator.FinishLoad();
            Check(coordinator.RetainedCount == 0 && service.GetActivePaints().Single().PresetName == "Saved name"
                && service.Snapshot(kitten)!.Materials[0].Color == Blue, "repeat scene replay preserves assignment and exact effective colors");
        }
        Check(service.Presets.TryGet("Saved name", out var saved) && saved.Tint == Red && saved.Materials.Count == 0,
            "scene replay does not overwrite an existing global preset with unsaved scene colors");
        service.Presets.Delete("Saved name");
        coordinator.PrepareLoad(document); coordinator.ResetWorld();
        kitten.IsDisposed = true;
        Universe.CurrentSystem = new(); kitten = Add("named");
        coordinator.RestoreWorld(); coordinator.FinishLoad();
        Check(coordinator.RetainedCount == 0 && service.Presets.TryGet("Saved name", out saved)
            && saved.Materials[0].Color == Blue, "a missing global preset is recovered from its detached scene recipe");

        var node = JsonNode.Parse(document.Features["war-paint"].State.GetRawText())!;
        node["Kittens"]![0]!.AsObject().Remove("PresetName");
        var legacy = new SaveDocument();
        legacy.Features["war-paint"] = new() { Version = 1, State = SaveJson.ToElement(node) };
        coordinator.PrepareLoad(legacy); coordinator.ResetWorld();
        kitten.IsDisposed = true;
        Universe.CurrentSystem = new(); kitten = Add("named");
        coordinator.RestoreWorld(); coordinator.FinishLoad();
        Check(coordinator.RetainedCount == 0 && !string.IsNullOrWhiteSpace(service.GetActivePaints().Single().PresetName)
            && service.Snapshot(kitten)!.Materials[0].Color == Blue, "earlier v1 recipes without names migrate without anonymous paint or color loss");
        Check(service.DeletePreset(service.GetActivePaints().Single().PresetName) && service.GetActivePaints().Count == 0,
            "restored and migrated assignments participate in preset deletion");
        node["Kittens"]![0]!["PresetName"] = " ";
        var invalid = new SaveDocument();
        invalid.Features["war-paint"] = new() { Version = 1, State = SaveJson.ToElement(node) };
        coordinator.PrepareLoad(invalid); coordinator.ResetWorld();
        coordinator.RestoreWorld(); coordinator.FinishLoad();
        Check(coordinator.RetainedCount == 1 && service.GetActivePaints().Count == 0
            && coordinator.Messages.Any(m => m.Contains("Invalid War Paint preset name")),
            "malformed additive preset identity is retained without mutating native paint");
        service.Cleanup();
    }
}
