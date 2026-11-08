using System;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.DohLib;
using MeowSci.DohLib.Spawning;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.WarPaintLib;

internal static partial class Checks
{
    private static void PaintMigrationChecks()
    {
        var submod = new DohSubmod();
        var coordinator = new SceneSaveCoordinator(submod.SaveParticipants.Append(WarPaintService.Shared.SaveParticipant));
        var payload = new DohSubmod.SavedDoh
        {
            UseCustomColor = true, TintColor = Red, UniquePerKitten = false,
            Kittens = Ids.Select((id, i) => new DohSubmod.SavedKitten
            {
                VehicleId = id, CharacterId = "Calico", Tint = i < 3 ? (i == 2 ? Blue : Red) : null,
                MaterialGroup = i < 2 ? "legacy-shared-group" : i == 2 ? "legacy-unique" : null,
                Materials = i == 2 ? new() { new() { Source = "CharacterModel", Name = "Body", Color = Green } } : new()
            }).ToList()
        };
        var legacy = new SaveDocument();
        legacy.Features.Add("doh", new SaveFeature { State = SaveJson.ToElement(payload) });
        string reservedName = "DOH legacy tint 3F80000000000000000000003F800000";
        WarPaintService.Shared.Presets.Save(reservedName, new PaintSettings { Tint = Green });
        Load(coordinator, legacy, Kittens(Ids));
        Require(coordinator.RetainedCount == 0 && submod.Registry.Count == 4,
            "legacy doh v1 restores all native kittens without spawning duplicates");
        Require(Set(submod, 0) != null && !ReferenceEquals(Set(submod, 0), Set(submod, 1)),
            "legacy shared MaterialGroup migrates to independently owned paint per kitten");
        Require(Set(submod, 0)!.TintColor == Red && Set(submod, 1)!.TintColor == Red,
            "shared-group migration preserves every kitten's effective color");
        Require(Set(submod, 2)!.TintColor == Blue && Set(submod, 2)!.Materials[0].Color == Green,
            "legacy tint and per-source/name material color migrate through production War Paint");
        Require(Set(submod, 3) == null, "a legacy stock kitten remains unpainted");
        string imported = submod.PresetName!;
        Require(imported.StartsWith("DOH legacy tint ", StringComparison.Ordinal)
            && WarPaintService.Shared.Presets.TryGet(imported, out var recipe) && recipe.Tint == Red,
            "an enabled legacy spawn tint imports a deterministic reusable preset");

        Require(imported == reservedName + " (1)" && WarPaintService.Shared.Presets.TryGet(reservedName, out var preserved)
            && preserved.Tint == Green, "legacy preset import resolves collisions without overwriting an existing global preset");
        var saved = RoundTrip(coordinator.Capture());
        var savedDoh = SaveJson.FromElement<DohSubmod.SavedDoh>(saved.Features["doh"].State);
        Require(saved.Features["doh"].Version == 1 && saved.Features.ContainsKey("war-paint"),
            "new capture preserves doh v1 and separately captures central paint ownership");
        Require(savedDoh.Kittens.All(k => k.Tint == null && k.Materials.Count == 0 && k.MaterialGroup == null),
            "new doh records do not duplicate central paint ownership");
        Require(savedDoh.UseCustomColor && !savedDoh.UniquePerKitten && savedDoh.TintColor == Red
            && savedDoh.SpawnPaint!.Tint == Red && savedDoh.PaintPresetName == imported,
            "legacy flags and spawn color survive alongside the new detached selector recipe");

        var oldSets = Ids.Take(3).Select(id => WarPaintService.Shared.TryGetPaint(VehicleProviderKitten(id)!, out var p) ? p.MaterialSet : null).ToArray();
        int clones = NativeKittenPaintBackend.Clones;
        submod.SelectedVehicle = VehicleProviderKitten(Ids[0]);
        Load(coordinator, saved, Kittens(Ids));
        Require(submod.SelectedVehicle == null && NativeKittenPaintBackend.Clones == clones,
            "same-scene replay clears UI target and reuses central detached private sets");
        Require(ReferenceEquals(Set(submod, 0), oldSets[0]) && ReferenceEquals(Set(submod, 1), oldSets[1]),
            "repeated load preserves independent central ownership without new allocations");
        Require(Set(submod, 2)!.Materials[0].Color == Green, "central scene replay preserves the migrated individual material");
        Load(coordinator, saved, Kittens(Ids));
        Require(NativeKittenPaintBackend.Clones == clones && submod.Registry.Count == 4,
            "another replay neither duplicates kittens nor compounds allocations");

        WarPaintService.Shared.Presets.Delete(imported);
        Load(coordinator, saved, Kittens(Ids));
        Require(submod.SpawnPaint!.Tint == Red && submod.PresetName == imported && coordinator.RetainedCount == 0,
            "scene snapshot restores spawn appearance even when its global preset disappears");

        Load(coordinator, StripGroups(legacy), Kittens(Ids));
        Require(!ReferenceEquals(Set(submod, 0), Set(submod, 1)) && Set(submod, 2)!.Materials[0].Color == Green,
            "pre-MaterialGroup v1 records migrate with the same independent colors");
        Load(coordinator, legacy, Kittens(Ids[0], Ids[1], Ids[3]));
        submod.NextFrame();
        Require(coordinator.Messages.Any(m => m.Contains("DOH kitten missing: " + Ids[2])) && coordinator.RetainedCount == 1,
            "unresolved legacy targets diagnose and retain their original record");
        Require(oldSets[2]!.IsReleased, "a missing target's detached private GPU slots are released by central ownership");

        var changedCharacters = Kittens(Ids);
        changedCharacters[0].Character = new CharacterReference("Other character");
        Load(coordinator, legacy, changedCharacters);
        Require(coordinator.RetainedCount == 1 && submod.Registry.Get(Ids[0]) == null
            && !WarPaintService.Shared.TryGetPaint(changedCharacters[0], out _)
            && coordinator.Messages.Any(m => m.Contains("DOH kitten character changed: " + Ids[0])),
            "a changed character is diagnosed and retained without applying or claiming the wrong target");

        Load(coordinator, WithGroup(legacy, " "), Kittens(Ids));
        Require(coordinator.RetainedCount == 1 && submod.Registry.Count == 0,
            "malformed legacy sharing identity is retained before replay");
        Load(coordinator, null, new Vehicle("Rocket"));
        submod.NextFrame();
        Require(submod.Registry.Count == 0 && WarPaintService.Shared.GetActivePaints().Count == 0
            && submod.Factory.CreatedSets.Count == 0 && submod.PresetName == null,
            "vanilla/new-scene reset clears DOH and central ownership and returns orphaned slots");

        // Disabled legacy color controls still represent saved user input; retain them losslessly.
        payload.Kittens.Clear(); payload.UseCustomColor = false; payload.UniquePerKitten = true; payload.TintColor = Green;
        legacy.Features["doh"].State = SaveJson.ToElement(payload);
        Load(coordinator, legacy);
        var disabled = SaveJson.FromElement<DohSubmod.SavedDoh>(coordinator.Capture().Features["doh"].State);
        Require(submod.PresetName == null && !disabled.UseCustomColor && disabled.UniquePerKitten && disabled.TintColor == Green,
            "disabled legacy controls stay inactive while preserving their saved tint and unique flag");
    }

