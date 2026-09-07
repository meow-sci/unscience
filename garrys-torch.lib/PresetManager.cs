using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brutal.Numerics;
using Tomlyn;
using Tomlyn.Model;
using MeowSci.KsaAbstractions;

namespace MeowSci.GarrysTorchLib;

/// <summary>Manages named weld presets persisted to a TOML file.</summary>
public sealed class PresetManager
{
    private readonly string _configDir;
    private readonly string _filePath;
    private Dictionary<string, WeldPreset> _presets = new();
    private string[] _cachedNames = Array.Empty<string>();
    private bool _cacheValid;

    public PresetManager()
    {
        _configDir = Path.Combine(KsaPaths.UserDataDir, ".unscience");
        _filePath = Path.Combine(_configDir, "garrys-torch-presets.toml");
    }

    public void Initialize()
    {
        try
        {
            Directory.CreateDirectory(_configDir);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"garrys-torch: Failed to create config directory: {ex.Message}");
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

    public WeldPreset? GetPreset(string name)
    {
        return _presets.TryGetValue(name, out var preset) ? preset : null;
    }

    public bool PresetExists(string name)
    {
        return _presets.ContainsKey(name);
    }

    public bool SavePreset(string name, WeldPreset preset)
    {
        if (string.IsNullOrWhiteSpace(name) || !WeldScale.IsValid(preset.Scale))
            return false;

        _presets[name] = preset;
        _cacheValid = false;
        Save();
        Console.WriteLine($"garrys-torch: Saved preset '{name}'");
        return true;
    }

    public bool DeletePreset(string name)
    {
        if (!_presets.Remove(name))
            return false;

        _cacheValid = false;
        Save();
        Console.WriteLine($"garrys-torch: Deleted preset '{name}'");
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
                    if (value is not TomlTable entry)
                        continue;

                    _presets[name] = new WeldPreset
                    {
                        Position = new float3(
                            GetFloat(entry, "position_x"),
                            GetFloat(entry, "position_y"),
                            GetFloat(entry, "position_z")),
                        Rotation = new float3(
                            GetFloat(entry, "rotation_x"),
                            GetFloat(entry, "rotation_y"),
                            GetFloat(entry, "rotation_z")),
                        Scale = ReadScale(entry),
                        Collisions = entry.TryGetValue("collisions", out var collisions) && collisions is true,
                        LockRotation = entry.TryGetValue("lock_rotation", out var lr) && lr is bool b ? b : true,
                    };
                }
            }

            Console.WriteLine($"garrys-torch: Loaded {_presets.Count} preset(s)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"garrys-torch: Failed to load presets: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var root = new TomlTable();
            var presetsTable = new TomlTable();

            foreach (var (name, preset) in _presets)
            {
                presetsTable[name] = new TomlTable
                {
                    ["position_x"] = (double)preset.Position.X,
                    ["position_y"] = (double)preset.Position.Y,
                    ["position_z"] = (double)preset.Position.Z,
                    ["rotation_x"] = (double)preset.Rotation.X,
                    ["rotation_y"] = (double)preset.Rotation.Y,
                    ["rotation_z"] = (double)preset.Rotation.Z,
                    ["scale_x"] = (double)preset.Scale.X,
                    ["scale_y"] = (double)preset.Scale.Y,
                    ["scale_z"] = (double)preset.Scale.Z,
                    ["lock_rotation"] = preset.LockRotation,
                    ["collisions"] = preset.Collisions,
                };
            }

            root["presets"] = presetsTable;
            Directory.CreateDirectory(_configDir);
            File.WriteAllText(_filePath, Toml.FromModel(root));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"garrys-torch: Failed to save presets: {ex.Message}");
        }
    }

    private static float GetFloat(TomlTable table, string key, float defaultValue = 0f)
    {
        if (table.TryGetValue(key, out var v) && v is double d)
            return (float)d;
        return defaultValue;
    }

    private static float3 ReadScale(TomlTable table)
    {
        // Presets written before XYZ scaling used one scalar. Treat it as a uniform
        // value so existing user presets migrate without a manual edit.
        float legacyScale = GetFloat(table, "scale", 1f);
        return new float3(
            GetFloat(table, "scale_x", legacyScale),
            GetFloat(table, "scale_y", legacyScale),
            GetFloat(table, "scale_z", legacyScale));
    }
}
