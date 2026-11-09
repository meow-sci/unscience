using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace MeowSci.WarPaintLib;

public sealed partial class WarPaintSubmod
{
    private void RenderPresets()
    {
        ImGui.SetNextItemWidth(-1f);
        ImString preview = _editor.SelectedName == null ? "Select Preset..." : $"{_editor.SelectedName}{(_editor.HasChanges ? " *" : "")}";
        if (ImGui.BeginCombo("##wp_presets"u8, preview, ImGuiComboFlags.HeightLarge))
        {
            if (ImGui.IsWindowAppearing())
            {
                ImGui.SetKeyboardFocusHere();
                _presetFilter.Clear();
            }
            ImGui.SetNextItemWidth(-1f);
            bool complete = ImGui.InputTextWithHint("##wp_preset_filter"u8, "Filter presets..."u8, _presetFilter, ImGuiInputTextFlags.EnterReturnsTrue);
            string filter = _presetFilter.ToString().Trim();
            bool any = false;
            foreach (string name in _service.Presets.Names)
            {
                if (!name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                bool firstMatch = !any;
                any = true;
                bool selected = name == _editor.SelectedName;
                if (ImGui.Selectable(name, selected) || (complete && firstMatch))
                {
                    SelectPreset(name);
                    ImGui.CloseCurrentPopup();
                    break;
                }
                if (selected) ImGui.SetItemDefaultFocus();
            }
            if (!any) ImGui.TextDisabled("No matching presets."u8);
            ImGui.EndCombo();
        }
        RenderPresetActions();
        if (_service.Presets.LastError != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new float4(1f, .3f, .3f, 1f));
            ImGui.TextWrapped(_service.Presets.LastError);
            ImGui.PopStyleColor();
        }
    }

    private void RenderPresetActions()
    {
        float padding = ImGui.GetStyle().FramePadding.X * 2;
        float applyWidth = ImGui.CalcTextSize(" Apply to Kittens "u8).X + padding;
        float saveWidth = ImGui.CalcTextSize(" Save Changes "u8).X + padding;
        float createWidth = ImGui.CalcTextSize(" New Preset "u8).X + padding;
        float deleteWidth = ImGui.CalcTextSize(" Delete "u8).X + padding;
        float width = ImGui.GetContentRegionAvail().X;
        int columns = width >= applyWidth + saveWidth + createWidth + deleteWidth + 36f
            ? 4 : width >= (applyWidth + 12f) * 2 ? 2 : 1;
        bool openApply = false, openCreate = false, openDelete = false;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##wp_actions"u8, columns, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX))
        {
            if (columns == 4)
            {
                ImGui.TableSetupColumn("##apply"u8, ImGuiTableColumnFlags.WidthStretch, applyWidth);
                ImGui.TableSetupColumn("##save"u8, ImGuiTableColumnFlags.WidthStretch, saveWidth);
                ImGui.TableSetupColumn("##create"u8, ImGuiTableColumnFlags.WidthStretch, createWidth);
                ImGui.TableSetupColumn("##delete"u8, ImGuiTableColumnFlags.WidthStretch, deleteWidth);
            }
            bool selected = _editor.SelectedName != null;
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(!selected);
            if (ImGui.Button(" Apply to Kittens ##wp"u8, new float2(-1, 0)))
            {
                _applySelection.Clear();
                _dialogError = null;
                openApply = true;
            }
            ImGui.EndDisabled();
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(!_editor.HasChanges);
            if (ImGui.Button(" Save Changes ##wp"u8, new float2(-1, 0)))
            {
                if (_editor.Save()) SetStatus($"Saved '{_editor.SelectedName}'.");
                else SetStatus(_editor.LastError ?? "Could not save all preset changes.", true);
            }
            ImGui.EndDisabled();
            ImGui.TableNextColumn();
            if (ImGui.Button(" New Preset ##wp"u8, new float2(-1, 0)))
            {
                _presetName.Clear();
                _dialogError = null;
                openCreate = true;
            }
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(!selected);
            if (DangerButton(" Delete ##wp_preset"u8, new float2(-1, 0)))
            {
                _dialogError = null;
                openDelete = true;
            }
            ImGui.EndDisabled();
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        // Open and render popups at the content ID scope, outside the layout table.
        if (openApply) ImGui.OpenPopup("Apply to Kittens##wp"u8);
        if (openCreate) ImGui.OpenPopup("New War Paint Preset##wp"u8);
        if (openDelete) ImGui.OpenPopup("Delete War Paint Preset##wp"u8);
    }
}
