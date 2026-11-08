using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace MeowSci.WarPaintLib;

public sealed partial class WarPaintSubmod
{
    private void RenderBrush()
    {
        ImGui.SeparatorText("Kitten Paint"u8);
        var tint = _draft.Tint;
        if (ImGui.ColorEdit4("Tint all materials##wp"u8, ref tint, ImGuiColorEditFlags.AlphaBar))
        {
            _draft.Tint = tint;
            // Like DOH's original Tint All, the brush replaces every material color.
            foreach (var material in _draft.Materials) material.Color = tint;
            _draftChanged = true;
            _pendingApply = !_liveEditing;
            if (_liveEditing && _target != null && !_service.UpdateTint(_target, tint))
            {
                _liveEditing = false;
                _pendingApply = true;
                SetStatus(_service.LastError ?? "Could not update the kitten tint.", true);
            }
        }
        ImGui.SetItemTooltip("Replaces every material color on this kitten. Edit individual materials below for finer control. Alpha affects opaque kitten surfaces; visor visibility is in Humble Arteest."u8);

        ImGui.BeginDisabled(_target == null);
        if (ImGui.Button("Apply to kitten##wp"u8) && _target != null)
        {
            if (_service.Apply(_target, _draft) != null)
            {
                _draft = _service.Snapshot(_target) ?? _draft;
                _liveEditing = true;
                _pendingApply = false;
                SetStatus($"Paint applied to '{_target.Id}'. Color edits now update it live.");
            }
            else SetStatus(_service.LastError ?? "Could not paint the kitten.", true);
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(_target == null || !_service.TryGetPaint(_target, out _));
        if (ImGui.Button("Unset##wp_target"u8) && _target != null)
            UnsetTarget(_target);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!_pendingApply);
        if (ImGui.Button("Load kitten colors##wp"u8) && _target != null)
        {
            _pendingApply = false;
            SelectTarget(_target);
        }
        ImGui.EndDisabled();
        ImGui.SetItemTooltip("Discards the unapplied brush and loads this kitten's current paint, or stock colors if unpainted."u8);
        ImGui.EndDisabled();

        if (_target == null)
            ImGui.TextDisabled("Select a kitten, then apply your paint or a saved preset."u8);
        else if (_liveEditing)
            ImGui.TextDisabled("Color edits apply live. Save a preset to reuse them in DOH."u8);
        else
            ImGui.TextDisabled("Apply the brush to edit this kitten live and reveal its materials."u8);

        if (_draft.Materials.Count > 0 && ImGui.CollapsingHeader("Individual Materials##wp"u8, ImGuiTreeNodeFlags.DefaultOpen))
            RenderMaterials();
    }

    private void RenderMaterials()
    {
        ImGui.SetNextItemWidth(-1f);
        _materialFilter.Draw("Filter materials##wp"u8);
        ImGui.TextDisabled("Presets match the same body or attachment material on compatible kittens."u8);
        var flags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH
            | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoPadOuterX;
        if (!ImGui.BeginTable("##wp_materials"u8, 2, flags, new float2(0, ImGui.GetTextLineHeightWithSpacing() * 10))) return;
        ImGui.TableSetupColumn("Color"u8, ImGuiTableColumnFlags.WidthFixed, 175f);
        ImGui.TableSetupColumn("Material"u8, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        for (int i = 0; i < _draft.Materials.Count; i++)
        {
            var material = _draft.Materials[i];
            if (!_materialFilter.PassFilter(material.Name) && !_materialFilter.PassFilter(material.Source)) continue;
            ImGui.PushID(i);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var color = material.Color;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.ColorEdit4("##wp_color"u8, ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
            {
                material.Color = color;
                _draftChanged = true;
                _pendingApply = !_liveEditing;
                if (_liveEditing && _target != null && !_service.UpdateMaterial(_target, material.Source, material.Name, color))
                {
                    _liveEditing = false;
                    _pendingApply = true;
                    SetStatus(_service.LastError ?? $"Could not update '{material.Name}'.", true);
                }
            }
            ImGui.TableNextColumn();
            ImGui.Text(material.Name);
            ImGui.SetItemTooltip(material.Slot >= 0
                ? $"{material.Source}, material {material.Slot + 1}. Presets reuse this body or attachment slot."
                : material.Source);
            ImGui.SameLine();
            ImGui.TextDisabled($"({material.Source})");
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private void RenderActivePaints()
    {
        var paints = _service.GetActivePaints();
        ImGui.SeparatorText($"Active Kitten Paints ({paints.Count})");
        if (paints.Count == 0)
        {
            ImGui.TextDisabled("No kittens have War Paint applied."u8);
            return;
        }
        if (!ImGui.BeginTable("##wp_active"u8, 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.NoPadOuterX)) return;
        ImGui.TableSetupColumn("Kitten"u8, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Tint"u8, ImGuiTableColumnFlags.WidthFixed, 34f);
        ImGui.TableSetupColumn("##wp_actions"u8, ImGuiTableColumnFlags.WidthFixed, 60f);
        ImGui.TableHeadersRow();
        foreach (var paint in paints)
        {
            ImGui.PushID(paint.VehicleId);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (ImGui.Selectable(paint.VehicleId, ReferenceEquals(paint.Kitten, _target))) SelectTarget(paint.Kitten);
            ImGui.TableNextColumn();
            ImGui.ColorButton("##wp_tint"u8, paint.MaterialSet.TintColor, ImGuiColorEditFlags.NoTooltip, new float2(24, 18));
            ImGui.TableNextColumn();
            if (ImGui.SmallButton("Unset##wp"u8)) UnsetTarget(paint.Kitten);
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private void UnsetTarget(KSA.KittenEva kitten)
    {
        if (!_service.Unset(kitten))
        {
            SetStatus(_service.LastError ?? $"Could not unset '{kitten.Id}'.", true);
            return;
        }
        if (ReferenceEquals(kitten, _target))
        {
            _draft = new();
            _liveEditing = false;
            _pendingApply = false;
            _draftChanged = false;
            _selectedPreset = null;
        }
        SetStatus($"Unset paint on '{kitten.Id}'. Original materials restored.");
    }
}