    private static void PresetSnapshotChecks()
    {
        var recipe = new PaintSettings { Tint = Red, Materials = new() { new() { Source = "CharacterModel", Name = "Body", Color = Blue } } };
        var request = new SpawnRequest { PaintPresetName = "Saved palette", PaintSettings = recipe, TintColor = Green, PerKittenColors = new[] { Green } };
        recipe.Tint = Green; recipe.Materials[0].Color = Green;
        Require(request.PaintSettings!.Tint == Red && request.PaintSettings.Materials[0].Color == Blue,
            "spawn requests detach a supplied preset before deferred execution");
        var kittenRecipe = request.GetPaintForKitten(request.PaintSettings, 0)!;
        Require(kittenRecipe.Tint == Red && kittenRecipe.Materials[0].Color == Blue,
            "new preset recipe takes precedence over conflicting legacy per-kitten and base colors");
        kittenRecipe.Materials[0].Color = Green;
        Require(request.GetPaintForKitten(request.PaintSettings, 1)!.Materials[0].Color == Blue,
            "every batch kitten receives a detached recipe");
        var legacy = new SpawnRequest { TintColor = Red, PerKittenColors = new[] { Blue } };
        Require(legacy.GetPaintForKitten(null, 0)!.Tint == Blue && legacy.GetPaintForKitten(null, 1)!.Tint == Red,
            "legacy per-kitten colors still fall back to the legacy batch tint");
        Require(new SpawnRequest().GetPaintForKitten(null, 0) == null,
            "stock appearance requests do not allocate paint recipes");
        var exposed = request.PaintSettings!; exposed.Materials.Clear(); exposed.Tint = Blue;
        Require(request.PaintSettings!.Tint == Red && request.PaintSettings.Materials.Count == 1,
            "retrieving the request snapshot cannot mutate its frozen recipe");
    }
}
