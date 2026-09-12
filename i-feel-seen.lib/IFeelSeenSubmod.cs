using System;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using MeowSci.KsaAbstractions;

namespace MeowSci.IFeelSeenLib;

public sealed partial class IFeelSeenSubmod : ISubmod
{
    public string Name => "I Feel Seen - Always Visible Vehicles";
    public string Tooltip => "Makes vehicles visible from infinite distance.";

    private VehicleTracker _tracker = null!;
    private int _pendingVehicleIndex;
    private readonly ImInputString _vehicleFilter = new(128);

    /// <summary>Exposed so Harmony patches can reference it for vehicle render distance overrides.</summary>
    public VehicleTracker Tracker => _tracker;

    public void Initialize()
    {
        _tracker = new VehicleTracker();
    }

    public void Update(double dt) { }

    public void RenderContent()
    {
        var vehicles = VehicleProvider.GetAllVehicles();

        SubmodUI.BeginContentArea("##ifs_content");

        // Vehicle selector — 2-column proportional table (label 1fr, widget 3fr)
        var selectorFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##ifs_selector", 2, selectorFlags))
        {
            ImGui.TableSetupColumn("##ifs_lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##ifs_widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Vehicle");

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);

            if (vehicles.Count > 0)
            {
                _pendingVehicleIndex = Math.Clamp(_pendingVehicleIndex, 0, vehicles.Count - 1);
                string preview = vehicles[_pendingVehicleIndex].Id;
                if (ImGui.BeginCombo("##ifs_vehicle", preview))
                {
                    if (ImGui.IsWindowAppearing())
                    {
                        ImGui.SetKeyboardFocusHere();
                        _vehicleFilter.Clear();
                    }
                    ImGui.SetNextItemWidth(-1f);
                    ImGui.InputTextWithHint("##ifs_vehicle_filter", "filter..."u8, _vehicleFilter);
                    string filterText = _vehicleFilter.ToString().Trim();
                    for (int i = 0; i < vehicles.Count; i++)
                    {
                        if (filterText.Length > 0 &&
                            !vehicles[i].Id.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                            continue;
                        bool selected = _pendingVehicleIndex == i;
                        if (ImGui.Selectable(vehicles[i].Id, selected))
                            _pendingVehicleIndex = i;
                        if (selected) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }
            else
            {
                ImGui.BeginDisabled();
                if (ImGui.BeginCombo("##ifs_vehicle", "No vehicles available"))
                    ImGui.EndCombo();
                ImGui.EndDisabled();
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding

        ImGui.Spacing();
        if (ImGui.Button(" Add Vehicle ##ifs") && vehicles.Count > 0 && _pendingVehicleIndex >= 0 && _pendingVehicleIndex < vehicles.Count)
            _tracker.AddVehicle(vehicles[_pendingVehicleIndex]);

        if (_tracker.Tracked.Count > 0)
        {
            ImGui.SeparatorText("I can see ...");

            // Tracked vehicles — 3-column fixed table
            TrackedVehicle? toRemove = null;
            var tracked = _tracker.Tracked;

            var trackedFlags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX;
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
            if (ImGui.BeginTable("##ifs_tracked", 3, trackedFlags))
            {
                float chkW = ImGui.GetFrameHeight();
                float delW = ImGui.CalcTextSize(" del ").X + ImGui.GetStyle().FramePadding.X * 2f;
                ImGui.TableSetupColumn("##ifs_chk", ImGuiTableColumnFlags.WidthFixed, chkW);
                ImGui.TableSetupColumn("##ifs_name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("##ifs_del", ImGuiTableColumnFlags.WidthFixed, delW);

                for (int i = 0; i < tracked.Count; i++)
                {
                    var entry = tracked[i];
                    ImGui.PushID(i + 10000); // offset to avoid ID collision with other submods

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    bool seeMe = entry.SeeMe;
                    if (ImGui.Checkbox("##ifs_chk", ref seeMe))
                        entry.SeeMe = seeMe;

                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(entry.Vehicle.Id);

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.Button(" del ##ifs"))
                        toRemove = entry;

                    ImGui.PopID();
                }

                ImGui.EndTable();
            }
            ImGui.PopStyleVar(); // CellPadding

            if (toRemove != null)
                _tracker.RemoveVehicle(toRemove.Vehicle);
        }

        SubmodUI.EndContentArea();
    }

    public void Dispose()
    {
        _tracker.Clear();
    }
}
