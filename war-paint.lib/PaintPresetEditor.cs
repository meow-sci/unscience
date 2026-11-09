using System;
using System.Linq;

namespace MeowSci.WarPaintLib;

/// <summary>Detached preset editing and live preview, independent of the ImGui shell.</summary>
public sealed class PaintPresetEditor(WarPaintService service)
{
    private PaintSettings _saved = new();
    public string? SelectedName { get; private set; }
    public PaintSettings Draft { get; private set; } = new();
    public bool HasChanges => SelectedName != null && !Draft.EquivalentTo(_saved);
    public string? LastError { get; private set; }

    public bool Select(string name)
    {
        if (name == SelectedName) return true;
        if (!service.Presets.TryGet(name, out var settings))
        { LastError = service.Presets.LastError ?? $"Paint preset unavailable: {name}."; return false; }
        if (!DiscardPreview()) return false;
        SelectedName = name;
        Draft = service.DescribeMaterials(settings);
        _saved = Draft.Clone();
        LastError = null;
        return true;
    }

    public bool Deselect()
    {
        if (!DiscardPreview()) return false;
        Clear();
        return true;
    }

    public bool Create(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 128 || name.Contains('\0'))
        { LastError = "Preset names must contain 1–128 characters and no NUL."; return false; }
        if (service.Presets.Names.Contains(name))
        { LastError = "A preset with this name already exists."; return false; }
        if (!DiscardPreview()) return false;
        var settings = service.DescribeMaterials(new PaintSettings());
        if (!service.Presets.Save(name, settings))
        {
            string? error = service.Presets.LastError;
            // Creation failed after discarding the previous preview; keep that editor usable.
            if (HasChanges) Preview();
            LastError = error;
            return false;
        }
        SelectedName = name;
        Draft = settings;
        _saved = settings.Clone();
        LastError = null;
        return true;
    }

    public bool Preview()
    {
        if (SelectedName == null) return false;
        bool success = service.PreviewPreset(SelectedName, Draft);
        LastError = service.LastError;
        return success;
    }

    public bool Save()
    {
        if (SelectedName == null) return false;
        if (!service.Presets.Save(SelectedName, Draft))
        { LastError = service.Presets.LastError; return false; }
        _saved = Draft.Clone();
        // Retry a previously failed preview; retain diagnostics if some targets cannot update.
        if (Preview()) return true;
        LastError = $"Preset saved, but some kittens could not update: {LastError}";
        return false;
    }

    public bool Delete()
    {
        if (SelectedName == null || !service.DeletePreset(SelectedName))
        { LastError = service.LastError; return false; }
        Clear();
        return true;
    }

    public bool DiscardPreview()
    {
        if (SelectedName == null || !HasChanges) return true;
        bool success = service.PreviewPreset(SelectedName, _saved);
        LastError = service.LastError;
        return success;
    }

    /// <summary>Merge newly available roles, including delayed body/attachment assets, without replacing edits.</summary>
    public void DiscoverMaterials()
    {
        if (SelectedName == null) return;
        Draft = service.DescribeMaterials(Draft);
        _saved = service.DescribeMaterials(_saved);
    }

    public void Clear()
    {
        SelectedName = null;
        Draft = new();
        _saved = new();
        LastError = null;
    }
}
