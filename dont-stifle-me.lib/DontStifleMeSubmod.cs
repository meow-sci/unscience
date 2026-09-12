using Brutal.ImGuiApi;
using MeowSci.KsaAbstractions;

namespace MeowSci.DontStifleMeLib;

/// <summary>
/// ImGui surface for dont-stifle-me's scale and editor value-limit controls.
/// </summary>
public sealed partial class DontStifleMeSubmod : ISubmod
{
    public string Name => "Don't Stifle Me - Editor Limits";
    public string Tooltip => "Removes restrictive vehicle-editor scale and configurable-value limits.";

    public void Initialize() { }
    public void Update(double dt) { }
    public void Dispose() { }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##dsm_content");

        ImGui.Checkbox("Enabled", ref EditorScaleSettings.Enabled);
        ImGui.SetItemTooltip("Lift the 0.5x-2x part scale clamp and scale per axis (X/Y/Z) with the gizmo.\nOff = stock editor.");

        ImGui.BeginDisabled(!EditorScaleSettings.Enabled);
        ImGui.Checkbox("Snap scaling", ref EditorScaleSettings.Snap);
        ImGui.SetItemTooltip("Snap scale drags to 0.25 m diameter increments (game default).\nOff = free, continuous scaling.");
        ImGui.EndDisabled();

        if (ImGui.Checkbox("jpl said no clamps", ref EditorLimitSettings.JplSaidNoClamps) &&
            !EditorLimitSettings.JplSaidNoClamps)
        {
            EditorValueLimitPatches.RestoreTrackedBounds();
        }
        ImGui.SetItemTooltip("Expand selected vehicle-editor value ranges.\nCurrently: parachute diameter 2-1000 m.");

        if (!EditorScalePatches.IsApplied || !EditorValueLimitPatches.IsApplied)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Brutal.Numerics.float4(1f, 0.3f, 0.3f, 1f), "Editor patches are not applied - check the log.");
        }

        SubmodUI.EndContentArea();
    }
}
