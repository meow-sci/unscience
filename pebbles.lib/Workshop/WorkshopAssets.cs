using System;
using System.Linq;
using System.Collections.Generic;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace MeowSci.PebblesLib;

public sealed partial class WorkshopEditor
{
    private readonly ImInputString _assetFilter = new(128);

    private void AssetCombo(string label, string current, string[] options, Action<string> assign, bool allowDefault = false)
    {
        ImGui.Text(label); ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo($"##workshop-{label}", current.Length == 0 ? "Choose / game default" : ClutterAssets.MeshLabel(current))) return;
        try
        {
            if (ImGui.IsWindowAppearing()) _assetFilter.Value16 = _state.AssetFilter;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##asset-filter"u8, "Filter assets"u8, _assetFilter)) _state.AssetFilter = _assetFilter.ToString();
            if (allowDefault && ImGui.Selectable("Game default"u8, current.Length == 0)) assign("");
            foreach (string option in options)
            {
                if (!option.Contains(_state.AssetFilter, StringComparison.OrdinalIgnoreCase) && !ClutterAssets.MeshLabel(option).Contains(_state.AssetFilter, StringComparison.OrdinalIgnoreCase)) continue;
                if (ImGui.Selectable(ClutterAssets.MeshLabel(option) + "##" + option, option == current)) assign(option);
            }
        }
        finally { ImGui.EndCombo(); }
    }
}
