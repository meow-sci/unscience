using System;
using System.Linq;
using HarmonyLib;
using MeowSci.KitchenSinkLib;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

internal static class CapsuleGlassSaveChecks
{
    private const string Id = "kitchen-sink-capsule-glass";
    private static int _checks;

    public static void Run(Harmony harmony)
    {
        CapsuleGlassExperiment.Apply(harmony);
        var coordinator = new SceneSaveCoordinator(new KitchenSinkSubmod().SaveParticipants);
        try
        {
            IvaForceRender.Enabled = false;
            CapsuleGlassExperiment.SetEnabled(true);
            var enabled = SaveJson.FromElement<SaveDocument>(SaveJson.ToElement(coordinator.Capture()));
            Require(enabled.Features[Id].Version == 1 && enabled.Features[Id].State.GetBoolean(), "new v1 record round trips as detached boolean");
            Require(!enabled.Features["kitchen-sink"].State.GetBoolean(), "legacy IVA preference remains independently saved");
            coordinator.PrepareLoad(enabled);
            Require(CapsuleGlassExperiment.Enabled, "prepare is nonmutating");
            coordinator.ResetWorld();
            Require(!CapsuleGlassExperiment.Enabled && !IvaForceRender.Required, "reset releases old-world visibility ownership");
            coordinator.RestoreWorld();
            coordinator.FinishLoad();
            Require(CapsuleGlassExperiment.Enabled && IvaForceRender.Required && !IvaForceRender.Enabled, "restore reacquires normal visibility ownership");

            CapsuleGlassExperiment.SetEnabled(false);
            IvaForceRender.Enabled = true;
            var disabled = coordinator.Capture();
            Load(enabled);
            Load(disabled);
            Require(!CapsuleGlassExperiment.Enabled && IvaForceRender.Enabled, "distinct scene restores independent switch combination");
            Load(enabled);
            Load(enabled);
            Require(CapsuleGlassExperiment.Enabled && IvaForceRender.Required && !IvaForceRender.Enabled, "A-B-A and repeated loads do not compound ownership");

            var legacy = new SaveDocument();
            legacy.Features["kitchen-sink"] = new SaveFeature { State = SaveJson.ToElement(true) };
            Load(legacy);
            Require(!CapsuleGlassExperiment.Enabled && !IvaForceRender.Required && IvaForceRender.Enabled, "old boolean-only saves keep experiment off");
            Load(enabled);
            Load(null);
            Require(!CapsuleGlassExperiment.Enabled && !IvaForceRender.Required && !IvaForceRender.Enabled, "vanilla/new scene resets both switches");

            foreach (var invalid in new[] { SaveJson.ToElement("true"), SaveJson.ToElement(new { Enabled = true }), SaveJson.ToElement(1) })
            {
                var bad = new SaveDocument();
                bad.Features[Id] = new SaveFeature { State = invalid };
                Load(bad);
                Require(!CapsuleGlassExperiment.Enabled && coordinator.RetainedCount == 1, "malformed data is rejected and retained");
            }
            var future = new SaveDocument();
            future.Features[Id] = new SaveFeature { Version = 99, State = SaveJson.ToElement(true) };
            Load(future);
            Require(!CapsuleGlassExperiment.Enabled && coordinator.RetainedCount == 1, "future version is retained without enabling");

            CapsuleGlassExperiment.Remove(harmony);
            Load(enabled);
            Require(!CapsuleGlassExperiment.Enabled && coordinator.RetainedCount == 1
                && coordinator.Messages.Any(x => x.Contains("render patches are unavailable")), "unavailable rendering reports a retained restore failure");
            Require(coordinator.Capture().Features[Id].State.GetBoolean(), "failed restoration cannot erase saved enabled state");
            CapsuleGlassExperiment.Apply(harmony);
            Load(enabled);
            Require(CapsuleGlassExperiment.Enabled && coordinator.RetainedCount == 0, "saved experiment recovers when rendering becomes available");
            CapsuleGlassExperiment.Remove(harmony);
            Require(!CapsuleGlassExperiment.Enabled && !IvaForceRender.Required, "unload releases toggle and visibility requirement");
        }
        finally
        {
            coordinator.ResetWorld();
            CapsuleGlassExperiment.Remove(harmony);
        }
        Console.WriteLine($"PASS: {_checks} capsule glass save/restore checks.");

        void Load(SaveDocument? document)
        {
            coordinator.PrepareLoad(document);
            coordinator.ResetWorld();
            coordinator.RestoreWorld();
            coordinator.FinishLoad();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Capsule glass saves: " + message);
        _checks++;
    }
}

namespace KSA
{
    // Renderer/ownership are exercised against the real helpers in ksa-upgrade.tests.
    // These compile-only model members keep this persistence suite native-free.
    public sealed class PartModel { public PartModelTemplate Template { get; } = new(); }
    public sealed class PartModelTemplate { public string Id = "fixture"; }
}
