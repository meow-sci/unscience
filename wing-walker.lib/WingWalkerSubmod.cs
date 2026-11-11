using Brutal.ImGuiApi;
using MeowSci.KsaAbstractions;

namespace MeowSci.WingWalkerLib;

/// <summary>Discovery panel; posture and occupancy belong to native part and crew save data.</summary>
public sealed class WingWalkerSubmod : ISubmod
{
    public string Name => "Wing Walker";
    public string Tooltip => "Attach seated or standing crew to any vehicle with external crew parts.";
    public void Initialize() { }
    public void Update(double dt) { }
    public void Dispose() { }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##wing_walker_content");
        try
        {
            ImGui.TextWrapped("Place External Seat or Standing Mount from the editor's External Crew category."u8);
            ImGui.TextWrapped("Assign a kitten in the part's Crew controls, or board its entry point during flight. Use the crew portrait's EVA action to leave."u8);
            ImGui.TextWrapped("Both parts reuse the stock chair and show helmeted crew. Standing Mount uses an upright pose, including while paused. Supplies are finite; refill or connect appropriate life support before long missions."u8);
            if (!WingWalkerPatches.IsApplied)
                ImGui.TextWrapped("Crew visibility support is unavailable. Check the game log."u8);
            if (WingWalkerPatches.LastError is string error)
                ImGui.TextWrapped($"Wing Walker: {error}");
        }
        finally { SubmodUI.EndContentArea(); }
    }
}
