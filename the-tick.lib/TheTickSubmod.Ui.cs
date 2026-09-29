using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.TheTickLib;

public sealed partial class TheTickSubmod
{
    private readonly ImInputString _vehicleFilter = new(256);
    private Vehicle? _selectedVehicle;

    private void RenderProtectionPanel()
    {
        ImGui.SeparatorText("Indestructible Vessels"u8);
        ImGui.TextWrapped("Chosen vessels cannot be destroyed by forces: every part reports an unbreakable crash tolerance, and whole-vehicle G-load and aero/hydro pressure destruction is suppressed. Collisions still push things around."u8);
        ImGui.TextDisabled("Protected vessels are saved with the Unscience scene. Debris shed by other vessels is never protected."u8);
        ImGui.Spacing();

        var vehicles = VehicleProvider.GetAllVehicles();
        vehicles.RemoveAll(vehicle => vehicle.IsDisposed);
        if (_selectedVehicle != null && !vehicles.Any(vehicle => ReferenceEquals(vehicle, _selectedVehicle)))
            _selectedVehicle = null;

        RenderVehiclePicker(vehicles);
        RenderAddButtons();
        RenderActiveTable();
    }

    private void RenderVehiclePicker(System.Collections.Generic.List<Vehicle> vehicles)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##tick_selector"u8, 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("##label"u8, ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##picker"u8, ImGuiTableColumnFlags.WidthStretch, 3f);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Vessel"u8);
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.BeginCombo("##tick_vehicle"u8, _selectedVehicle?.Id ?? "Select..."))
            {
                if (ImGui.IsWindowAppearing())
                {
                    ImGui.SetKeyboardFocusHere();
                    _vehicleFilter.Clear();
                }
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputTextWithHint("##tick_filter"u8, "filter..."u8, _vehicleFilter);
                string filter = _vehicleFilter.ToString();
                for (int i = 0; i < vehicles.Count; i++)
                {
                    var vehicle = vehicles[i];
                    if (!vehicle.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                    ImGui.PushID(i);
                    bool selected = ReferenceEquals(_selectedVehicle, vehicle);
                    if (ImGui.Selectable(vehicle.Id, selected)) _selectedVehicle = vehicle;
                    if (selected) ImGui.SetItemDefaultFocus();
                    ImGui.PopID();
                }
                ImGui.EndCombo();
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private void RenderAddButtons()
    {
        bool ready = TheTickPatches.IsApplied;
        bool canAdd = ready && _selectedVehicle != null && !TickProtection.Contains(_selectedVehicle);
        ImGui.BeginDisabled(!canAdd);
        if (ImGui.Button(" Make Indestructible "u8) && _selectedVehicle != null)
            Protect(_selectedVehicle);
        ImGui.EndDisabled();
        ImGui.SetItemTooltip("Protect the selected vessel."u8);

        var controlled = VehicleProvider.GetControlledVehicle();
        bool canAddControlled = ready && controlled != null && !controlled.IsDisposed && !TickProtection.Contains(controlled);
        ImGui.SameLine(0, 8);
        ImGui.BeginDisabled(!canAddControlled);
        if (ImGui.Button(" Protect Controlled Vessel "u8) && controlled != null)
            Protect(controlled);
        ImGui.EndDisabled();
        ImGui.SetItemTooltip("Protect the vessel you are currently flying."u8);

        if (!ready)
            ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), "Indestructibility is unavailable; see the mod log."u8);
    }

    private static void Protect(Vehicle vehicle)
    {
        if (TickProtection.Add(vehicle))
            Console.WriteLine($"the-tick: '{vehicle.Id}' is now indestructible.");
    }

    private void RenderActiveTable()
    {
        var active = TickProtection.Snapshot().OrderBy(vehicle => vehicle.Id, StringComparer.Ordinal).ToArray();
        if (active.Length == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("No vessels protected."u8);
            return;
        }
        ImGui.Spacing();
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##tick_active"u8, 3,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("Vessel"u8, ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Parts"u8, ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Actions"u8, ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableHeadersRow();
            for (int i = 0; i < active.Length; i++)
            {
                var vehicle = active[i];
                ImGui.PushID(i);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(vehicle.Id);
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text($"{vehicle.Parts.Count} unbreakable");
                ImGui.TableNextColumn();
                if (ImGui.Button(" Delete "u8))
                {
                    TickProtection.Remove(vehicle);
                    Console.WriteLine($"the-tick: '{vehicle.Id}' is destructible again.");
                }
                ImGui.SetItemTooltip("Restore native damage for this vessel."u8);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private void ResetPicker()
    {
        _selectedVehicle = null;
        _vehicleFilter.Clear();
    }
}
