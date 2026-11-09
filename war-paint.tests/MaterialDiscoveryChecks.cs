using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.WarPaintLib;

internal static partial class WarPaintChecks
{
    private static void DelayedMaterialDiscovery()
    {
        Universe.CurrentSystem = new();
        var service = NewService(out var backend);
        var kitten = Add("initializing");
        backend.DiscoverFur = backend.MaterialNamesUnavailable = true;
        var editor = new PaintPresetEditor(service);
        Check(editor.Create("Early catalog") && editor.Draft.Materials.Count == 1
            && editor.Draft.Materials[0].Source == "Fur", "reproduce a first-initialization catalog with only CatFur available");
        editor.Draft.Tint = Red;
        editor.Draft.Materials[0].Color = Blue;
        backend.MaterialNamesUnavailable = false;
        editor.DiscoverMaterials();
        Check(editor.Draft.Materials.Count == 3
            && editor.Draft.Materials.Count(m => m.Source == "CharacterModel") == 2,
            "retry extends a nonempty partial catalog when body materials become ready");
        Check(editor.Draft.Materials.Single(m => m.Source == "Fur").Color == Blue
            && editor.Draft.Materials.Where(m => m.Source == "CharacterModel").All(m => m.Color == Red)
            && editor.HasChanges, "discovery preserves edited rows and initializes new rows from the current whole-material color");
        Check(service.Presets.TryGet("Early catalog", out var saved) && saved.Materials.Count == 1
            && saved.Materials[0].Color == float4.One, "background discovery never silently saves draft edits");
        int lookups = backend.MaterialNameLookups;
        editor.DiscoverMaterials();
        backend.MaterialNamesUnavailable = true;
        editor.DiscoverMaterials();
        Check(editor.Draft.Materials.Count == 3 && backend.MaterialNameLookups == lookups,
            "repeated discovery neither duplicates rows nor resolves names for known roles, even if assets become temporarily unavailable");
        Check(backend.Created == 0 && kitten.Materials.SequenceEqual(new[] { 1, 2 }),
            "incremental discovery performs no material allocation or native binding writes");
        editor.Draft.Tint = float4.One;
        foreach (var material in editor.Draft.Materials) material.Color = float4.One;
        Check(!editor.HasChanges, "newly discovered rows extend the saved baseline without introducing spurious unsaved changes");
        Check(editor.Save() && service.Presets.TryGet("Early catalog", out saved) && saved.Materials.Count == 3,
            "an explicit save persists the completed material catalog");
        backend.FailInitialize = true;
        Check(editor.Create("Before bridge initialization") && editor.Draft.Materials.Count == 0,
            "a preset created before native bridge initialization keeps a clean empty catalog");
        backend.FailInitialize = backend.MaterialNamesUnavailable = false;
        editor.DiscoverMaterials();
        Check(editor.Draft.Materials.Count == 3 && !editor.HasChanges && backend.Created == 0,
            "discovery also retries native bridge initialization and completes without painting or false dirty state");
        service.Cleanup();
    }

    private static void PresetDeselection()
    {
        Universe.CurrentSystem = new();
        var service = NewService(out _);
        var kitten = Add("deselect");
        var editor = new PaintPresetEditor(service);
        editor.Create("Team");
        service.ApplyPreset(kitten, "Team");
        editor.Draft.Materials[0].Color = Blue;
        editor.Preview();
        Check(editor.Deselect() && editor.SelectedName == null && editor.Draft.Materials.Count == 0
            && !editor.HasChanges, "None clears preset selection, draft and dirty state");
        Check(service.TryGetPaint(kitten, out var paint) && paint.PresetName == "Team"
            && service.Snapshot(kitten)!.Materials[0].Color == float4.One,
            "deselection discards unsaved live preview while preserving the named kitten assignment");
        Check(!editor.Preview() && !editor.Save(), "deselection disables editing and saving until another preset is selected");
        Check(editor.Select("Team") && !editor.HasChanges && editor.Draft.Materials[0].Color == float4.One,
            "a deselected preset can be selected again with its saved colors");
        service.Cleanup();
    }
}
