using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.DentWizardLib;

public sealed partial class DentWizardSubmod
{
    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##dent_wizard_content");
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6, 6));
        if (ImGui.BeginTable("##dent_form", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch, 3);
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Source");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            if (_armed || _pending != null) ImGui.BeginDisabled();
            DrawSourcePicker();
            if (_armed || _pending != null) ImGui.EndDisabled();
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Speed (m/s)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            // No AlwaysClamp flag or post-edit clamp: manual input may exceed drag bounds.
            ImGui.DragFloat("##dent_speed", ref _speed, 0.1f, 0.1f, 100f, "%.3f");
            ImGui.SetItemTooltip("Relative to the target. Ctrl-click to type any finite value >= 0.001 m/s; dragging uses 0.1–100.");
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        ImGui.Spacing();
        bool validSpeed = LaunchMath.IsValidSpeed(_speed);
        bool available = _initialized && PhysicsFrameHook.IsApplied && !Program.EditorFlag && _source != null && validSpeed;
        bool disableAim = !available || _armed || _pending != null;
        if (disableAim) ImGui.BeginDisabled();
        if (ImGui.Button(" Aim and fire ")) Arm(automatic: false);
        if (disableAim) ImGui.EndDisabled();
        ImGui.SameLine(0, 8);
        // An active toggle must remain usable even if the speed becomes invalid or a shot is queued.
        bool disableAutomatic = !_automatic && (!available || _pending != null);
        bool highlightAutomatic = _automatic;
        if (disableAutomatic) ImGui.BeginDisabled();
        if (highlightAutomatic) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
        if (ImGui.Button(" Automatic mode ")) ToggleAutomaticMode();
        if (highlightAutomatic) ImGui.PopStyleColor();
        if (disableAutomatic) ImGui.EndDisabled();
        ImGui.SetItemTooltip("Stay in click-to-fire mode. Each click re-fires the same source; press again to turn off.");
        if (_automatic)
            ImGui.TextColored(new float4(.4f, 1, .4f, 1), "Automatic mode ON — each world click re-fires the source.");
        ImGui.Spacing();
        if (_armed || _pending != null)
        {
            if (ImGui.Button(" Cancel ")) Cancel("Launch cancelled.");
        }
        if (!validSpeed) ImGui.TextColored(new float4(1, .3f, .3f, 1), "Enter a finite speed of at least 0.001 m/s.");
        if (!PhysicsFrameHook.IsApplied) ImGui.TextWrapped("Launch unavailable: the physics handoff hook did not load. See the game log.");
        ImGui.TextWrapped("Click a vessel or terrain within 10 km. Source moves to the camera; the shot inherits the target's velocity. Esc or right-click cancels.");
        if (!string.IsNullOrEmpty(_status))
        {
            ImGui.Spacing();
            ImGui.TextColored(_error ? new float4(1, .3f, .3f, 1) : new float4(.4f, 1, .4f, 1), _status);
        }
        SubmodUI.EndContentArea();
    }

    private void DrawSourcePicker()
    {
        if (!ImGui.BeginCombo("##dent_source", _source?.Id ?? "Select a vessel...")) return;
        if (ImGui.IsWindowAppearing()) { ImGui.SetKeyboardFocusHere(); _filter.Clear(); }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##dent_filter", "filter vessels...", _filter);
        string filter = _filter.ToString().Trim();
        int index = 0;
        foreach (var vehicle in VehicleProvider.GetAllVehicles(includeDebris: true))
        {
            if (vehicle.IsDisposed || vehicle.IsEditedVehicle) continue;
            string label = $"{vehicle.Id}{(vehicle is KittenEva ? " (EVA)" : vehicle.IsDebris ? " (debris)" : "")}";
            if (!label.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            bool selected = ReferenceEquals(_source, vehicle);
            ImGui.PushID(index++);
            if (ImGui.Selectable(label, selected)) _source = vehicle;
            if (selected) ImGui.SetItemDefaultFocus();
            ImGui.PopID();
        }
        ImGui.EndCombo();
    }

    public void RenderFloatingWindows()
    {
        if (!_armed) return;
        if (Program.EditorFlag || _source == null || !PhysicsFrameHook.IsApplied)
        { Cancel("Launch cancelled — source or flight scene unavailable."); return; }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape) || ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        { Cancel("Launch cancelled."); return; }
        var pos = ImGui.GetMousePos() + new float2(18, 18);
        ImString hint = $"{(_automatic ? "automatic" : "fire")} {_source.Id} at {_speed:G6} m/s — click target (Esc cancels)";
        var draw = ImGui.GetForegroundDrawList();
        draw.AddText(pos + new float2(1, 1), ImColor8.Black, hint);
        draw.AddText(pos, ImColor8.White, hint);
        if (_pending != null || ImGui.GetIO().WantCaptureMouse || !Program.HoveredViewport.IsMain()
            || !ImGui.IsMouseClicked(ImGuiMouseButton.Left)) return;
        try
        {
            if (!LaunchMath.IsValidSpeed(_speed)) throw new InvalidOperationException("Enter a finite speed >= 0.001 m/s.");
            AcceptPick(DentWizardPicker.Pick(_source, _speed));
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }
}
