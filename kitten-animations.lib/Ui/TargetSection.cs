using System;
using System.Collections.Generic;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace MeowSci.KittenAnimationsLib.Ui;

/// <summary>Filterable selector for choosing which live EVA kitten the panel drives.</summary>
public static class TargetSection
{
    private const string FollowControlledLabel = "Follow controlled kitten";

    public static bool Render(
        IReadOnlyList<KittenEva> kittens,
        KittenEva? controlledKitten,
        string? selectedKittenId,
        ImInputString filter,
        out string? newSelectedKittenId)
    {
        newSelectedKittenId = selectedKittenId;
        bool changed = false;

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##ka_target_tbl", 2,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("##ka_target_label", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##ka_target_value", ImGuiTableColumnFlags.WidthStretch, 3f);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Target Kitten");
            ImGui.SetItemTooltip("Choose a live EVA kitten without taking control of it.");

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1f);
            string preview = Preview(kittens, controlledKitten, selectedKittenId);
            if (ImGui.BeginCombo("##ka_target", preview))
            {
                if (ImGui.IsWindowAppearing())
                {
                    ImGui.SetKeyboardFocusHere();
                    filter.Clear();
                }

                ImGui.SetNextItemWidth(-1f);
                ImGui.InputTextWithHint("##ka_target_filter", "filter..."u8, filter);
                string filterText = filter.ToString().Trim();

                string followLabel = controlledKitten == null
                    ? $"{FollowControlledLabel} (none)"
                    : $"{FollowControlledLabel} ({controlledKitten.Id})";
                if (Matches(followLabel, filterText))
                {
                    bool selected = selectedKittenId == null;
                    if (ImGui.Selectable(followLabel + "##ka_target_auto", selected) && !selected)
                    {
                        newSelectedKittenId = null;
                        changed = true;
                    }
                    if (selected) ImGui.SetItemDefaultFocus();
                }

                for (int i = 0; i < kittens.Count; i++)
                {
                    var kitten = kittens[i];
                    string label = ReferenceEquals(kitten, controlledKitten)
                        ? $"{kitten.Id} (controlled)"
                        : kitten.Id;
                    if (!Matches(label, filterText)) continue;

                    bool selected = string.Equals(selectedKittenId, kitten.Id, StringComparison.Ordinal);
                    ImGui.PushID(i);
                    if (ImGui.Selectable(label, selected) && !selected)
                    {
                        newSelectedKittenId = kitten.Id;
                        changed = true;
                    }
                    ImGui.PopID();
                    if (selected) ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
            ImGui.SetItemTooltip("Follow the controlled EVA kitten automatically, or pin this panel to any live EVA kitten.\n"
                               + "Selecting a kitten does not change vehicle control or move the camera.\n"
                               + "Each kitten keeps its holds until you clear or disable them.");

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        return changed;
    }

    private static string Preview(
        IReadOnlyList<KittenEva> kittens,
        KittenEva? controlledKitten,
        string? selectedKittenId)
    {
        if (selectedKittenId == null)
        {
            return controlledKitten == null
                ? $"{FollowControlledLabel} (none)"
                : $"{FollowControlledLabel} ({controlledKitten.Id})";
        }

        for (int i = 0; i < kittens.Count; i++)
        {
            if (!string.Equals(kittens[i].Id, selectedKittenId, StringComparison.Ordinal)) continue;
            return ReferenceEquals(kittens[i], controlledKitten)
                ? $"{selectedKittenId} (controlled)"
                : selectedKittenId;
        }

        return $"{selectedKittenId} (unavailable)";
    }

    private static bool Matches(string label, string filter) =>
        filter.Length == 0 || label.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
