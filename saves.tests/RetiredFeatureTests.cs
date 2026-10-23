using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MeowSci.KsaAbstractions.Persistence;

/// <summary>
/// Sidecars written while the retired Parts Now submod was registered carry a <c>parts-now</c>
/// record (version 1, a <c>SavedDependency[]</c> list; written even when no part mod was loaded).
/// The submod was removed at KSA 5638, so no participant owns that id any more. These checks pin
/// that such saves still load through the real storage and coordinator: live features restore, the
/// orphaned record is reported and retained (never a load failure), and it is carried forward
/// unchanged by later captures, by "use current setup" and by a rewrite.
/// </summary>
internal static class RetiredFeatureTests
{
    // Exact DTO shape the removed PartsNowSubmod.Saves adapter serialized through SaveJson.
    private sealed record SavedDependency(string ModId, string[] PartIds);

    private sealed class Payload { public int Value { get; set; } }

    private static int _checks;

    public static void Run()
    {
        LegacyPartsNowRecordLoads(Array.Empty<SavedDependency>());
        LegacyPartsNowRecordLoads(new[] { new SavedDependency("my-parts", new[] { "MyTank", "MyEngine" }) });
        Console.WriteLine($"Retired feature records: {_checks} checks passed.");
    }

    private static void LegacyPartsNowRecordLoads(SavedDependency[] dependencies)
    {
        string directory = Path.Combine(Path.GetTempPath(), "unscience-retired-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "universe.xml"), "<Universe>legacy</Universe>");
            var legacy = new SaveDocument();
            legacy.Features["parts-now"] = new() { Version = 1, State = SaveJson.ToElement(dependencies) };
            legacy.Features["live"] = new() { Version = 1, State = SaveJson.ToElement(new Payload { Value = 7 }) };
            SaveStorage.Write(directory, legacy);
            JsonElement legacyState = legacy.Features["parts-now"].State;

            int live = 0;
            var participant = new SaveParticipant<Payload>("live", () => new Payload { Value = live },
                () => live = 0, (payload, _) => live = payload.Value);
            var coordinator = new SceneSaveCoordinator(new[] { participant });
            coordinator.PrepareLoad(SaveStorage.Read(directory));
            coordinator.ResetWorld();
            coordinator.RestoreWorld();
            coordinator.FinishLoad();

            Check(live == 7, "live feature restores beside the retired record");
            Check(coordinator.Status.StartsWith("Restored 1 feature records"), "load completes instead of failing");
            Check(coordinator.RetainedCount == 1, "retired record retained");
            Check(coordinator.Messages.Count(m => m.StartsWith("parts-now: unsupported feature")) == 1, "retired record reported once");

            live = 9;
            var next = coordinator.Capture();
            Check(JsonElement.DeepEquals(next.Features["parts-now"].State, legacyState), "retired record carried forward unchanged");
            Check(SaveJson.FromElement<Payload>(next.Features["live"].State).Value == 9, "live feature captures current edits");

            coordinator.UseCurrentSetup();
            Check(coordinator.Capture().Features.ContainsKey("parts-now"), "use current setup keeps the unowned record");

            SaveStorage.Write(directory, next);
            var reread = SaveJson.FromElement<SavedDependency[]>(SaveStorage.Read(directory)!.Features["parts-now"].State);
            Check(reread.Length == dependencies.Length
                && reread.Zip(dependencies).All(p => p.First.ModId == p.Second.ModId && p.First.PartIds.SequenceEqual(p.Second.PartIds)),
                "retired record survives a rewrite");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }
}
