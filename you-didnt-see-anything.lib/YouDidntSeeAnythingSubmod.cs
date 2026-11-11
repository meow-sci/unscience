using System.Collections.Generic;
using Brutal.ImGuiApi;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.YouDidntSeeAnythingLib;

/// <summary>Controls the optional UI replay while the game HUD is hidden.</summary>
public sealed class YouDidntSeeAnythingSubmod : ISubmod, ISaveParticipantSource
{
    public string Name => "You Didn't See Anything";
    public string Tooltip => "Keep ImGui windows visible when F2 hides the game UI.";

    public void Initialize() { }
    public void Update(double dt) { }
    public void Dispose() => VisibilitySettings.Reset();

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##ydsa_content");
        ImGui.Checkbox("Keep ImGui windows visible when UI is hidden", ref VisibilitySettings.Enabled);
        ImGui.SetItemTooltip("When F2 hides the game UI, continue drawing ImGui windows. Off preserves normal F2 behavior.");
        ImGui.BeginDisabled(!VisibilitySettings.Enabled);
        ImGui.Checkbox("Detached OS windows only", ref VisibilitySettings.DetachedOnly);
        ImGui.SetItemTooltip("Show only windows detached from the main game window while the UI is hidden.");
        ImGui.Checkbox("Hide stock gauges", ref VisibilitySettings.HideGauges);
        ImGui.SetItemTooltip("Keep KSA's stock gauges hidden while retaining ImGui windows.");
        ImGui.EndDisabled();
        if (!HiddenUiVisibilityPatches.IsApplied || !NativeWindowFilter.IsReady)
            ImGui.TextColored(new Brutal.Numerics.float4(1f, 0.3f, 0.3f, 1f), "UI visibility unavailable; check the log.");
        SubmodUI.EndContentArea();
    }

    public IEnumerable<ISaveParticipant> SaveParticipants => new[]
    {
        VisibilitySettings.CreateParticipant()
    };
}
