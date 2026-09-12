using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MeowSci.KsaAbstractions.Persistence;

internal static class StorageTests
{
    private static int _checks;
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "unscience-save-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string universe = Path.Combine(directory, "universe.xml");
            File.WriteAllText(universe, "<Universe>first</Universe>");
            Check(SaveStorage.Read(directory) == null, "vanilla save");
            var doc = new SaveDocument();
            doc.Features["unknown"] = new() { Version = 7, State = SaveJson.ToElement(new Payload { Value = 42 }) };
            SaveStorage.Write(directory, doc);
            Check(SaveJson.FromElement<Payload>(SaveStorage.Read(directory)!.Features["unknown"].State).Value == 42, "round-trip");
            Check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "no temporary residue");
            File.WriteAllText(universe, "<Universe>different</Universe>");
            Throws(() => SaveStorage.Read(directory), "mismatched native file");
            File.WriteAllText(universe, "<Universe>first</Universe>");
            File.WriteAllText(Path.Combine(directory, SaveStorage.FileName), "{");
            Throws(() => SaveStorage.Read(directory), "truncated JSON");
            File.WriteAllText(Path.Combine(directory, SaveStorage.FileName), "{\"SchemaVersion\":1,\"SchemaVersion\":2}");
            Throws(() => SaveStorage.Read(directory), "duplicate properties rejected");
            doc.SchemaVersion = 2;
            Throws(() => SaveStorage.Write(directory, doc), "future document rejected");
            doc.SchemaVersion = 1;
            SaveStorage.Write(directory, doc);
            using (var stream = File.OpenWrite(Path.Combine(directory, SaveStorage.FileName))) stream.SetLength(SaveStorage.MaximumBytes + 1L);
            Throws(() => SaveStorage.Read(directory), "oversized file rejected");
            using (var overflow = System.Text.Json.JsonDocument.Parse("1e999"))
                Throws(() => SaveJson.FromElement<double>(overflow.RootElement), "overflow double rejected");
            using (var overflow = System.Text.Json.JsonDocument.Parse("1e100"))
                Throws(() => SaveJson.FromElement<float>(overflow.RootElement), "overflow float rejected");
            CoordinatorScenarios();
            Console.WriteLine($"save storage/coordinator: {_checks} checks passed");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void CoordinatorScenarios()
    {
        int live = 10;
        bool failCapture = false, failRestore = false, partial = false;
        var order = new List<string>();
        var first = new SaveParticipant<Payload>("first", () => failCapture ? throw new Exception("capture") : new() { Value = live },
            () => { order.Add("reset first"); live = 0; }, (p, c) =>
            {
                order.Add("restore first");
                if (failRestore) throw new Exception("restore");
                live = p.Value;
                if (partial) c.Warn("missing asset");
            }, order: 10, validate: p => { if (p.Value < 0) throw new Exception("invalid"); });
        var second = new SaveParticipant<Payload>("second", () => new() { Value = 20 }, () => order.Add("reset second"),
            (_, _) => order.Add("restore second"), order: 20);
        var coordinator = new SceneSaveCoordinator(new[] { first, second });
        var document = coordinator.Capture();
        document.Features["future"] = new() { Version = 4, State = SaveJson.ToElement(new Payload { Value = 500 }) };
        live = 99;
        coordinator.PrepareLoad(document);
        Check(live == 99, "preflight does not mutate live state");
        coordinator.ResetWorld();
        coordinator.RestoreWorld();
        coordinator.FinishLoad();
        Check(live == 10, "restored saved state");
        Check(order.SequenceEqual(new[] { "reset second", "reset first", "restore first", "restore second" }), "dependency ordering");
        Check(coordinator.Capture().Features["future"].Version == 4, "unknown feature retained");
        failCapture = true;
        Check(SaveJson.FromElement<Payload>(coordinator.Capture().Features["first"].State).Value == 10, "capture failure retains last good state");
        failCapture = false;
        coordinator.PrepareLoad(null); // Native XML fails before reset: retain current recovery records.
        coordinator.FinishLoad();
        Check(coordinator.Capture().Features.ContainsKey("future"), "aborted load retains old recovery state");
        failRestore = true;
        coordinator.PrepareLoad(document);
        coordinator.ResetWorld();
        coordinator.RestoreWorld();
        coordinator.FinishLoad();
        live = 80;
        Check(SaveJson.FromElement<Payload>(coordinator.Capture().Features["first"].State).Value == 10, "failed restore retained");
        Check(order.Last() == "restore second", "failure isolated");
        failRestore = false;
        partial = true;
        coordinator.PrepareLoad(document);
        coordinator.ResetWorld();
        coordinator.RestoreWorld();
        coordinator.FinishLoad();
        live = 70;
        Check(SaveJson.FromElement<Payload>(coordinator.Capture().Features["first"].State).Value == 10, "partial restore retained");
        coordinator.UseCurrentSetup();
        Check(SaveJson.FromElement<Payload>(coordinator.Capture().Features["first"].State).Value == 70, "explicit current setup replacement");
        Check(coordinator.Capture().Features.ContainsKey("future"), "current setup leaves unknown payload intact");
        partial = false;
        coordinator.PrepareLoad(null);
        coordinator.ResetWorld();
        coordinator.RestoreWorld();
        coordinator.FinishLoad();
        Check(live == 0 && coordinator.RetainedCount == 0, "vanilla load clears prior scene");
        var invalid = new SaveDocument();
        invalid.Features["first"] = new() { State = SaveJson.ToElement(new Payload { Value = -1 }) };
        coordinator.PrepareLoad(invalid);
        coordinator.ResetWorld();
        coordinator.RestoreWorld();
        coordinator.FinishLoad();
        Check(coordinator.RetainedCount == 1, "invalid feature payload retained");
        order.Clear();
        coordinator.RestoreWorld();
        Check(order.Count == 0, "restore actions consumed once");
    }

    public sealed class Payload { public int Value { get; set; } }
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static void Throws(Action action, string name)
    {
        try { action(); } catch (Exception) { _checks++; return; }
        throw new Exception("Expected failure: " + name);
    }
}
