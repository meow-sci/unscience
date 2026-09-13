using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.ItsSoShinyLib;

public sealed partial class ItsSoShinySubmod : ISubmod
{
    public static ItsSoShinySubmod? Instance { get; private set; }

    public string Name => "Its So Shiny - Light Grids";
    public string Tooltip => "Dynamic pixel grids built from built-in LightPart parts.";

    private readonly HashSet<(string vehicleId, string gridName)> _pendingDestroy = new();
    private readonly Queue<(double delayBefore, Action action)> _deferredActions = new();
    private double _deferredTimer;

    private readonly ImInputString _newGridName = new(64);
    private readonly ImInputString _vehicleFilter = new(128);
    private int _selectedVehicleIndex = -1;

    private string _createMessage = "";
    private bool _createMessageIsError;

    private int _configCols = 8;
    private int _configRows = 8;
    private float _configSpacing = 0.75f;
    private float _configOffsetX;
    private float _configOffsetY = 3f;
    private float _configOffsetZ = 2f;
    private float _configLightScale = 0.5f;
    private int _configLayoutIndex;
    private float _configIntensity = 1f;
    private float4 _configColor = new(1f, 1f, 1f, 1f);

    public void Initialize()
    {
        Instance = this;
    }

    public void Update(double dt)
    {
        if (_deferredActions.Count > 0)
        {
            _deferredTimer -= dt;
            if (_deferredTimer <= 0)
            {
                var (_, action) = _deferredActions.Dequeue();
                action();
                _deferredTimer = _deferredActions.Count > 0 ? _deferredActions.Peek().delayBefore : 0;
            }
        }

        ShinyGridManager.TickAll(dt);
    }

    public void RenderContent()
    {
        RenderMenuBar();
        SubmodUI.BeginContentArea("##iss_content");

        RenderCreateSection();

        var grids = ShinyGridManager.Grids;
        int vehicleCount = grids.Count > 0 ? grids.Values.Select(s => s.VehicleId).Distinct().Count() : 0;
        ImGui.Spacing();
        ImGui.SeparatorText($"shiny grids ( {vehicleCount} vehicle(s), {grids.Count} grid(s) )");

        bool renderMeshes = ShinyPatchState.RenderShinyParts;
        if (ImGui.Checkbox("Render light meshes", ref renderMeshes))
            ShinyPatchState.RenderShinyParts = renderMeshes;
        ImGui.SameLine(0, 4);
        ImGui.TextDisabled("(?)");
        ImGui.SetItemTooltip("When off (default): meshes render only while the light is active, so the emissive\nappears and disappears with the light. When on: meshes always render regardless of state.");

        foreach (var state in grids.Values.ToList())
            RenderGridSection(state);

        SubmodUI.EndContentArea();
    }

    public void Dispose()
    {
        ShinyGridManager.Clear();
        _pendingDestroy.Clear();
        _deferredActions.Clear();
        Instance = null;
    }

    private void RenderMenuBar()
    {
        if (!ImGui.BeginMenuBar()) return;

        if (ImGui.BeginMenu("Debug"))
        {
            if (ImGui.MenuItem("Scan for shiny grids"))
                DoGlobalScan();
            ImGui.EndMenu();
        }

        ImGui.EndMenuBar();
    }

    private void RenderCreateSection()
    {
        if (!ImGui.CollapsingHeader("Create Shiny Grid (?)", ImGuiTreeNodeFlags.DefaultOpen))
            return;
        ImGui.SetItemTooltip("Build a dynamic grid of built-in LightPart parts on a vehicle. Each pixel is one light part, controlled through its light switch.");

        var tableFlags = ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##iss_params", 4, tableFlags))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Columns");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.DragInt("##iss_cols", ref _configCols, 0.5f, 1, 256);
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Rows");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.DragInt("##iss_rows", ref _configRows, 0.5f, 1, 256);

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Spacing (m)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.DragFloat("##iss_space", ref _configSpacing, 0.01f, 0.05f, 100f);
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Light Scale");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.DragFloat("##iss_scale", ref _configLightScale, 0.01f, 0.01f, 10f);

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Offset: x,y,z");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.DragFloat("##iss_ox", ref _configOffsetX, 0.1f);
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.DragFloat("##iss_oy", ref _configOffsetY, 0.1f);
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.DragFloat("##iss_oz", ref _configOffsetZ, 0.1f);

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        if (ImGui.RadioButton("Flat##iss", _configLayoutIndex == 0)) _configLayoutIndex = 0;
        ImGui.SameLine();
        if (ImGui.RadioButton("Cylinder##iss", _configLayoutIndex == 1)) _configLayoutIndex = 1;

        var table2Flags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##iss_select", 2, table2Flags))
        {
            ImGui.TableSetupColumn("##iss_lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##iss_widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Vehicle");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); RenderVehicleCombo();

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Grid Name");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.InputText("##iss_gridname", _newGridName);

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Intensity");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.DragFloat("##iss_intensity", ref _configIntensity, 0.1f, 0f, 25f);

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Color");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.ColorEdit4("##iss_color", ref _configColor, ImGuiColorEditFlags.NoLabel);

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        if (ImGui.Button(" Create ##iss"))
            DoBuildGrid();
        ImGui.SameLine(0, 12);
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"Total parts: {_configCols * _configRows}");

