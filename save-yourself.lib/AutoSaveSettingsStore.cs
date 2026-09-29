using System;
using System.IO;
using MeowSci.KsaAbstractions;
using Tomlyn;
using Tomlyn.Model;

namespace MeowSci.SaveYourselfLib;

/// <summary>
/// Global Unscience preference file for auto save: <c>.unscience/save-yourself.toml</c>. These settings
/// are not scene data and are deliberately kept out of the game-save sidecar, like the window layout.
/// </summary>
public sealed class AutoSaveSettingsStore
{
    public const string FileName = "save-yourself.toml";
    private const string Section = "auto_save";

    private readonly string _directory;
    private readonly string _filePath;

    /// <param name="directory">Folder holding the file; defaults to the shared <c>.unscience</c> data directory.</param>
    public AutoSaveSettingsStore(string? directory = null)
    {
        _directory = directory ?? KsaPaths.ModDataDir;
        _filePath = Path.Combine(_directory, FileName);
    }

    public string FilePath => _filePath;

    /// <summary>Reads the settings; a missing, unreadable or partial file yields defaults for what is absent or invalid.</summary>
    public AutoSaveSettings Load()
    {
        if (!File.Exists(_filePath)) return AutoSaveSettings.Default;
        try
        {
            if (!Toml.TryToModel<TomlTable>(File.ReadAllText(_filePath), out var root, out var diagnostics))
            {
                foreach (var diagnostic in diagnostics)
                    Console.WriteLine($"save-yourself: settings parse error: {diagnostic}");
                return AutoSaveSettings.Default;
            }
            if (!root.TryGetValue(Section, out var sectionObj) || sectionObj is not TomlTable section)
                return AutoSaveSettings.Default;

            var settings = AutoSaveSettings.Default;
            if (section.TryGetValue("prefix", out var prefixObj) && prefixObj is string prefix)
                settings = settings with { Prefix = prefix };
            if (section.TryGetValue("enabled", out var enabledObj) && enabledObj is bool enabled)
                settings = settings with { Enabled = enabled };
            if (section.TryGetValue("interval_seconds", out var intervalObj) && intervalObj is long interval)
                settings = settings with { IntervalSeconds = (int)Math.Clamp(interval, int.MinValue, int.MaxValue) };
            return settings.Normalized();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"save-yourself: failed to load settings: {ex.Message}");
            return AutoSaveSettings.Default;
        }
    }

    /// <summary>Writes the normalised settings; failures are logged and never thrown at the UI.</summary>
    public bool Save(AutoSaveSettings settings)
    {
        try
        {
            var normalized = settings.Normalized();
            Directory.CreateDirectory(_directory);
            var section = new TomlTable
            {
                ["prefix"] = normalized.Prefix,
                ["enabled"] = normalized.Enabled,
                ["interval_seconds"] = (long)normalized.IntervalSeconds
            };
            File.WriteAllText(_filePath, Toml.FromModel(new TomlTable { [Section] = section }));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"save-yourself: failed to save settings: {ex.Message}");
            return false;
        }
    }
}
