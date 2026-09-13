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

    private readonly FlexoSubpartTest _flexoSubpartTest = new();
    private readonly FlexoPartTest _flexoPartTest = new();

    public void Initialize() { Instance = this; }

    public void Update(double dt) { }

    /// <summary>
    /// Called from a Harmony prefix on Universe.ExecuteNextVehicleSolvers.
    /// This is the only safe phase to mutate vehicle part trees and call
    /// UpdateAfterPartTreeModification() — it runs before the solver task
    /// is prepared, so kinematic and analytic state timestamps stay coherent.
    /// </summary>
    public void UpdateBeforeVehicleSolvers(double dt)
    {
        _flexoSubpartTest.UpdateBeforeVehicleSolvers(dt);
        _flexoPartTest.UpdateBeforeVehicleSolvers(dt);
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##ks_content");
        RenderIvaForceRender();
        RenderFixInvisibleSubparts();
        _flexoSubpartTest.RenderContent();
        _flexoPartTest.RenderContent();
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
        if (Instance == this) Instance = null;
    }
}
