using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace MeowSci.WarPaintLib;

public sealed partial class WarPaintSubmod
{
    private void RenderNewPresetDialog()
    {
        PrepareDialog();
        bool open = true;
        if (!ImGui.BeginPopupModal("New War Paint Preset##wp"u8, ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;
        if (CloseStaleDialog()) return;
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(-1f);
        bool submitted = ImGui.InputTextWithHint("##wp_new_name"u8, "Name..."u8, _presetName, ImGuiInputTextFlags.EnterReturnsTrue);
        string name = _presetName.ToString().Trim();
        bool valid = name.Length is > 0 and <= 128 && !_service.Presets.Names.Contains(name);
        if (name.Length > 128) ImGui.TextWrapped("Preset names can contain up to 128 characters."u8);
        else if (name.Length > 0 && !valid) ImGui.TextWrapped("A preset with this name already exists."u8);
        bool create = DialogButtons(" Create "u8, valid, out bool cancel);
        if ((create || submitted) && valid)
        {
            if (_editor.Create(name))
            {
                _brushColor = _editor.Draft.Tint;
                _materialFilter.Clear();
                SetStatus($"Created '{name}'.");
                ImGui.CloseCurrentPopup();
            }
            else _dialogError = _editor.LastError ?? "Could not create the preset.";
        }
        if (cancel || ImGui.IsKeyPressed(ImGuiKey.Escape)) ImGui.CloseCurrentPopup();
        RenderDialogError();
        ImGui.EndPopup();
    }

    private void RenderDeletePresetDialog()
    {
        PrepareDialog();
        bool open = true;
        if (!ImGui.BeginPopupModal("Delete War Paint Preset##wp"u8, ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;
        if (CloseStaleDialog()) return;
        string? name = _editor.SelectedName;
        int count = _service.GetActivePaints().Count(p => p.PresetName == name);
        ImGui.TextWrapped($"Delete '{name ?? string.Empty}'? Paint will be removed from all {count} kittens using this preset.");
        bool delete = DialogButtons(" Delete "u8, name != null, out bool cancel, destructive: true);
        if (delete)
        {
            if (_editor.Delete())
            {
                SetStatus($"Deleted '{name}' and removed its kitten paint.");
                ImGui.CloseCurrentPopup();
            }
            else _dialogError = _editor.LastError ?? "Could not delete the preset.";
        }
        if (cancel || ImGui.IsKeyPressed(ImGuiKey.Escape)) ImGui.CloseCurrentPopup();
        RenderDialogError();
        ImGui.EndPopup();
    }

    private void RenderApplyDialog()
    {
        PrepareDialog();
        bool open = true;
        if (!ImGui.BeginPopupModal("Apply to Kittens##wp"u8, ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;
        if (CloseStaleDialog()) return;
        var kittens = WarPaintService.VehicleKittens().ToArray();
        _applySelection.IntersectWith(kittens);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        float height = ImGui.GetFrameHeightWithSpacing() * Math.Clamp(kittens.Length + 1, 2, 10);
        if (ImGui.BeginTable("##wp_apply_kittens"u8, 2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY, new float2(0, height)))
        {
            ImGui.TableSetupColumn("##check"u8, ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
            ImGui.TableSetupColumn("Kitten"u8, ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();
            foreach (var kitten in kittens)
            {
                ImGui.PushID(kitten.Id);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                bool selected = _applySelection.Contains(kitten);
                if (ImGui.Checkbox("##wp_selected"u8, ref selected))
                {
                    if (selected) _applySelection.Add(kitten);
                    else _applySelection.Remove(kitten);
                }
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(kitten.Id);
                ImGui.SetItemTooltip(kitten.Id);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        if (kittens.Length == 0) ImGui.TextWrapped("No live EVA kittens are available."u8);
        bool apply = DialogButtons(" Apply "u8, _editor.SelectedName != null && _applySelection.Count > 0, out bool cancel);
        if (apply)
        {
            var errors = new System.Collections.Generic.List<string>();
            int applied = 0;
            foreach (var kitten in _applySelection.ToArray())
            {
                if (_service.Apply(kitten, _editor.Draft, _editor.SelectedName) == null)
                    errors.Add($"{kitten.Id}: {_service.LastError}");
                else { applied++; _applySelection.Remove(kitten); }
            }
            if (errors.Count == 0)
            {
                SetStatus($"Applied '{_editor.SelectedName}' to {applied} kitten(s).");
                ImGui.CloseCurrentPopup();
            }
            else _dialogError = string.Join("\n", errors);
        }
        if (cancel || ImGui.IsKeyPressed(ImGuiKey.Escape)) ImGui.CloseCurrentPopup();
        RenderDialogError();
        ImGui.EndPopup();
    }

    private static void PrepareDialog()
    {
        float width = Math.Min(440f, ImGui.GetMainViewport().Size.X - 40f);
        ImGui.SetNextWindowSizeConstraints(new float2(width, 0), new float2(width, float.MaxValue));
    }

    private bool CloseStaleDialog()
    {
        if (!_closeDialogs) return false;
        ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        return true;
    }

    private static bool DialogButtons(ImString confirm, bool enabled, out bool cancel, bool destructive = false)
    {
        bool accepted = false;
        cancel = false;
        ImGui.Spacing();
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##wp_dialog_actions"u8, 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(!enabled);
            accepted = destructive ? DangerButton(confirm, new float2(-1, 0)) : ImGui.Button(confirm, new float2(-1, 0));
            ImGui.EndDisabled();
            ImGui.TableNextColumn();
            cancel = ImGui.Button(" Cancel "u8, new float2(-1, 0));
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        return accepted;
    }

    private void RenderDialogError()
    {
        if (string.IsNullOrEmpty(_dialogError)) return;
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, new float4(1f, .3f, .3f, 1f));
        ImGui.TextWrapped(_dialogError);
        ImGui.PopStyleColor();
    }

    private static bool DangerButton(ImString label, float2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(KSAColor.Xkcd.Scarlet));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new float4(.95f, .25f, .2f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new float4(.7f, .1f, .08f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(KSAColor.Xkcd.PaleGrey));
        bool pressed = ImGui.Button(label, size);
        ImGui.PopStyleColor(4);
        return pressed;
    }
}
