using System;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.GlassLib;

public sealed partial class GlassSubmod : ISubmod
{
    public string Name => "Glass - Camera Lens";
    public string Tooltip => "Adjusts camera field of view with preset lens options from telephoto to fisheye.";

    private int _fov = 50;
    private int _selectedPresetIndex = 0; // 0 = Game Default

    private static readonly (string Name, int Fov)[] Presets = new[]
    {
        ("Game Default", 50),
        ("Super Telephoto (200mm)", 15),
        ("Telephoto (135mm)", 20),
        ("Portrait (85mm)", 30),
        ("Standard (50mm)", 50),
        ("Wide Angle (28mm)", 75),
        ("Ultra Wide (16mm)", 100),
        ("Fisheye (10mm)", 120),
    };

    public void Initialize() { }

    public void Update(double dt)
    {
        try { FovController.ApplyFov(); }
        catch (Exception ex) { Console.WriteLine($"unscience/glass: Error applying FOV override: {ex.Message}"); }
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##glass_content");

        float currentFovDeg = FovController.GetCurrentFovDegrees();
        ImGui.Text($"Current FOV: {currentFovDeg:F1}\u00b0");

        ImGui.Spacing();

        var tableFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##glass_params", 2, tableFlags))
        {
            ImGui.TableSetupColumn("##glass_lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##glass_widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            // Lens preset row
            string preview = _selectedPresetIndex >= 0 ? Presets[_selectedPresetIndex].Name : "Custom";
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Lens");
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##glass_lens", preview))
            {
                for (int i = 0; i < Presets.Length; i++)
                {
                    bool selected = _selectedPresetIndex == i;
                    if (ImGui.Selectable(Presets[i].Name, selected))
                    {
                        _selectedPresetIndex = i;
                        _fov = Presets[i].Fov;
                        FovController.SetFov(_fov);
                    }
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            // FOV slider row
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("FOV");
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.DragInt("##glass_fov", ref _fov, 1f, 10, 200))
            {
                _fov = Math.Clamp(_fov, 10, 200);
                FovController.SetFov(_fov);
                _selectedPresetIndex = FindPresetIndex(_fov);
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding

        SubmodUI.EndContentArea();
    }

    private int FindPresetIndex(int fov)
    {
        for (int i = 0; i < Presets.Length; i++)
            if (Presets[i].Fov == fov) return i;
        return -1;
    }

    public void Dispose()
    {
        FovController.DisableOverride();
    }
}
