using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.WarPaintLib;

internal static partial class WarPaintChecks
{
    private static void Compatibility()
    {
        Universe.CurrentSystem = new();
        var service = NewService(out var backend);
        backend.CharacterNames = true;
        var calico = Add("calico"); var other = Add("other", "OtherCharacter");
        service.Apply(calico, Red);
        service.UpdateMaterial(calico, "CharacterModel", "Calico/1", Blue);
        var recipe = service.Snapshot(calico)!;
        Check(recipe.Materials.All(m => m.Slot >= 0), "snapshot records portable renderable material roles");
        Check(service.Apply(other, recipe) != null, "preset applies across character-specific material asset names");
        Check(service.Snapshot(other)!.Materials.Single(m => m.Name == "OtherCharacter/1").Color == Blue,
            "portable preset preserves target asset identity and corresponding slot color");
        var incompatible = recipe.Clone(); incompatible.Materials[0].Slot = 999;
        Check(service.Apply(other, incompatible) == null
            && service.Snapshot(other)!.Materials.Single(m => m.Name == "OtherCharacter/1").Color == Blue,
            "incompatible material roles fail without repainting an existing target");
        service.Cleanup();

        Universe.CurrentSystem = new();
        service = NewService(out backend);
        var first = Add("legacy-a"); var second = Add("legacy-b");
        var messages = new List<string>();
        Check(service.ImportLegacyColors(new() { ["stock/1"] = Blue }, new SaveRestoreContext(messages.Add)),
            "legacy Humble material name resolves onto each explicit live kitten");
        Check(service.GetActivePaints().Count == 2 && !first.Materials.Intersect(second.Materials).Any(),
            "legacy global colors become independent per-kitten ownership");
        Check(service.Snapshot(first)!.Materials.Single(m => m.Name == "stock/1").Color == Blue
            && service.Snapshot(second)!.Materials.Single(m => m.Name == "stock/1").Color == Blue,
            "legacy material colors preserve effective appearance for all matching targets");
        Check(!service.ImportLegacyColors(new() { ["unavailable/asset"] = Red }, new SaveRestoreContext(messages.Add))
            && messages.Any(m => m.Contains("unavailable/asset")), "unresolved legacy material is diagnosed");
        Check(service.ApplyLegacy(first, new PaintSettings { Tint = Red }, new SaveRestoreContext(messages.Add)),
            "legacy DOH tint enters normal War Paint ownership");
        var coordinator = new SceneSaveCoordinator(new[] { service.SaveParticipant });
        var saved = coordinator.Capture();
        var node = JsonNode.Parse(saved.Features["war-paint"].State.GetRawText())!;
        // Drop all active native targets to isolate the invalid-record retention contract.
        node["Kittens"]![0]!["Paint"]!["Materials"]![0]!["Slot"] = -2;
        var invalid = new SaveDocument();
        invalid.Features["war-paint"] = new() { Version = 1, State = SaveJson.ToElement(node) };
        coordinator.PrepareLoad(invalid); coordinator.ResetWorld();
        first.IsDisposed = second.IsDisposed = true; Universe.CurrentSystem = new();
        coordinator.RestoreWorld(); coordinator.FinishLoad(); service.Update();
        Check(coordinator.RetainedCount == 1 && service.GetActivePaints().Count == 0,
            "malformed new paint record is retained without native mutation");
        Check(coordinator.Messages.Any(m => m.Contains("invalid saved state")), "invalid recipe emits save diagnostics");
        service.Cleanup();

        string damaged = Path.Combine(TestDirectory, "damaged.json");
        File.WriteAllText(damaged, "{damaged");
        var library = new PaintPresetLibrary(damaged);
        Check(library.Names.Count == 0 && library.LastError != null, "corrupt preset library reports load failure");
        Check(!library.Save("overwrite", new()) && File.ReadAllText(damaged) == "{damaged",
            "failed library load never overwrites earlier preset bytes");
        string future = Path.Combine(TestDirectory, "future.json");
        File.WriteAllText(future, "{\"Version\":99,\"Presets\":{}}");
        library = new PaintPresetLibrary(future);
        Check(!library.Save("overwrite", new()) && File.ReadAllText(future).Contains("99"),
            "future preset format is preserved for a compatible build");
    }
}
