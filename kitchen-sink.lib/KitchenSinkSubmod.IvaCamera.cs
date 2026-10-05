using Brutal.ImGuiApi;

namespace MeowSci.KitchenSinkLib;

public sealed partial class KitchenSinkSubmod
{
    private static void RenderIvaCamera()
    {
        ImGui.SeparatorText("IVA Camera"u8);
        bool enabled = IvaCameraUnlock.Enabled;
        bool canEnable = IvaCameraUnlock.CanEnable(out string reason);
        ImGui.BeginDisabled(!enabled && !canEnable);
        if (ImGui.Checkbox("Unlock IVA Camera"u8, ref enabled))
        {
            if (enabled) IvaCameraUnlock.Enable();
            else IvaCameraUnlock.Disable();
        }
        ImGui.EndDisabled();
        ImGui.TextWrapped("Move with the free cam controls while keeping IVA interiors and lighting. Turn off to return to your seat."u8);
        if (!enabled && !canEnable) ImGui.TextWrapped(reason);
        if (IvaCameraUnlock.LastError != null) ImGui.TextWrapped(IvaCameraUnlock.LastError);
        if (!enabled) return;
        float speed = (float)IvaCameraUnlock.Speed;
        if (ImGui.DragFloat("Move speed (m/s)"u8, ref speed, 0.01f, 0.01f, 100f, "%.2f"u8))
            IvaCameraUnlock.Speed = speed;
        ImGui.TextWrapped("Hold left mouse to look; scroll to adjust speed. Alt releases the cursor. Ray tracing uses your game graphics setting."u8);
        if (ImGui.Button("Return to Seat"u8)) IvaCameraUnlock.Disable();
    }
}
