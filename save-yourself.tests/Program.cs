using System;
using System.Collections.Generic;
using MeowSci.SaveYourselfLib;

internal static class Checks
{
    private static int _checks;
    private static readonly DateTime Stamp = new(2026, 9, 28, 20, 30, 5);

    private static void Main()
    {
        CheckNaming();
        CheckSettings();
        CheckController();
        StoreChecks.Run();
        Console.WriteLine($"PASS: {_checks} Save Yourself checks; native save/UI acceptance remains in-game.");
    }

    private static void CheckNaming()
    {
        Require(AutoSaveNaming.BuildName("autosave_", Stamp) == "autosave_20260928T203005", "default prefix keeps a single separator");
        Require(AutoSaveNaming.BuildName("mission", Stamp) == "mission_20260928T203005", "plain prefix gets an underscore separator");
        Require(AutoSaveNaming.BuildName("run-", Stamp) == "run-20260928T203005", "hyphen-terminated prefix is kept as-is");
        Require(AutoSaveNaming.BuildName("x", Stamp).Length == 1 + AutoSaveNaming.SuffixLength, "suffix length constant matches the format");
        Require(AutoSaveNaming.BuildName(new string('a', AutoSaveSettings.MaxPrefixLength), Stamp).Length == 64,
            "longest prefix still fits KSA's 64-character save name");
    }

    private static void CheckSettings()
    {
        var defaults = AutoSaveSettings.Default;
        Require(!defaults.Enabled && defaults.IntervalSeconds == 30 && defaults.Prefix == "" && defaults.EffectivePrefix == "autosave_",
            "defaults: off, 30 s, empty prefix resolves to autosave_");
        Require(AutoSaveSettings.SanitizePrefix("My Save!#1 (b)_-") == "MySave1b_-", "sanitising keeps only alphanumerics, underscore and hyphen");
        Require(AutoSaveSettings.SanitizePrefix(null) == "" && AutoSaveSettings.SanitizePrefix("  ") == "", "null/whitespace sanitise to empty");
        Require(AutoSaveSettings.SanitizePrefix(new string('z', 100)).Length == AutoSaveSettings.MaxPrefixLength, "over-long prefixes are truncated");
        Require(AutoSaveSettings.IsValidPrefix("") && AutoSaveSettings.IsValidPrefix("a-b_c9") && !AutoSaveSettings.IsValidPrefix("a b")
            && !AutoSaveSettings.IsValidPrefix("é") && !AutoSaveSettings.IsValidPrefix(null) && !AutoSaveSettings.IsValidPrefix(new string('a', 49)),
            "prefix validation matches the allowed alphabet and length");
        Require(AutoSaveSettings.ClampInterval(1) == 5 && AutoSaveSettings.ClampInterval(301) == 300 && AutoSaveSettings.ClampInterval(42) == 42,
            "interval clamps to [5, 300]");
        var messy = new AutoSaveSettings { Prefix = "bad name", Enabled = true, IntervalSeconds = 0 }.Normalized();
        Require(messy.Prefix == "badname" && messy.IntervalSeconds == 5 && messy.Enabled && messy.IsValid, "normalisation yields a valid record");
    }

    private static void CheckController()
    {
        var written = new List<string>();
        bool canSave = true;
        bool succeed = true;
        var clock = Stamp;
        var controller = new AutoSaveController(() => canSave, name => { if (succeed) written.Add(name); return succeed; }, () => clock);

        for (int i = 0; i < 100; i++) controller.Tick(1);
        Require(written.Count == 0 && controller.SecondsUntilNextSave == 0, "disabled controller never saves and shows no countdown");

        controller.Configure(new AutoSaveSettings { Enabled = true, IntervalSeconds = 10 });
        Require(controller.SecondsUntilNextSave == 10, "enabling restarts the countdown");
        controller.Tick(9.5);
        Require(written.Count == 0 && Math.Abs(controller.SecondsUntilNextSave - 0.5) < 1e-9, "no save before the interval elapses");
        controller.Tick(0.5);
        Require(written.Count == 1 && written[0] == "autosave_20260928T203005" && controller.LastSaveName == written[0]
            && controller.SaveCount == 1 && controller.LastError == null, "save fires at the interval with the default prefix");
        Require(controller.SecondsUntilNextSave == 10, "countdown restarts after a save");

        clock = clock.AddSeconds(10);
        canSave = false;
        controller.Tick(10);
        controller.Tick(5);
        Require(written.Count == 1 && controller.IsWaitingForGame, "a due save waits while the game cannot save");
        canSave = true;
        controller.Tick(0);
        Require(written.Count == 2 && !controller.IsWaitingForGame && written[1] == "autosave_20260928T203015",
            "the held save fires as soon as the game allows it");

        controller.Configure(new AutoSaveSettings { Prefix = "orbit test", Enabled = true, IntervalSeconds = 5 });
        Require(controller.Settings.Prefix == "orbittest" && controller.SecondsUntilNextSave == 5, "reconfiguring sanitises and keeps the countdown length");
        controller.Tick(-1);
        controller.Tick(double.NaN);
        Require(controller.SecondsUntilNextSave == 5, "negative or NaN frame deltas are ignored");
        controller.Tick(5);
        Require(written[2] == "orbittest_20260928T203015", "custom prefix names use an underscore separator");

        succeed = false;
        controller.Tick(5);
        Require(written.Count == 3 && controller.LastError != null && controller.LastError.Contains("did not write") && controller.SaveCount == 3,
            "a refused save reports an error without counting");
        Require(controller.SecondsUntilNextSave == 5, "a failed save still restarts the countdown instead of retrying every frame");
        succeed = true;
        var throwing = new AutoSaveController(() => true, _ => throw new InvalidOperationException("disk"), () => clock);
        throwing.Configure(new AutoSaveSettings { Enabled = true });
        Require(!throwing.SaveNow() && throwing.LastError!.Contains("disk"), "exceptions from the game are reported, not propagated");

        Require(controller.SaveNow() && written.Count == 4 && controller.SecondsUntilNextSave == 5, "manual save uses the same naming and leaves the countdown");
        canSave = false;
        Require(!controller.SaveNow() && controller.LastError!.Contains("No world"), "manual save reports when the game cannot save");
        canSave = true;

        controller.Configure(controller.Settings with { Enabled = false });
        controller.Tick(100);
        Require(written.Count == 4 && controller.SecondsUntilNextSave == 0, "disabling stops timed saves");
        controller.Reset();
        Require(!controller.Settings.Enabled && controller.LastSaveName == null && controller.SaveCount == 0 && controller.LastError == null,
            "reset returns to defaults and clears history");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
}
