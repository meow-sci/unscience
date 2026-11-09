using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace MeowSci.WarPaintLib;

public sealed partial class WarPaintSubmod
{
    private void RenderPresetColors()
    {
        ImGui.SeparatorText("Preset Colors"u8);
        ImGui.BeginDisabled(_editor.SelectedName == null);
        float width = ImGui.GetContentRegionAvail().X;
        float buttonWidth = ImGui.CalcTextSize(" Apply to All Materials "u8).X + ImGui.GetStyle().FramePadding.X * 2;
        // Keep RGBA inputs usable when the host window is narrow; let the action wrap below.
        float colorWidth = ImGui.GetFontSize() * 20f;
        bool split = width >= buttonWidth + colorWidth + 24f;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##wp_brush"u8, split ? 2 : 1, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX))
        {
            if (split)
            {
                ImGui.TableSetupColumn("##color"u8, ImGuiTableColumnFlags.WidthFixed, colorWidth);
                ImGui.TableSetupColumn("##apply"u8, ImGuiTableColumnFlags.WidthStretch, 1f);
            }
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1f);
            ImGui.ColorEdit4("##wp_brush_color"u8, ref _brushColor,
                ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf
                | ImGuiColorEditFlags.Uint8 | ImGuiColorEditFlags.DisplayRGB);
            ImGui.TableNextColumn();
            if (ImGui.Button(" Apply to All Materials ##wp"u8, new float2(-1, 0)))
            {
                _editor.Draft.Tint = _brushColor;
                foreach (var material in _editor.Draft.Materials) material.Color = _brushColor;
                PreviewChanges();
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##wp_material_filter"u8, "Filter materials..."u8, _materialFilter);
        ImGui.SetItemTooltip("Filter materials by name or attachment."u8);
        RenderMaterials();
        ImGui.EndDisabled();
    }

    private void RenderMaterials()
    {
        var flags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH
            | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        float rowHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().CellPadding.Y * 2f;
        float headerHeight = ImGui.GetTextLineHeight() + ImGui.GetStyle().CellPadding.Y * 2f;
        // Add 40% to the previous viewport, and fit normal kitten layouts including cell padding.
        float height = Math.Max(ImGui.GetFrameHeightWithSpacing() * 8f * 1.4f,
            headerHeight + rowHeight * Math.Min(_editor.Draft.Materials.Count, 11) + 2f);
        if (ImGui.BeginTable("##wp_materials"u8, 2, flags, new float2(0, height)))
        {
            ImGui.TableSetupColumn("##wp_color_column"u8, ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
            ImGui.TableSetupColumn("Material"u8, ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();
            string filter = _materialFilter.ToString().Trim();
            for (int i = 0; i < _editor.Draft.Materials.Count; i++)
            {
                var material = _editor.Draft.Materials[i];
                if (!material.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !material.Source.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                ImGui.PushID(i);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var color = material.Color;
                if (ImGui.ColorEdit4("##wp_color"u8, ref color,
                    ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel
                    | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf))
                {
                    material.Color = color;
                    PreviewChanges();
                }
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(material.Name);
                ImGui.SetItemTooltip($"{material.Name}\n{material.Source}, material {material.Slot + 1}");
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        if (_editor.SelectedName != null && _editor.Draft.Materials.Count == 0)
            ImGui.TextWrapped("Individual materials appear when an EVA kitten is available. You can still set the whole-material color."u8);
    }

    private void RenderActivePaints()
    {
        ImGui.SeparatorText("Live War Paint"u8);
        var paints = _service.GetActivePaints();
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##wp_active"u8, 3,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("Kitten"u8, ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Preset"u8, ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##wp_actions"u8, ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
            ImGui.TableHeadersRow();
            foreach (var paint in paints)
            {
                ImGui.PushID(paint.VehicleId);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(paint.VehicleId);
                ImGui.SetItemTooltip(paint.VehicleId);
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(paint.PresetName);
                ImGui.SetItemTooltip(paint.PresetName);
                ImGui.TableNextColumn();
                if (DangerButton("X##wp_unset"u8, new float2(-1, 0)))
                {
                    if (_service.Unset(paint.Kitten)) LogStatus($"Removed paint from '{paint.VehicleId}'.");
                    else LogStatus(_service.LastError ?? "Could not remove kitten paint.", true);
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        if (paints.Count == 0) ImGui.TextDisabled("No kittens have War Paint applied."u8);
    }
}
