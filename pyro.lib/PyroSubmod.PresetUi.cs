using System;
using Brutal.Numerics;
using Brutal.ImGuiApi;

namespace MeowSci.PyroLib;

/// <summary>Preset UI: create-form preset combo with delete, save-as-preset entry point and both modals.</summary>
public sealed partial class PyroSubmod
{
    private readonly PlumePresetManager _presetManager = new();

    // Create-form preset selection. The stashed preset carries the fields the create form
    // doesn't show (throttle, nozzle, look) and is applied when Create Plume is clicked.
    private int _selectedPresetIndex = -1;
    private readonly ImInputString _presetFilter = new(128);
    private PlumePreset? _pendingPreset;

    // Deferred modal open flags (popups must be opened at matching ID scope)
    private bool _openDeleteModal;
    private bool _openSaveModal;

    // Delete preset modal state
    private string? _deleteConfirmName;

    // Save preset modal state
    private readonly ImInputString _savePresetName = new(128);
    private string? _savePresetError;
    private PlumePreset? _pendingSavePreset;

    // ---- Create form row ----

    /// <summary>Renders the "Preset" row (filterable combo + delete button) inside the create-form table.</summary>
    private void RenderPresetFormRow(string[] templateIds)
    {
        var presetNames = _presetManager.GetPresetNames();
        if (_selectedPresetIndex >= presetNames.Length) _selectedPresetIndex = -1;

        ImGui.TableNextRow();
        ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Preset");
        ImGui.TableNextColumn();

        var style = ImGui.GetStyle();
        float delW = ImGui.CalcTextSize(" del ").X + style.FramePadding.X * 2f;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - delW - style.ItemSpacing.X);
        RenderPresetCombo(presetNames, templateIds);

        ImGui.SameLine();
        bool hasSelection = _selectedPresetIndex >= 0 && _selectedPresetIndex < presetNames.Length;
        if (!hasSelection) ImGui.BeginDisabled();
        if (ImGui.Button(" del ##pyro_preset_del"))
        {
            _deleteConfirmName = presetNames[_selectedPresetIndex];
            _openDeleteModal = true;
        }
        if (!hasSelection) ImGui.EndDisabled();
    }

    private void RenderPresetCombo(string[] presetNames, string[] templateIds)
    {
        string preview = _selectedPresetIndex >= 0 && _selectedPresetIndex < presetNames.Length
            ? presetNames[_selectedPresetIndex] : "Select...";

        if (!ImGui.BeginCombo("##pyro_preset", preview))
            return;

        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
            _presetFilter.Clear();
        }
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##pyro_preset_filter", "filter..."u8, _presetFilter);
        string filterText = _presetFilter.ToString().Trim();

        for (int i = 0; i < presetNames.Length; i++)
        {
            if (filterText.Length > 0 && !presetNames[i].Contains(filterText, StringComparison.OrdinalIgnoreCase)) continue;
            bool sel = _selectedPresetIndex == i;
            if (ImGui.Selectable(presetNames[i], sel))
            {
                _selectedPresetIndex = i;
                ApplyPresetToCreateForm(presetNames[i], templateIds);
            }
            if (sel) ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    /// <summary>Loads a preset into the create form: template and offsets go into the visible
    /// fields; throttle/nozzle/look ride along in <see cref="_pendingPreset"/> until create.</summary>
    private void ApplyPresetToCreateForm(string name, string[] templateIds)
    {
        var preset = _presetManager.GetPreset(name);
        if (preset == null) return;

        preset.TemplateId = PlumeTemplates.NormalizeId(preset.TemplateId);
        _pendingPreset = preset;
        _pendingPosition = preset.Position;
        _pendingRotation = preset.Rotation;
        int templateIndex = Array.IndexOf(templateIds, preset.TemplateId);
        if (templateIndex >= 0) _pendingTemplateIndex = templateIndex;
    }

    // ---- Save entry point (per-plume section) ----

    /// <summary>Snapshots a plume's settings and opens the save-as-preset modal.</summary>
    private void OpenSavePresetModal(PlumeEntry plume)
    {
        _pendingSavePreset = PlumePreset.FromPlume(plume);
        _savePresetName.Clear();
        _savePresetError = null;
        _openSaveModal = true;
    }

    // ---- Modals ----

    /// <summary>Opens deferred popups and renders both preset modals at content-area ID scope.</summary>
    private void RenderPresetModals()
    {
        if (_openDeleteModal)
        {
            ImGui.OpenPopup("Delete preset##pyro");
            _openDeleteModal = false;
        }
        if (_openSaveModal)
        {
            ImGui.OpenPopup("Save as preset##pyro");
            _openSaveModal = false;
        }
        RenderDeletePresetModal();
        RenderSavePresetModal();
    }

    private void RenderDeletePresetModal()
    {
        bool open = true;
        if (!ImGui.BeginPopupModal("Delete preset##pyro", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.Text($"Are you sure you want to delete\npreset '{_deleteConfirmName ?? string.Empty}'?");
        ImGui.Spacing();
        if (ImGui.Button(" You bet ##pyro_delyes"))
        {
            if (_deleteConfirmName != null)
                _presetManager.DeletePreset(_deleteConfirmName);
            _selectedPresetIndex = -1;
            _deleteConfirmName = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine(0, 8);
        if (ImGui.Button(" Cancel ##pyro_delno"))
        {
            _deleteConfirmName = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void RenderSavePresetModal()
    {
        bool open = true;
        if (!ImGui.BeginPopupModal("Save as preset##pyro", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.InputText("##pyro_savename", _savePresetName);
        ImGui.Spacing();
        if (ImGui.Button(" Save ##pyro_savebtn"))
        {
            var name = _savePresetName.ToString().Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                _savePresetError = "Name is required";
            }
            else if (_presetManager.PresetExists(name))
            {
                _savePresetError = "A preset with this name already exists";
            }
            else if (_pendingSavePreset != null)
            {
                _presetManager.SavePreset(name, _pendingSavePreset);
                _pendingSavePreset = null;
                _savePresetError = null;
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine(0, 8);
        if (ImGui.Button(" Cancel ##pyro_savecancel"))
        {
            _savePresetError = null;
            ImGui.CloseCurrentPopup();
        }
        if (!string.IsNullOrEmpty(_savePresetError))
        {
            ImGui.Spacing();
            ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), _savePresetError);
        }
        ImGui.EndPopup();
    }
}
