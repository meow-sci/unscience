using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.WarPaintLib;

/// <summary>Named global paint recipes, stored outside native scene save directories.</summary>
public sealed class PaintPresetLibrary
{
    private readonly string _path;
    private Dictionary<string, PaintSettings> _presets = new(StringComparer.Ordinal);
    private bool _loaded;
    private bool _loadFailed;
    public string? LastError { get; private set; }
    public string FilePath => _path;
    public PaintPresetLibrary(string? path = null) => _path = path ?? Path.Combine(KsaPaths.ModDataDir, "war-paint-presets.json");

    public IReadOnlyList<string> Names
    {
        get { Load(); return _presets.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray(); }
    }

    public bool TryGet(string name, out PaintSettings settings)
    {
        Load();
        if (_presets.TryGetValue(name, out var saved)) { settings = saved.Clone(); return true; }
        settings = new PaintSettings(); return false;
    }

    public bool Save(string name, PaintSettings settings)
    {
        Load();
        if (_loadFailed) return false;
        try
        {
            ValidateName(name); settings.Validate();
            var next = new Dictionary<string, PaintSettings>(_presets, StringComparer.Ordinal)
                { [name] = settings.Clone() };
            if (!Write(next)) return false;
            _presets = next;
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public bool Delete(string name)
    {
        Load();
        if (_loadFailed || !_presets.ContainsKey(name)) return false;
        var next = new Dictionary<string, PaintSettings>(_presets, StringComparer.Ordinal);
        next.Remove(name);
        if (!Write(next)) return false;
        _presets = next;
        return true;
    }

    private void Load()
    {
        if (_loaded) return;
        _loaded = true;
        if (!File.Exists(_path)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<PresetDocument>(File.ReadAllText(_path), SaveJson.Options)
                ?? throw new InvalidOperationException("Missing War Paint preset document.");
            if (saved.Version != 1 || saved.Presets == null || saved.Presets.Count > 10000)
                throw new InvalidOperationException("Unsupported or invalid War Paint preset library.");
            foreach (var pair in saved.Presets)
            { ValidateName(pair.Key); pair.Value.Validate(); }
            _presets = new(saved.Presets, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _loadFailed = true;
            LastError = $"Could not read paint presets; existing file preserved: {ex.Message}";
        }
    }

    private bool Write(Dictionary<string, PaintSettings> presets)
    {
        string? temporary = null;
        try
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, $".war-paint-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(new PresetDocument { Presets = presets }, SaveJson.Options));
            File.Move(temporary, _path, overwrite: true);
            LastError = null;
            return true;
        }
        catch (Exception ex) { LastError = $"Could not save paint presets: {ex.Message}"; return false; }
        finally
        {
            if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Contains('\0'))
            throw new InvalidOperationException("Preset names must contain 1–128 characters and no NUL.");
    }

    public sealed class PresetDocument
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, PaintSettings> Presets { get; set; } = new(StringComparer.Ordinal);
    }
}
