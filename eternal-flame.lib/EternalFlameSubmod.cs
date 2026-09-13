using System;
using System.Linq;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.EternalFlameLib;

public sealed partial class EternalFlameSubmod : ISubmod
{
    public string Name => "Eternal Flame - Infinite Fuel";
    public string Tooltip => "Automatically refills fuel tanks on the selected vehicle at regular intervals.";

    public static EternalFlameSubmod? Instance { get; private set; }

    private FuelManager _fuelManager = null!;
    private readonly ImInputString _vehicleFilter = new ImInputString(128);
    private int _selectedVehicleIndex = -1;
    private int _refillIntervalMs = 100;

    public void Initialize()
    {
        Instance = this;
        _fuelManager = new FuelManager();
        _refillIntervalMs = _fuelManager.RefillIntervalMs;
        Console.WriteLine("eternal-flame: submod Initialize");
    }

    public void Update(double dt) => _fuelManager.Update(dt);

    public void UpdateBeforeVehicleSolvers() => _fuelManager.UpdateElectricityBeforeVehicleSolvers();

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##ef_content");

        RenderVehicleSelector();
        RenderAddButton();
        RenderMonitoredSection();

        SubmodUI.EndContentArea();
    }

    public void Dispose()
    {
        Console.WriteLine("eternal-flame: submod Dispose");
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    private void RenderVehicleSelector()
    {
        var vehicles = VehicleProvider.GetAllVehicles();
        if (vehicles.Count == 0)
        {
            ImGui.TextDisabled("No vehicles available");
            return;
        }

        var vehicleNames = vehicles.Select(v => v.Id).ToArray();

        if (_selectedVehicleIndex >= vehicleNames.Length)
            _selectedVehicleIndex = -1;

        string preview = _selectedVehicleIndex >= 0 ? vehicleNames[_selectedVehicleIndex] : "Select...";

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Vehicle");
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Once monitored, a background thread will every N milliseconds issue");
            ImGui.Text("the equivalent of a console \"refill\" command and top up that");
            ImGui.Text("vehicle's fuel.");
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##ef_selector", preview))
        {
            if (ImGui.IsWindowAppearing())
            {
                ImGui.SetKeyboardFocusHere();
                _vehicleFilter.Clear();
            }
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##ef_VehicleFilter", "filter..."u8, _vehicleFilter);
            string vehicleFilterText = _vehicleFilter.ToString().Trim();

            for (int i = 0; i < vehicleNames.Length; i++)
            {
                if (vehicleFilterText.Length > 0
                    && !vehicleNames[i].Contains(vehicleFilterText, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isSelected = _selectedVehicleIndex == i;
                if (ImGui.Selectable(vehicleNames[i] + "##ef", isSelected))
                    _selectedVehicleIndex = i;
            }
            ImGui.EndCombo();
        }
    }

    private void RenderAddButton()
    {
        var vehicles = VehicleProvider.GetAllVehicles();
        bool canAdd = _selectedVehicleIndex >= 0
            && _selectedVehicleIndex < vehicles.Count;

        if (!canAdd) ImGui.BeginDisabled();
        if (ImGui.Button(" Add ##ef"))
        {
            var v = vehicles![_selectedVehicleIndex];
            _fuelManager.AddVehicle(v.Id, v.Id);
            _selectedVehicleIndex = -1;
            _vehicleFilter.Clear();
        }
        if (!canAdd) ImGui.EndDisabled();
    }

    private void RenderMonitoredSection()
    {
        var monitored = _fuelManager.MonitoredVehicles;

        ImGui.Spacing();
        ImGui.SeparatorText($"Gassing up ( {monitored.Count} ) Vehicles");

        if (monitored.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Nothing to see here.  See above to start filling!");
            return;
        }

        // ImGui.Spacing();
        // ImGui.Spacing();
        ImGui.NewLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Refill Interval (ms)");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.DragInt("##ef_interval", ref _refillIntervalMs, 1, 0, 5000))
        {
            _fuelManager.RefillIntervalMs = _refillIntervalMs;
            Console.WriteLine($"eternal-flame: interval changed - intervalMs={_refillIntervalMs}");
        }
        // ImGui.NewLine();
        ImGui.Spacing();
        // ImGui.NewLine();
        // ImGui.Spacing();

        RenderMonitoredTable();
    }

    private void RenderMonitoredTable()
    {
        var monitored = _fuelManager.MonitoredVehicles;

        if (ImGui.BeginTable("##ef_MonitoredVehicles", 4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Fuel", ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("Elec", ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("Vehicle", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##ef_Remove", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableHeadersRow();

            string? toRemove = null;
            for (int i = 0; i < monitored.Count; i++)
            {
                var entry = monitored[i];
                ImGui.TableNextRow();

                float checkboxSize = ImGui.GetFrameHeight();

                ImGui.TableSetColumnIndex(0);
                bool refillFuel = entry.RefillFuel;
                float colWidth0 = ImGui.GetColumnWidth();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (colWidth0 - checkboxSize) / 2f);
                if (ImGui.Checkbox($"##ef_fuel_{i}", ref refillFuel))
                {
                    entry.RefillFuel = refillFuel;
                    Console.WriteLine($"eternal-flame: fuel toggle - vehicle={entry.DisplayName}, enabled={entry.RefillFuel}");
                }

                ImGui.TableSetColumnIndex(1);
                bool refillElec = entry.RefillElectricity;
                float colWidth1 = ImGui.GetColumnWidth();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (colWidth1 - checkboxSize) / 2f);
                if (ImGui.Checkbox($"##ef_elec_{i}", ref refillElec))
                {
                    entry.RefillElectricity = refillElec;
                    Console.WriteLine($"eternal-flame: electric toggle - vehicle={entry.DisplayName}, enabled={entry.RefillElectricity}");
                }

                ImGui.TableSetColumnIndex(2);
                ImGui.Text(entry.DisplayName);

                ImGui.TableSetColumnIndex(3);
                float btnWidth = ImGui.CalcTextSize(" X ").X + ImGui.GetStyle().FramePadding.X * 2f;
                float removeColWidth = ImGui.GetColumnWidth();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (removeColWidth - btnWidth) / 2f);
                if (ImGui.SmallButton($" X ##ef_{i}"))
                    toRemove = entry.VehicleId;
            }

            ImGui.EndTable();

            if (toRemove != null)
                _fuelManager.RemoveVehicle(toRemove);
        }
    }
}
