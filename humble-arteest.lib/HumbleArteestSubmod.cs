using Brutal.ImGuiApi;
using MeowSci.KsaAbstractions;

namespace MeowSci.HumbleArteestLib;

/// <summary>
/// Composite ISubmod that groups Vehicle Paint, visor visibility, and Engine Emissive
/// under a single "Humble Arteest" collapsing section in the Unscience Toolbox.
/// </summary>
public sealed partial class HumbleArteestSubmod : ISubmod, MeowSci.KsaAbstractions.Persistence.ISaveParticipantSource
{
    public string Name => "Humble Arteest - Paint Stuff";
    public string Tooltip => "Vehicle paint, kitten visor visibility, and engine emissive paint brushes";

    private readonly VehiclePaintSubmod _vehiclePaint = new();
    private readonly EngineEmissiveSubmod _engineEmissive = new();

    public void Initialize()
    {
        _vehiclePaint.Initialize();
        _engineEmissive.Initialize();
    }

    public void Update(double dt)
    {
        _vehiclePaint.Update(dt);
        _engineEmissive.Update(dt);
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##ha_content");

        ImGui.SeparatorText("Vehicle Paint");
        ImGui.SetItemTooltip(VehiclePaintSubmod.HeaderTooltip);
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();
        _vehiclePaint.RenderBody();

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SeparatorText("Kitten Visors"u8);
        RenderVisorControl();

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.SeparatorText("Engine Emissive");
        ImGui.SetItemTooltip(
            "Overrides the Temperature field on dynamic engine parts to control\n" +
            "their emissive glow. Uses the game's existing per-instance Temperature\n" +
            "data path — no shader modifications needed.\n\n" +
            "Temperature drives the ENABLE_TEMPERATURE variant of MeshIndirect.frag\n" +
            "and its heat lookup table, making engines glow from cool to hot.");
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();
        _engineEmissive.RenderBody();

        SubmodUI.EndContentArea();
    }

    public void Dispose()
    {
        _vehiclePaint.Dispose();
        KittenVisorPatches.Hidden = false;
        _engineEmissive.Dispose();
    }

    private static void RenderVisorControl()
    {
        ImGui.BeginDisabled(!KittenVisorPatches.IsApplied);
        if (ImGui.Button(KittenVisorPatches.Hidden ? "Show visor glass##ha_visor" : "Hide visor glass##ha_visor"))
            KittenVisorPatches.Hidden = !KittenVisorPatches.Hidden;
        ImGui.EndDisabled();
        ImGui.SetItemTooltip("Controls visor glass on all kittens, including newly spawned kittens. Saved with the scene.");
        ImGui.SameLine();
        ImGui.TextDisabled(KittenVisorPatches.Hidden ? "Visors hidden" : "Visors shown");
        if (!KittenVisorPatches.IsApplied)
            ImGui.TextWrapped(KittenVisorPatches.LastError ?? "Visor toggle patch is not installed.");
        ImGui.TextDisabled("Kitten colors and presets are in War Paint."u8);
    }
}
