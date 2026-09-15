using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.KitchenSinkLib;

public sealed partial class KitchenSinkSubmod
{
    private readonly ImInputString _vehicleFilter = new(256);
    private Vehicle? _selectedVehicle;

    private void RenderGLoadProtection()
    {
        ImGui.Spacing();
        ImGui.SeparatorText("G-load Invincibility"u8);
        ImGui.TextWrapped("Protect selected vehicles from G-load destruction. Collisions, part crash tolerances and fluid-pressure damage still apply."u8);
        ImGui.TextDisabled("Protected vehicles are saved with the Unscience scene."u8);
        var vehicles = VehicleProvider.GetAllVehicles();
        vehicles.RemoveAll(vehicle => vehicle.IsDisposed);
        if (_selectedVehicle != null && !vehicles.Any(vehicle => ReferenceEquals(vehicle, _selectedVehicle)))
            _selectedVehicle = null;

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##ks_gload_selector"u8, 2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("##label"u8, ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##picker"u8, ImGuiTableColumnFlags.WidthStretch, 3f);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Vehicle"u8);
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.BeginCombo("##ks_gload_vehicle"u8, _selectedVehicle?.Id ?? "Select..."))
            {
                if (ImGui.IsWindowAppearing())
                {
                    ImGui.SetKeyboardFocusHere();
                    _vehicleFilter.Clear();
                }
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputTextWithHint("##ks_gload_filter"u8, "filter..."u8, _vehicleFilter);
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

        bool canAdd = GLoadProtectionPatches.IsApplied && _selectedVehicle != null
            && !GLoadProtection.Contains(_selectedVehicle);
        ImGui.BeginDisabled(!canAdd);
        if (ImGui.Button(" Add G-load Invincible "u8) && _selectedVehicle != null)
        {
            GLoadProtection.Add(_selectedVehicle);
            Console.WriteLine($"kitchen-sink: G-load protection added to '{_selectedVehicle.Id}'.");
        }
        ImGui.EndDisabled();
        if (!GLoadProtectionPatches.IsApplied)
            ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), "G-load protection is unavailable; see the mod log."u8);

        var active = GLoadProtection.Snapshot().OrderBy(vehicle => vehicle.Id, StringComparer.Ordinal).ToArray();
        if (active.Length == 0)
        {
            ImGui.TextDisabled("No vehicles protected."u8);
            return;
        }
        ImGui.Spacing();
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##ks_gload_active"u8, 3,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("Vehicle"u8, ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Protection"u8, ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Actions"u8, ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableHeadersRow();
            for (int i = 0; i < active.Length; i++)
            {
                ImGui.PushID(i);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(active[i].Id);
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text("G-load invincible"u8);
                ImGui.TableNextColumn();
                if (ImGui.Button(" Delete "u8))
                {
                    GLoadProtection.Remove(active[i]);
                    Console.WriteLine($"kitchen-sink: G-load protection removed from '{active[i].Id}'.");
                }
                ImGui.SetItemTooltip("Remove G-load protection from this vehicle."u8);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private void ResetGLoadPicker()
    {
        _selectedVehicle = null;
        _vehicleFilter.Clear();
    }
}
