using System;
using KSA;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using MeowSci.KsaAbstractions;

namespace MeowSci.KitchenSinkLib;

/// <summary>
/// Submod for kitchen-sink: a collection of one-off hacks and fixes for KSA.
/// </summary>
public sealed partial class KitchenSinkSubmod : ISubmod
{
    public string Name => "Kitchen Sink";
    public string Tooltip => "Random collection of one-off hacks and fixes for KSA.";

    public static KitchenSinkSubmod? Instance { get; private set; }

    public void Initialize() { Instance = this; }

    public void Update(double dt)
    {
        GLoadProtection.Prune(VehicleProvider.GetAllVehicles(includeDebris: true));
        IvaCameraUnlock.Update();
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##ks_content");
        RenderIvaForceRender();
        RenderCapsuleGlassExperiment();
        RenderIvaCamera();
        RenderFixInvisibleSubparts();
        RenderGLoadProtection();
        SubmodUI.EndContentArea();
    }

    private void RenderFixInvisibleSubparts()
    {
        ImGui.SeparatorText("Fix Invisible Subparts");
        ImGui.TextWrapped("Workaround for a KSA bug where subparts become invisible in the editor. Click the button below to reinitialize the vehicle part tree.");
        ImGui.Spacing();

        if (ImGui.Button("Refresh Vehicle", new float2(334f, 36f)))
        {
            var editor = Program.Editor;
            if (editor?.EditingSpace?.Parts != null)
            {
                var oldStates = editor.EditingSpace.Parts.States;
                editor.EditingSpace.Parts.ReinitializeDerivedValues(oldStates);
                Console.WriteLine("kitchen-sink: ReinitializeDerivedValues called on editor parts.");
            }
            else
            {
                Console.WriteLine("kitchen-sink: Editor or parts not available — open the vehicle editor first.");
            }
        }
    }

    private void RenderIvaForceRender()
    {
        ImGui.SeparatorText("Force IVA Rendering");
        ImGui.TextWrapped("Force interior (IVA) parts to render even when not in IVA camera mode.");
        ImGui.Spacing();

        var enabled = IvaForceRender.Enabled;
        if (ImGui.Checkbox("Always Render IVA Interiors", ref enabled))
            IvaForceRender.Enabled = enabled;
    }

    public void Dispose()
    {
        CapsuleGlassExperiment.SetEnabled(false);
        IvaCameraUnlock.Disable();
        IvaCameraUnlock.Reset();
        ResetGLoadProtection();
        if (Instance == this) Instance = null;
    }

    private static void RenderCapsuleGlassExperiment()
    {
        ImGui.SeparatorText("Capsule Glass Experiment"u8);
        ImGui.TextWrapped("Try viewing the stock medium (Gemini) capsule interior through its windows. Hides the two opaque exterior window surfaces and renders the cabin using its existing IVA glass."u8);
        var enabled = CapsuleGlassExperiment.Enabled;
        ImGui.BeginDisabled(!CapsuleGlassExperiment.IsAvailable && !enabled);
        if (ImGui.Checkbox("See Inside Capsule (Experimental)"u8, ref enabled))
            CapsuleGlassExperiment.SetEnabled(enabled);
        ImGui.EndDisabled();
        if (!CapsuleGlassExperiment.IsAvailable)
            ImGui.TextDisabled("Capsule glass rendering patches are unavailable."u8);
        else if (enabled)
            ImGui.TextDisabled("IVA interiors are shown while this experiment is on."u8);
        ImGui.TextWrapped("Experimental: stock glass is strongly tinted. Other exterior windows are unchanged."u8);
    }
}
