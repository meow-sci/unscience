using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Logging;
using Brutal.Numerics;
using KSA;
using MeowSci.WarPaintLib;
using MeowSci.DohLib.Spawning;
using MeowSci.KsaAbstractions;

namespace MeowSci.DohLib;

/// <summary>
/// ISubmod implementation for DOH (Dynamically Originating Hominids).
/// Provides kitten spawning controls and management UI.
/// Used by unscience supermod and standalone doh mod.
/// </summary>
public sealed partial class DohSubmod : ISubmod, MeowSci.KsaAbstractions.Persistence.ISaveParticipantSource
{
    public string Name => "DOH - Clone Kittens";
    public string Tooltip => "Spawn kittens and optionally apply a saved War Paint preset.";

    // Core systems
    private SpawnedKittenRegistry? _registry;
    private KittenSpawner? _spawner;

    // UI state — vehicle selection. Held by object, never by list index: the game's vehicle list
    // swap-removes on deregister (and debris is filtered out), so indices shift under a selection.
    private Vehicle? _selectedVehicle;
    private readonly ImInputString _vehicleFilter = new(128);

    // UI state — character selection
    private string[] _availableCharacters = Array.Empty<string>();
    private int _selectedCharacterIndex = -1;
    private readonly ImInputString _characterFilter = new(128);

    // UI state — spawn parameters
    private float3 _offset = DefaultSpawnOffset;
    private int _spawnCount = 1;
    private long _nextRosterMaintenanceAt;
    private readonly ImInputString _spawnName = new(SpawnNameCapacity, SpawnRequest.DefaultNamePrefix);
    private bool _useCustomColor;
    private float4 _tintColor = new float4(1f, 1f, 1f, 1f);
    private bool _uniquePerKitten;

    // The legacy color fields above remain solely for lossless v1 save compatibility.
    private string? _paintPresetName;
    private PaintSettings? _spawnPaint;

    public void Initialize()
    {
        WarPaintService.Shared.Initialize();
        _registry = new SpawnedKittenRegistry();
        _spawner = new KittenSpawner(_registry);

        _availableCharacters = _spawner.GetAvailableCharacters();
        InitializePlacement();
        Console.WriteLine("doh: DohSubmod initialized");
    }

    public void Update(double dt)
    {
        // Kittens the game removes itself (boarding, destruction) must not keep GPU material slots.
        _spawner?.PruneDisposed();
        long now = Environment.TickCount64;
        if (now >= _nextRosterMaintenanceAt)
        {
            _spawner?.MaintainRoster();
            _nextRosterMaintenanceAt = now + 1000;
        }
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##doh_content");

        RenderSpawnControls();
        ImGui.SeparatorText("Spawned Kittens");
        RenderKittenList();

        SubmodUI.EndContentArea();
    }

    public void Dispose()
    {
        CancelPlacement();
        PhysicsFrameHook.BeforePhysics -= PlacementFrame;
        _placementCancelled = null;
        // Vehicles belong to KSA. Unloading DOH must not destroy avatars still in its render list.
        _registry?.Clear();
        _spawner = null;
        _registry = null;
        _selectedVehicle = null;
        Console.WriteLine("doh: DohSubmod disposed");
    }

    // ---- Spawn Controls ----

