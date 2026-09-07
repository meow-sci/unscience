using System;
using System.IO;
using MeowSci.GarrysTorchLib;
using MeowSci.KsaAbstractions;

internal static class PresetChecks
{
    public static void Run()
    {
        try
        {
            var directory = Path.Combine(KsaPaths.UserDataDir, ".unscience");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "garrys-torch-presets.toml"),
                "[presets.legacy]\nscale = 1.0\nlock_rotation = true\n");
            var manager = new PresetManager();
            manager.Initialize();
            Require(manager.GetPreset("legacy") is { Collisions: false }, "legacy preset defaults collisions off");
            Require(manager.SavePreset("enabled", new WeldPreset { Scale = WeldScale.Identity, Collisions = true }),
                "save collision opt-in");
            Require(manager.SavePreset("disabled", new WeldPreset { Scale = WeldScale.Identity }), "save collisions off");
            var reloaded = new PresetManager();
            reloaded.Initialize();
            Require(reloaded.GetPreset("enabled") is { Collisions: true }, "round-trip opt-in");
            Require(reloaded.GetPreset("disabled") is { Collisions: false }, "round-trip default");
            Require(reloaded.GetPreset("legacy") is { Collisions: false }, "preserve migrated default");
            Console.WriteLine("PASS: collision preset migration and round-trip");
        }
        finally { Directory.Delete(KsaPaths.UserDataDir, recursive: true); }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}

namespace MeowSci.KsaAbstractions
{
    internal static class KsaPaths
    {
        public static string UserDataDir { get; } = Path.Combine(Path.GetTempPath(), "weld-presets-" + Guid.NewGuid());
    }
}