        if (!string.IsNullOrEmpty(_createMessage))
        {
            ImGui.Spacing();
            var color = _createMessageIsError ? new float4(1f, 0.3f, 0.3f, 1f) : new float4(0.4f, 1f, 0.4f, 1f);
            ImGui.TextColored(color, _createMessage);
        }
    }

    private void RenderGridSection(ShinyGridState state)
    {
        string gridId = $"{state.VehicleId}_{state.GridName}";
        if (!ImGui.CollapsingHeader($"Shiny Grid: '{state.GridName}' on '{state.VehicleId}##iss_grid_{gridId}'"))
            return;

        var wpadX = ImGui.GetStyle().WindowPadding.X;
        float childW = ImGui.GetContentRegionAvail().X + wpadX * 2;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() - wpadX);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(20f, 10f));
        ImGui.BeginChild($"child_{gridId}", new float2(childW, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleVar();

        RenderGridInfoTable(state, gridId);
        RenderGridAppearanceControls(state, gridId);
        RenderPatternButtons(state, gridId);

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(KSAColor.Xkcd.Scarlet));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(KSAColor.Xkcd.PaleGrey));
        if (ImGui.Button($" Destroy ##{gridId}"))
            ScheduleDestroy(state);
        ImGui.PopStyleColor();
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.EndChild();
    }

    private void RenderGridInfoTable(ShinyGridState state, string gridId)
    {
        var tableFlags = ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable($"##iss_gridinfo_{gridId}", 4, tableFlags))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Name");
            ImGui.TableNextColumn(); ImGui.Text(state.GridName);
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Rows");
            ImGui.TableNextColumn(); ImGui.Text($"{state.ShinyGrid.Grid.Rows}");

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Discovery");
            ImGui.TableNextColumn(); ImGui.Text(state.ShinyGrid.IsOwned ? "Owned" : "Scanned");
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Columns");
            ImGui.TableNextColumn(); ImGui.Text($"{state.ShinyGrid.Grid.Cols}");

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Status");
            ImGui.TableNextColumn();
            if (_pendingDestroy.Contains((state.VehicleId, state.GridName)))
                ImGui.TextColored(new float4(1f, 0.8f, 0.2f, 1f), "Destroying...");
            else if (state.Scroll.IsActive)
                ImGui.TextDisabled("Scrolling");
            else
                ImGui.TextDisabled("Ready");
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private static void RenderGridAppearanceControls(ShinyGridState state, string gridId)
    {
        var controlsFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        var color4 = new float4(state.Color.X, state.Color.Y, state.Color.Z, 1f);
        var intensity = state.Intensity;

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable($"##iss_grid_controls_{gridId}", 2, controlsFlags))
        {
            ImGui.TableSetupColumn("##iss_grid_clbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##iss_grid_cwidget", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Intensity");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            if (ImGui.DragFloat($"##iss_grid_intensity_{gridId}", ref intensity, 0.1f, 0f, 25f))
                ShinyGridManager.SetAppearance(state.VehicleId, state.GridName, state.Color, intensity);

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Color");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            if (ImGui.ColorEdit4($"##iss_grid_color_{gridId}", ref color4, ImGuiColorEditFlags.NoLabel))
                ShinyGridManager.SetAppearance(state.VehicleId, state.GridName, new float3(color4.X, color4.Y, color4.Z), state.Intensity);

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private static void RenderPatternButtons(ShinyGridState state, string gridId)
    {
        if (ImGui.Button($" Off ##{gridId}"))
            ShinyGridManager.TurnOff(state.VehicleId, state.GridName);
        ImGui.SameLine(0, 8);
        if (ImGui.Button($" All ##{gridId}"))
            ShinyGridManager.ApplyPattern(state.VehicleId, state.GridName, ShinyPixelPatterns.AllOn);
        ImGui.SameLine(0, 8);
        if (ImGui.Button($" Rows ##{gridId}"))
            ShinyGridManager.ApplyPattern(state.VehicleId, state.GridName, ShinyPixelPatterns.AlternatingRows);
        ImGui.SameLine(0, 8);
        if (ImGui.Button($" Columns ##{gridId}"))
            ShinyGridManager.ApplyPattern(state.VehicleId, state.GridName, ShinyPixelPatterns.AlternatingCols);
        ImGui.SameLine(0, 8);
        if (ImGui.Button($" Checkers ##{gridId}"))
            ShinyGridManager.ApplyPattern(state.VehicleId, state.GridName, ShinyPixelPatterns.Checkerboard);
    }

    private void RenderVehicleCombo()
    {
        var vehicles = VehicleProvider.GetAllVehicles();
        string preview = _selectedVehicleIndex >= 0 && _selectedVehicleIndex < vehicles.Count ? vehicles[_selectedVehicleIndex].Id : "Select...";

        if (!ImGui.BeginCombo("##iss_vehicle", preview))
            return;

        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
            _vehicleFilter.Clear();
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##iss_vehicle_filter", "filter..."u8, _vehicleFilter);
        string filterText = _vehicleFilter.ToString().Trim();
        for (int i = 0; i < vehicles.Count; i++)
        {
            var vehicleId = vehicles[i].Id;
            if (filterText.Length > 0 && !vehicleId.Contains(filterText, StringComparison.OrdinalIgnoreCase)) continue;
            bool selected = _selectedVehicleIndex == i;
            if (ImGui.Selectable(vehicleId + "##issv", selected))
                _selectedVehicleIndex = i;
            if (selected) ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    private Vehicle? GetSelectedVehicle()
    {
        var vehicles = VehicleProvider.GetAllVehicles();
        return _selectedVehicleIndex >= 0 && _selectedVehicleIndex < vehicles.Count ? vehicles[_selectedVehicleIndex] : null;
    }

    private void DoBuildGrid()
    {
        var vehicle = GetSelectedVehicle();
        if (vehicle == null)
        {
            SetCreateMessage("Select a vehicle first", true);
            return;
        }

        var gridName = _newGridName.ToString().Trim();
        if (!ValidateGridName(gridName, vehicle.Id))
            return;

        try
        {
            var config = new ShinyGridConfig
            {
                Width = _configCols,
                Height = _configRows,
                Spacing = _configSpacing,
                OffsetX = _configOffsetX,
                OffsetY = _configOffsetY,
                OffsetZ = _configOffsetZ,
                PartScale = _configLightScale,
                Layout = _configLayoutIndex == 1 ? ShinyGridLayout.Cylinder : ShinyGridLayout.Flat,
            };
            var color = new float3(_configColor.X, _configColor.Y, _configColor.Z);
            var grid = ShinyGridBuilder.BuildGrid(vehicle, gridName, config);
            if (grid == null)
            {
                SetCreateMessage("Build failed; check console log", true);
                return;
            }

            ShinyGridManager.Register(vehicle, gridName, grid, color, _configIntensity);
            SetCreateMessage($"Built grid '{gridName}': {grid.Grid.Cols}x{grid.Grid.Rows} ({grid.OwnedParts.Count} parts)", false);
            _newGridName.Clear();
        }
        catch (Exception ex)
        {
            SetCreateMessage($"Build error: {ex.Message}", true);
            Console.WriteLine($"its-so-shiny: Build error: {ex}");
        }
    }

    private void DoGlobalScan()
    {
        try
        {
            var color = new float3(_configColor.X, _configColor.Y, _configColor.Z);
            var (discovered, names) = ShinyGridManager.ScanAllVehicles(color, _configIntensity);
            if (discovered == 0)
                SetCreateMessage("No shiny grids found on any vehicle", true);
            else
                SetCreateMessage($"Discovered {discovered} grid(s): {string.Join(", ", names)}", false);
        }
        catch (Exception ex)
        {
            SetCreateMessage($"Scan error: {ex.Message}", true);
            Console.WriteLine($"its-so-shiny: scan error: {ex}");
        }
    }

    private void ScheduleDestroy(ShinyGridState state)
    {
        var vehicleId = state.VehicleId;
        var gridName = state.GridName;
        var vehicle = state.Vehicle;
        var grid = state.ShinyGrid;
        _pendingDestroy.Add((vehicleId, gridName));

        ScheduleDeferred(0, () => ShinyGridManager.TurnOff(vehicleId, gridName));
        ScheduleDeferred(0.25, () =>
        {
            try
            {
                ShinyGridBuilder.DestroyGrid(vehicle, grid);
                ShinyGridManager.Unregister(vehicleId, gridName);
                _pendingDestroy.Remove((vehicleId, gridName));
                SetCreateMessage($"Grid '{gridName}' destroyed", false);
            }
            catch (Exception ex)
            {
                _pendingDestroy.Remove((vehicleId, gridName));
                SetCreateMessage($"Destroy failed: {ex.Message}", true);
                Console.WriteLine($"its-so-shiny: destroy error: {ex}");
            }
        });
    }

    private void ScheduleDeferred(double delaySeconds, Action action)
    {
        bool wasEmpty = _deferredActions.Count == 0;
        _deferredActions.Enqueue((delaySeconds, action));
        if (wasEmpty)
            _deferredTimer = delaySeconds;
    }

    private bool ValidateGridName(string gridName, string vehicleId)
    {
        if (string.IsNullOrWhiteSpace(gridName))
        {
            SetCreateMessage("Grid name is required", true);
            return false;
        }
        if (!ShinyPixelGrid.IsValidGridName(gridName))
        {
            SetCreateMessage("Grid name must contain only letters, digits, and hyphens (no underscores)", true);
            return false;
        }
        if (ShinyGridManager.Get(vehicleId, gridName) != null)
        {
            SetCreateMessage($"Grid '{gridName}' already exists for this vehicle", true);
            return false;
        }
        return true;
    }

    private void SetCreateMessage(string message, bool isError)
    {
        _createMessage = message;
        _createMessageIsError = isError;
        Console.WriteLine($"its-so-shiny: {message}");
    }
}