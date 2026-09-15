using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brutal.Numerics;
using Tomlyn;
using Tomlyn.Model;
using MeowSci.KsaAbstractions;

namespace MeowSci.PyroLib;

/// <summary>Manages named plume presets persisted to a TOML file.</summary>
public sealed class PlumePresetManager
{
    private readonly string _configDir;
    private readonly string _filePath;
    private readonly Dictionary<string, PlumePreset> _presets = new();
    private string[] _cachedNames = Array.Empty<string>();
    private bool _cacheValid;

    public PlumePresetManager()
    {
        _configDir = Path.Combine(KsaPaths.UserDataDir, ".unscience");
        _filePath = Path.Combine(_configDir, "pyro-presets.toml");
    }

    public void Initialize()
    {
        try
        {
            Directory.CreateDirectory(_configDir);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"pyro: Failed to create config directory: {ex.Message}");
        }
        Load();
    }

    public string[] GetPresetNames()
    {
        if (!_cacheValid)
        {
            _cachedNames = _presets.Keys
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _cacheValid = true;
        }
        return _cachedNames;
    }

    /// <summary>Returns a copy of the named preset, so callers can't mutate the stored one.</summary>
    public PlumePreset? GetPreset(string name)
    {
        return _presets.TryGetValue(name, out var preset) ? preset.Clone() : null;
    }

    public bool PresetExists(string name)
    {
        return _presets.ContainsKey(name);
    }

    public bool SavePreset(string name, PlumePreset preset)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = preset.Clone();
        normalized.TemplateId = PlumeTemplates.NormalizeId(normalized.TemplateId);
        _presets[name] = normalized;
        _cacheValid = false;
        Save();
        Console.WriteLine($"pyro: Saved preset '{name}'");
        return true;
    }

    public bool DeletePreset(string name)
    {
        if (!_presets.Remove(name))
            return false;

        _cacheValid = false;
        Save();
        Console.WriteLine($"pyro: Deleted preset '{name}'");
        return true;
    }

    private void Load()
    {
        _presets.Clear();
        _cacheValid = false;

        try
        {
            if (!File.Exists(_filePath))
                return;

            var toml = File.ReadAllText(_filePath);
            var model = Toml.ToModel(toml);

            if (model.TryGetValue("presets", out var p) && p is TomlTable presetsTable)
            {
                foreach (var (name, value) in presetsTable)
                {
                    if (value is TomlTable entry)
                        _presets[name] = ReadPreset(entry);
                }
            }

            Console.WriteLine($"pyro: Loaded {_presets.Count} preset(s)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"pyro: Failed to load presets: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var root = new TomlTable();
            var presetsTable = new TomlTable();

            foreach (var (name, preset) in _presets)
                presetsTable[name] = WritePreset(preset);

            root["presets"] = presetsTable;
            Directory.CreateDirectory(_configDir);
            File.WriteAllText(_filePath, Toml.FromModel(root));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"pyro: Failed to save presets: {ex.Message}");
        }
    }

    private static PlumePreset ReadPreset(TomlTable entry)
    {
        var nozzleDefaults = new NozzleSettings();
        return new PlumePreset
        {
            TemplateId = PlumeTemplates.NormalizeId(GetString(entry, "template", "EngineALarge")),
            Position = new float3(
                GetFloat(entry, "position_x"),
                GetFloat(entry, "position_y"),
                GetFloat(entry, "position_z")),
            Rotation = new float3(
                GetFloat(entry, "rotation_x"),
                GetFloat(entry, "rotation_y"),
                GetFloat(entry, "rotation_z")),
            Throttle = Math.Clamp(GetFloat(entry, "throttle", 1f), 0f, 1f),
            Nozzle = new NozzleSettings
            {
                ExitRadius = GetFloat(entry, "exit_radius", nozzleDefaults.ExitRadius),
                ThroatRadius = GetFloat(entry, "throat_radius", nozzleDefaults.ThroatRadius),
                ChamberPressureBar = GetFloat(entry, "chamber_pressure_bar", nozzleDefaults.ChamberPressureBar),
                ChamberTemperatureK = GetFloat(entry, "chamber_temperature_k", nozzleDefaults.ChamberTemperatureK),
                Gamma = GetFloat(entry, "gamma", nozzleDefaults.Gamma),
                GasConstant = GetFloat(entry, "gas_constant", nozzleDefaults.GasConstant),
            },
            AbsorptionDensityScale = GetFloat(entry, "absorption_density_scale", 1f),
            RefractionIntensity = GetFloat(entry, "refraction_intensity", 1f),
        };
    }

    private static TomlTable WritePreset(PlumePreset preset) => new()
    {
        ["template"] = PlumeTemplates.NormalizeId(preset.TemplateId),
        ["position_x"] = (double)preset.Position.X,
        ["position_y"] = (double)preset.Position.Y,
        ["position_z"] = (double)preset.Position.Z,
        ["rotation_x"] = (double)preset.Rotation.X,
        ["rotation_y"] = (double)preset.Rotation.Y,
        ["rotation_z"] = (double)preset.Rotation.Z,
        ["throttle"] = (double)preset.Throttle,
        ["exit_radius"] = (double)preset.Nozzle.ExitRadius,
        ["throat_radius"] = (double)preset.Nozzle.ThroatRadius,
        ["chamber_pressure_bar"] = (double)preset.Nozzle.ChamberPressureBar,
        ["chamber_temperature_k"] = (double)preset.Nozzle.ChamberTemperatureK,
        ["gamma"] = (double)preset.Nozzle.Gamma,
        ["gas_constant"] = (double)preset.Nozzle.GasConstant,
        ["absorption_density_scale"] = (double)preset.AbsorptionDensityScale,
        ["refraction_intensity"] = (double)preset.RefractionIntensity,
    };

    private static float GetFloat(TomlTable table, string key, float defaultValue = 0f)
    {
        if (table.TryGetValue(key, out var v) && v is double d)
            return (float)d;
        return defaultValue;
    }

    private static string GetString(TomlTable table, string key, string defaultValue)
    {
        if (table.TryGetValue(key, out var v) && v is string s && !string.IsNullOrWhiteSpace(s))
            return s;
        return defaultValue;
    }
}
