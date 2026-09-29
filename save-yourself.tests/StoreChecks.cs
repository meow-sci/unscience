using System;
using System.IO;
using MeowSci.SaveYourselfLib;

internal static class StoreChecks
{
    private static int _checks;

    public static void Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "save-yourself.tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AutoSaveSettingsStore(dir);
            Require(store.FilePath == Path.Combine(dir, AutoSaveSettingsStore.FileName), "file lives in the given folder");
            Require(store.Load() == AutoSaveSettings.Default, "missing file yields defaults");

            var settings = new AutoSaveSettings { Prefix = "mission", Enabled = true, IntervalSeconds = 45 };
            Require(store.Save(settings) && File.Exists(store.FilePath), "save creates the folder and file");
            Require(store.Load() == settings, "round-trip restores prefix, toggle and interval");
            string text = File.ReadAllText(store.FilePath);
            Require(text.Contains("[auto_save]") && text.Contains("prefix = \"mission\"") && text.Contains("enabled = true")
                && text.Contains("interval_seconds = 45"), "file is a readable TOML section");

            store.Save(new AutoSaveSettings { Prefix = "bad name!", Enabled = true, IntervalSeconds = 9999 });
            Require(store.Load() == new AutoSaveSettings { Prefix = "badname", Enabled = true, IntervalSeconds = 300 },
                "saved settings are normalised on the way out");

            File.WriteAllText(store.FilePath, "[auto_save]\nenabled = true\n");
            Require(store.Load() == new AutoSaveSettings { Enabled = true }, "absent keys fall back to defaults");
            File.WriteAllText(store.FilePath, "[auto_save]\nprefix = \"a b\"\nenabled = \"yes\"\ninterval_seconds = 1\n");
            Require(store.Load() == new AutoSaveSettings { Prefix = "ab", Enabled = false, IntervalSeconds = 5 },
                "wrong types are ignored and out-of-range values are clamped");
            File.WriteAllText(store.FilePath, "[auto_save]\ninterval_seconds = 99999999999\n");
            Require(store.Load().IntervalSeconds == 300, "huge integers clamp instead of overflowing");
            File.WriteAllText(store.FilePath, "[other]\nenabled = true\n");
            Require(store.Load() == AutoSaveSettings.Default, "a file without the section yields defaults");
            File.WriteAllText(store.FilePath, "this is = = not toml [[");
            Require(store.Load() == AutoSaveSettings.Default, "malformed TOML yields defaults without throwing");

            var readOnly = new AutoSaveSettingsStore(Path.Combine(store.FilePath, "impossible"));
            Require(!readOnly.Save(settings), "an unwritable location reports failure instead of throwing");

            var defaultStore = new AutoSaveSettingsStore();
            Require(defaultStore.FilePath.EndsWith(Path.Combine(".unscience", AutoSaveSettingsStore.FileName), StringComparison.Ordinal),
                "default store targets the shared .unscience data folder");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        Console.WriteLine($"PASS: {_checks} Save Yourself settings-file checks.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
}
