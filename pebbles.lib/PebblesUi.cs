using System;
using System.Collections.Generic;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using MeowSci.KsaAbstractions;

namespace MeowSci.PebblesLib;

internal static class PebblesUi
{
    public static string Choice(string label, string selected, IEnumerable<string> values, string filter = "")
    {
        if (!ImGui.BeginCombo(label, selected.Length == 0 ? "Select…" : ClutterAssets.MeshLabel(selected))) return selected;
        try
        {
            foreach (var id in values)
                if ((filter.Length == 0 || (id.Contains(filter, StringComparison.OrdinalIgnoreCase) || ClutterAssets.MeshLabel(id).Contains(filter, StringComparison.OrdinalIgnoreCase))) && ImGui.Selectable(id.Length == 0 ? "(none / default)" : ClutterAssets.MeshLabel(id) + "##" + id, id == selected)) selected = id;
        }
        finally { ImGui.EndCombo(); }
        return selected;
    }
    public static T Enum<T>(string label, T value) where T : struct, Enum
    {
        string selected = Choice(label, value.ToString(), System.Enum.GetNames<T>());
        return System.Enum.Parse<T>(selected);
    }
    public static float Number(string label, float v) { ImGui.InputFloat(label, ref v); return v; }
    public static double Number(string label, double v) { ImGui.InputDouble(label, ref v); return v; }
    public static bool Toggle(string label, bool v) { ImGui.Checkbox(label, ref v); return v; }
    public static Vec3 Vector(string label, Vec3 v)
    {
        var value = new float3(v.X, v.Y, v.Z); ImGui.InputFloat3(label, ref value); return new(value.X, value.Y, value.Z);
    }
}
