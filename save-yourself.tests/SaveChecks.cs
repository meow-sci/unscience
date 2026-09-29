using System;
using System.Linq;
using System.Text.Json;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.SaveYourselfLib;

internal static class SaveChecks
{
    private static int _checks;

    public static void Run()
    {
        var submod = new SaveYourselfSubmod();
        var coordinator = new SceneSaveCoordinator(submod.SaveParticipants);
        submod.Controller.Configure(new AutoSaveSettings { Prefix = "mission", Enabled = true, IntervalSeconds = 45 });
        submod.Controller.Tick(20);

        var savedA = SaveJson.FromElement<SaveDocument>(SaveJson.ToElement(coordinator.Capture()));
        Require(savedA.Features[SaveYourselfSubmod.SaveId].Version == 1, "record is version 1");
        var recordA = Saved(savedA);
        Require(recordA.Prefix == "mission" && recordA.Enabled && recordA.IntervalSeconds == 45, "JSON round-trip captures prefix, toggle and interval");
        Require(!savedA.Features[SaveYourselfSubmod.SaveId].State.TryGetProperty("EffectivePrefix", out _), "computed properties are not serialised");

        coordinator.PrepareLoad(savedA);
        Require(submod.Controller.Settings.Enabled && submod.Controller.SecondsUntilNextSave == 25, "prepare does not mutate live settings");
        coordinator.ResetWorld();
        Require(!submod.Controller.Settings.Enabled && submod.Controller.SecondsUntilNextSave == 0, "reset disables auto save before reconstruction");
        coordinator.RestoreWorld();
        coordinator.FinishLoad();
        Require(submod.Controller.Settings == recordA && submod.Controller.SecondsUntilNextSave == 45,
            "replay restores the settings and restarts the countdown");

        submod.Controller.Configure(submod.Controller.Settings with { Enabled = false, IntervalSeconds = 5 });
        var savedB = coordinator.Capture();
        Require(!Saved(savedB).Enabled && Saved(savedB).IntervalSeconds == 5, "edits are reflected in subsequent saves");
        Load(coordinator, savedA);
        Load(coordinator, savedB);
        Load(coordinator, savedA);
        Load(coordinator, savedA);
        Require(submod.Controller.Settings == recordA, "A-B-A and repeated loads restore each record exactly");

        Load(coordinator, new SaveDocument());
        Require(submod.Controller.Settings == AutoSaveSettings.Default && coordinator.RetainedCount == 0,
            "saves without a save-yourself record leave auto save off and retain nothing");
        submod.Controller.Configure(new AutoSaveSettings { Enabled = true });
        Load(coordinator, null);
        Require(!submod.Controller.Settings.Enabled, "vanilla loads clear old settings");

        var legacyFriendly = new SaveDocument();
        legacyFriendly.Features[SaveYourselfSubmod.SaveId] = new SaveFeature { State = JsonDocument.Parse("{\"Enabled\":true}").RootElement };
        Load(coordinator, legacyFriendly);
        Require(submod.Controller.Settings.Enabled && submod.Controller.Settings.IntervalSeconds == 30 && submod.Controller.Settings.Prefix == "",
            "absent optional fields fall back to defaults");

        foreach (string invalid in new[]
        {
            "{\"Prefix\":\"bad name\",\"Enabled\":true,\"IntervalSeconds\":30}",
            "{\"Prefix\":\"\",\"Enabled\":true,\"IntervalSeconds\":4}",
            "{\"Prefix\":\"\",\"Enabled\":true,\"IntervalSeconds\":301}",
            "{\"Prefix\":null,\"Enabled\":true,\"IntervalSeconds\":30}",
            "{\"IntervalSeconds\":\"30\"}",
            "[]",
        })
        {
            var bad = new SaveDocument();
            bad.Features[SaveYourselfSubmod.SaveId] = new SaveFeature { State = JsonDocument.Parse(invalid).RootElement };
            Load(coordinator, bad);
            Require(coordinator.RetainedCount == 1 && submod.Controller.Settings == AutoSaveSettings.Default,
                $"invalid record {invalid} is rejected before replay and retained");
        }
        Require(coordinator.Capture().Features.ContainsKey(SaveYourselfSubmod.SaveId), "a retained record is carried into the next capture");

        var future = new SaveDocument();
        future.Features[SaveYourselfSubmod.SaveId] = new SaveFeature { Version = 2, State = SaveJson.ToElement(recordA) };
        Load(coordinator, future);
        Require(submod.Controller.Settings == AutoSaveSettings.Default && coordinator.RetainedCount == 1
            && coordinator.Messages.Any(message => message.Contains(SaveYourselfSubmod.SaveId)),
            "unsupported record versions are retained with a diagnostic instead of guessed");

        Load(coordinator, savedA);
        Require(submod.Controller.Settings == recordA && coordinator.RetainedCount == 0, "a good record recovers after rejected ones");
        coordinator.ResetWorld();
        SaveYourselfSubmod.Written.Clear();
        Console.WriteLine($"PASS: {_checks} Save Yourself save/restore checks.");
    }

    private static AutoSaveSettings Saved(SaveDocument document) =>
        SaveJson.FromElement<AutoSaveSettings>(document.Features[SaveYourselfSubmod.SaveId].State);

    private static void Load(SceneSaveCoordinator coordinator, SaveDocument? document)
    {
        coordinator.PrepareLoad(document);
        coordinator.ResetWorld();
        coordinator.RestoreWorld();
        coordinator.FinishLoad();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
}