    private void RenderSpawnControls()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        var flags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        if (ImGui.BeginTable("##doh_spawn_params", 2, flags))
        {
            ImGui.TableSetupColumn("##lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            RenderVehicleRow();
            RenderCharacterRow();

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding();
            ImGui.Text(_spawnCount > 1 ? "Name prefix" : "Vehicle name");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##doh_name", _spawnName);
            ImGui.SetItemTooltip("Single spawn: use this name if available. Occupied names advance to the next number. Batches: append numbers to this prefix. Leave blank for automatic Kitten_doh_ names.");

            // Offset
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Offset");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat3("##doh_offset", ref _offset, 1f, -1000f, 1000f);

            // Count
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Count");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.SliderInt("##doh_count", ref _spawnCount, 1, 20);

            RenderPaintPresetRow();

            ImGui.EndTable();
        }
        ImGui.Spacing();

        if (ImGui.BeginTable("##doh_spawn_actions"u8, 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(_selectedVehicle == null || _spawner == null);
            if (ImGui.Button(" Spawn Kitten(s) ##doh"u8, new float2(-1, 0)))
                DoSpawn();
            ImGui.EndDisabled();

            ImGui.TableNextColumn();
            ImGui.BeginDisabled(_spawner == null || !PhysicsFrameHook.IsApplied
                || !MeowSci.DohLib.Placement.PlacementInputPatches.IsApplied || Universe.CurrentSystem == null
                || Program.EditorFlag || Program.MainViewport.Mode == CameraMode.Map);
            if (ImGui.Button(" Spawn at... ##doh"u8, new float2(-1, 0))) ArmPlacement();
            ImGui.EndDisabled();
            ImGui.SetItemTooltip("Click ground or a vessel part to place one kitten with its capsule clear of the surface. Esc/right-click cancels. Count and offset apply to Spawn Kitten(s)."u8);
            ImGui.EndTable();
        }
        if (ImGui.BeginTable("##doh_catalog_actions"u8, 1, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableNextColumn();
            if (ImGui.Button(" Refresh Characters ##doh"u8, new float2(-1, 0)))
            {
                _availableCharacters = _spawner?.GetAvailableCharacters() ?? Array.Empty<string>();
                LogStatus($"Found {_availableCharacters.Length} characters.", false);
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding

        ImGui.Spacing();
    }

    private void RenderVehicleRow()
    {
        var vehicles = VehicleProvider.GetAllVehicles();

        // Drop a selection the game removed; never slide onto whatever took its list position.
        if (_selectedVehicle != null && (_selectedVehicle.IsDisposed || !vehicles.Contains(_selectedVehicle)))
            _selectedVehicle = null;

        string preview = _selectedVehicle?.Id ?? "Select vehicle...";

        ImGui.TableNextRow();
        ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Vehicle");
        ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##doh_vehicle", preview))
        {
            if (ImGui.IsWindowAppearing())
            {
                ImGui.SetKeyboardFocusHere();
                _vehicleFilter.Clear();
            }
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##doh_vfilter", "filter..."u8, _vehicleFilter);
            string vehicleFilterText = _vehicleFilter.ToString().Trim();

            for (int i = 0; i < vehicles.Count; i++)
            {
                var vehicle = vehicles[i];
                if (vehicleFilterText.Length > 0
                    && !vehicle.Id.Contains(vehicleFilterText, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                bool sel = ReferenceEquals(vehicle, _selectedVehicle);
                ImGui.PushID(i);
                if (ImGui.Selectable(vehicle.Id + "##doh_v", sel))
                    _selectedVehicle = vehicle;
                ImGui.PopID();
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
    }

    private void RenderCharacterRow()
    {
        if (_selectedCharacterIndex >= _availableCharacters.Length)
            _selectedCharacterIndex = -1;

        string charPreview = _selectedCharacterIndex >= 0
            ? _availableCharacters[_selectedCharacterIndex]
            : "(random)";

        ImGui.TableNextRow();
        ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Character");
        ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##doh_char", charPreview))
        {
            if (ImGui.IsWindowAppearing())
            {
                ImGui.SetKeyboardFocusHere();
                _characterFilter.Clear();
            }
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##doh_cfilter", "filter..."u8, _characterFilter);
            string characterFilterText = _characterFilter.ToString().Trim();

            if (characterFilterText.Length == 0
                || "(random)".Contains(characterFilterText, StringComparison.OrdinalIgnoreCase))
            {
                bool noSel = _selectedCharacterIndex < 0;
                if (ImGui.Selectable("(random)##doh_c", noSel))
                    _selectedCharacterIndex = -1;
                if (noSel) ImGui.SetItemDefaultFocus();
            }

            for (int i = 0; i < _availableCharacters.Length; i++)
            {
                if (characterFilterText.Length > 0
                    && !_availableCharacters[i].Contains(characterFilterText, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                bool sel = _selectedCharacterIndex == i;
                if (ImGui.Selectable(_availableCharacters[i] + "##doh_c", sel))
                    _selectedCharacterIndex = i;
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
    }

    /// <summary>The selected vehicle if it still exists; otherwise clears it and reports why.</summary>
    private Vehicle? GetSpawnTarget()
    {
        if (_selectedVehicle == null || _selectedVehicle.IsDisposed)
        {
            _selectedVehicle = null;
            LogStatus("No vehicle selected (the previous selection no longer exists).", true);
            return null;
        }

        return _selectedVehicle;
    }

    private void ReportSpawnResult(SpawnResult result, Func<int, string> successMessage)
    {
        if (!result.Success)
            LogStatus(result.Error ?? "Spawn failed.", true);
        else if (result.Warning != null)
            LogStatus(successMessage(result.Count) + " " + result.Warning, true);
        else
            LogStatus(successMessage(result.Count), false);
    }

    private static void LogStatus(string message, bool isError)
    {
        if (isError) DefaultCategory.Log.Error($"doh: {message}");
        else DefaultCategory.Log.Info($"doh: {message}");
    }
}
