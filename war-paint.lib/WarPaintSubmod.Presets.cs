using Brutal.ImGuiApi;
using System.Linq;

namespace MeowSci.WarPaintLib;

public sealed partial class WarPaintSubmod
{
    private void RenderPresets()
    {
        ImGui.SeparatorText("Presets"u8);
        var library = _service.Presets;
        var names = library.Names;
        if (_selectedPreset != null && !names.Contains(_selectedPreset))
        {
            _selectedPreset = null;
            _draftChanged = true;
        }
        ImGui.SetNextItemWidth(-90f);
        if (ImGui.BeginCombo("##wp_presets"u8, _selectedPreset ?? "Choose a saved preset..."))
        {
            foreach (string name in names)
            {
                if (!ImGui.Selectable(name, name == _selectedPreset)) continue;
                if (library.TryGet(name, out var settings))
                {
                    _draft = settings;
                    _selectedPreset = name;
                    _presetName.Value16 = name;
                    _liveEditing = false;
                    _pendingApply = true;
                    _draftChanged = false;
                    SetStatus($"Loaded '{name}'. Apply it to the selected kitten.");
                }
                else SetStatus(library.LastError ?? "The preset could not be loaded.", true);
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(_selectedPreset == null);
        if (ImGui.Button("Delete##wp_preset"u8) && _selectedPreset != null)
        {
            string name = _selectedPreset;
            if (library.Delete(name))
            {
                _selectedPreset = null;
                _draftChanged = true;
                SetStatus($"Deleted '{name}'. Applied kitten paint is unchanged.");
            }
            else SetStatus(library.LastError ?? "The preset could not be deleted.", true);
        }
        ImGui.EndDisabled();
        ImGui.SetItemTooltip("Deletes the saved preset. Kittens already painted with it keep their colors."u8);

        ImGui.SetNextItemWidth(-112f);
        ImGui.InputTextWithHint("##wp_preset_name"u8, "Preset name"u8, _presetName);
        ImGui.SameLine();
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_presetName.ToString()));
        if (ImGui.Button("Save preset##wp"u8))
        {
            string name = _presetName.ToString().Trim();
            if (library.Save(name, _draft))
            {
                _selectedPreset = name;
                _draftChanged = false;
                if (!_liveEditing) _pendingApply = true;
                SetStatus($"Saved '{name}'. It is now available in DOH.");
            }
            else SetStatus(library.LastError ?? "The preset could not be saved.", true);
        }
        ImGui.EndDisabled();
        ImGui.SetItemTooltip("Saves the current brush and material colors. An existing name replaces that preset."u8);
        ImGui.TextDisabled(_draftChanged ? "Unsaved color changes" : "Presets are shared across scenes.");
        if (library.LastError != null) ImGui.TextWrapped(library.LastError);
    }
}
