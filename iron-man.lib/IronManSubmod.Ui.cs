using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace MeowSci.IronManLib;

public sealed partial class IronManSubmod
{
    private Part.Connector? _selected;
    private float3 _position;
    private float3 _direction = new(0, 0, -1);
    private float _radius = 0.08f;

    public void RenderContent()
    {
        ImGui.TextWrapped("Fit tanks, engines and RCS to an existing EVA kitten. Choose native EVA movement or Iron Man rocket flight."u8);
        if (!IronManPatches.Ready)
        {
            ImGui.TextWrapped("Iron Man could not install its game hooks. See the game log."u8);
            return;
        }
        var kitten = Target;
        if (kitten == null)
        {
            ImGui.TextDisabled("Control an EVA kitten to configure it."u8);
            ImGui.Text($"Kittens in Iron Man mode: {_enabled.Count}");
            return;
        }

        ImGui.Text($"Kitten: {kitten.Id}");
        bool rocket = IsEnabled(kitten);
        bool editing = Program.Editor?.ExistingVehicle == kitten;
        ImGui.Text("flight mode"u8);
        ImGui.BeginDisabled(_pending || Program.IsEditorOpen);
        float width = Math.Max(1f, (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f);
        if (ModeButton("eva mode"u8, !rocket, width) && rocket)
            Queue(kitten, () => Disable(kitten));
        ImGui.SameLine();
        if (ModeButton("iron man"u8, rocket, width) && !rocket)
            Queue(kitten, () => Enable(kitten));
        ImGui.EndDisabled();
        ImGui.TextWrapped(_status);

        ImGui.TextWrapped(rocket
            ? "Iron Man uses vessel physics, headward rocket controls and the flight computer."
            : "EVA mode uses native walking, swimming, ladder and MMU controls. Equipment stays attached.");
        ImGui.BeginDisabled(_pending);
        if (!editing)
        {
            if (ImGui.Button("Edit this kitten"u8)) Queue(kitten, () => OpenEditor(kitten));
            if (rocket) DrawFlightControls(kitten);
        }
        else
        {
            if (ImGui.Button("Return to flight"u8))
                Program.Editor!.RequestExit(launchNewVehicle: false);
            DrawConnectors(kitten);
        }
        ImGui.EndDisabled();
        ImGui.Separator();
        ImGui.TextWrapped("Saves containing these nodes require Iron Man to load connected parts. Keep Unscience installed. Flight mode starts as EVA after loading."u8);
        ImGui.TextWrapped("Use this existing-kitten workflow; launching an EVA blueprint from an empty stock editor does not create a kitten."u8);
    }

    private static bool ModeButton(ImString label, bool selected, float width)
    {
        if (selected) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
        try { return ImGui.Button(label, new float2(width, 0)); }
        finally { if (selected) ImGui.PopStyleColor(); }
    }

    private void DrawFlightControls(KittenEva kitten)
    {
        ImGui.SeparatorText("Rocket controls"u8);
        ImGui.TextWrapped(kitten.ControlPart == null
            ? "Rocket orientation: head is the nose, feet are the tail. Autopilot Up points your head away from the surface."
            : "Control orientation follows your selected control part or docking port.");
        ImGui.TextWrapped("Use the stock Autopilot Settings panel for attitude targets, reference frames and burn controls. If hidden, enable it from the HUD menu."u8);
        var axes = kitten.FlightComputer.ActiveControlSystem;
        ImGui.Text($"Attitude control: X {axes.X}, Y {axes.Y}, Z {axes.Z}");
        ImGui.TextWrapped("Rcs uses fueled thrusters; Tvc uses engine gimbals with thrust. None means the flight computer currently has no actuator assigned on that axis."u8);
        if (ImGui.Button("Arm attached engines"u8))
            Queue(kitten, () =>
            {
                foreach (var engine in kitten.Parts.Modules.Get<EngineController>())
                    engine.SetIsActive(null, true);
                kitten.Parts.RecomputeAllDerivedData();
                _status = "Engines armed. Use Ignite and the game's throttle controls.";
            });
        ImGui.SameLine();
        if (ImGui.Button("Ignite"u8)) Queue(kitten, () => kitten.SetEnum(VehicleEngine.MainIgnite));
        if (ImGui.Button("Shut down and disarm"u8)) Queue(kitten, () => StopEngines(kitten));
        bool rcs = kitten.FlightComputer.RCSMode == FlightComputerRCSMode.Enabled;
        if (ImGui.Checkbox("RCS enabled"u8, ref rcs))
        {
            bool desired = rcs;
            Queue(kitten, () => kitten.FlightComputer.RCSMode = desired
                ? FlightComputerRCSMode.Enabled : FlightComputerRCSMode.Disabled);
        }
        if (ImGui.Button("Manual attitude"u8))
            Queue(kitten, () => kitten.FlightComputer.AttitudeMode = FlightComputerAttitudeMode.Manual);
        ImGui.SameLine();
        if (ImGui.Button("Hold rotation rate"u8))
            Queue(kitten, () => kitten.FlightComputer.RateHold(VehicleReferenceFrame.EclBody));
        ImGui.TextWrapped("Use normal vessel movement and throttle bindings. Supply compatible propellant and activate the equipment's resource group in the editor."u8);
    }

    private void DrawConnectors(KittenEva kitten)
    {
        var root = Program.Editor!.EditingSpace.Parts?.Root;
        if (root == null) return;
        ImGui.SeparatorText("Body attachment nodes"u8);
        ImGui.TextWrapped("Coordinates are body-local metres: -Z is up, +X forward. Node direction is its outward snapping axis. Connected nodes are locked; detach the equipment first."u8);
        var nodes = IronManConnectors.GetOwned(root);
        if (_selected != null && !nodes.Contains(_selected)) _selected = null;
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (ImGui.Selectable($"{node.Id}##iron-node-{i}", ReferenceEquals(node, _selected)))
            {
                _selected = node;
                _position = float3.Pack(node.PositionParentAsmb);
                _direction = float3.Pack(double3.UnitX.Transform(node.Asmb2ParentAsmb));
                _radius = (float)node.Scale.Y;
            }
            if (ImGui.IsItemHovered()) Program.Editor.HighlightConnector(node);
        }
        ImGui.DragFloat3("Position (m)"u8, ref _position, 0.01f);
        ImGui.DragFloat3("Outward direction"u8, ref _direction, 0.01f, -1f, 1f);
        ImGui.DragFloat("Radius (m)"u8, ref _radius, 0.005f, 0.005f, 2f, "%.3f");
        if (ImGui.Button("Point up"u8)) _direction = new float3(0, 0, -1);
        ImGui.SameLine();
        if (ImGui.Button("Point down"u8)) _direction = new float3(0, 0, 1);

        if (ImGui.Button("Add node"u8))
        {
            var position = double3.Unpack(_position);
            var direction = double3.Unpack(_direction);
            double radius = _radius;
            QueueConnectorEdit(kitten, root, () =>
                _selected = IronManConnectors.Add(root, "Node", position, direction, radius));
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(nodes.Count != 0);
        if (ImGui.Button("Restore default up/down pair"u8))
            QueueConnectorEdit(kitten, root, () => IronManConnectors.EnsureDefaults(root));
        ImGui.EndDisabled();
        if (_selected == null) return;
        var selected = _selected;
        bool connected = selected.Connection != null;
        ImGui.BeginDisabled(connected);
        if (ImGui.Button("Apply node changes"u8))
        {
            var position = double3.Unpack(_position);
            var direction = double3.Unpack(_direction);
            double radius = _radius;
            QueueConnectorEdit(kitten, root, () =>
            {
                if (!IronManConnectors.Update(selected, position, direction, radius))
                    throw new InvalidOperationException("Detach connected equipment before moving this node.");
            });
        }
        ImGui.SameLine();
        if (ImGui.Button("Remove node"u8))
            QueueConnectorEdit(kitten, root, () =>
            {
                if (!IronManConnectors.Remove(selected))
                    throw new InvalidOperationException("Detach connected equipment before removing this node.");
                _selected = null;
            });
        ImGui.EndDisabled();
    }

    private void QueueConnectorEdit(KittenEva kitten, Part root, Action edit)
    {
        Queue(kitten, () =>
        {
            var editor = Program.Editor;
            if (!IsConfigured(kitten) || editor?.ExistingVehicle != kitten || editor.EditingSpace.Parts?.Root != root)
                throw new InvalidOperationException("The edited body changed; select the node again.");
            edit();
            editor.EditingSpace.Parts.RecomputeAllDerivedData();
            editor.EditingSpace.Parts.HasUnsavedChanges = true;
            editor.EditingSpace.Parts.PerformanceSequences.SetDirty();
            editor.IsChangeStartOrEnd = true;
            _status = "Attachment nodes updated. Snap equipment onto a node using the stock part browser.";
        });
    }
}
