using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.BlinkyLib;

public sealed partial class BlinkySubmod : ISubmod
{
    public string Name => "Blinky - LCD Grids";
    public string Tooltip => "Dynamic LCD grid displays on vehicles using engines.";

    // Per-grid UI state, keyed by (vehicleId, gridName)
    // Tracks grids pending destroy so we can show status
    private readonly HashSet<(string vehicleId, string gridName)> _pendingDestroy = new();

    // Grid name input for creating new grids
    private readonly ImInputString _newGridName = new ImInputString(64);

    // Build/scan status message
    private string _createMessage = "";
    private bool _createMessageIsError;

    // Grid configuration (global — applies to next build on any vehicle)
    private int _configCols = 8;
    private int _configRows = 8;
    private float _configSpacing = 5.0f;
    private float _configOffsetX = 0f;
    private float _configOffsetY = 0f;
    private float _configOffsetZ = 0f;
    private float _configEngineScale = 0.010f;
    private string _enginePartId = "CorePropulsionA_Prefab_EngineA3";
    private int _configLayoutIndex = 0; // 0=Flat, 1=Cylinder
    private int _enginePresetIndex = 1;
    private readonly ImInputString _engineFilter = new(128);

    // Vehicle selection for grid creation
    private readonly ImInputString _vehicleFilter = new(128);
    private int _selectedVehicleIndex = -1;

    // Deferred action runner
    private readonly Queue<(double delayBefore, Action action)> _deferredActions = new();
    private double _deferredTimer = 0;

    // Known engine part IDs for quick-select.
    // EngineA1 is deliberately absent: it was removed from the game's Content between
    // builds 5018 and 5117, and ModLibrary.Get throws for ids that no longer exist.
    private static readonly string[] EnginePresets = new[]
    {
        "CorePropulsionA_Prefab_EngineA2",
        "CorePropulsionA_Prefab_EngineA3",
        "CorePropulsionA_Prefab_EngineA4",
        "CorePropulsionA_Prefab_EngineA5",
        "CorePropulsionA_Prefab_EngineA6",
    };

    public void Initialize() { }

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

        BlinkyGridManager.TickAll(dt);
    }

    private void ScheduleDeferred(double delaySeconds, Action action)
    {
        bool wasEmpty = _deferredActions.Count == 0;
        _deferredActions.Enqueue((delaySeconds, action));
        if (wasEmpty)
            _deferredTimer = delaySeconds;
    }

    //  Main Render 

    public void RenderContent()
    {
        RenderMenuBar();

        SubmodUI.BeginContentArea("##bk_content");

        RenderCreateSection();

        //  Separator with dynamic counts 
        var grids = BlinkyGridManager.Grids;
        int vehicleCount = grids.Count > 0
            ? grids.Values.Select(s => s.VehicleId).Distinct().Count()
            : 0;
        ImGui.Spacing();
        ImGui.SeparatorText($"blinky grids ( {vehicleCount} vehicle(s), {grids.Count} grid(s) )");

        //  Non-LCD engine warning for controlled vehicle 
        RenderNonLcdEngineWarning();

        //  Ignition / throttle warning for controlled vehicle 
        RenderIgnitionWarning();

        //  Render engine meshes checkbox 
        bool renderEngines = BlinkyPatchState.RenderPixelParts;
        if (ImGui.Checkbox("Render engine meshes", ref renderEngines))
            BlinkyPatchState.RenderPixelParts = renderEngines;
        ImGui.SameLine(0, 4);
        ImGui.TextDisabled("(?)");
        ImGui.SetItemTooltip("Disable for a significant performance boost.\nEngine part meshes are expensive to render — hiding them\nkeeps the pixel grid functional without the GPU cost.");

        ImGui.Spacing();

        //  Per-grid sections (all vehicles) 
        foreach (var gs in grids.Values.ToList())
            RenderGridSection(gs);

        SubmodUI.EndContentArea();
    }

    //  Menu Bar 

    private void RenderMenuBar()
    {
        if (!ImGui.BeginMenuBar()) return;

        if (ImGui.BeginMenu("Debug"))
        {
            if (ImGui.MenuItem("Scan for blinky grids"))
                DoGlobalScan();
            ImGui.EndMenu();
        }

        ImGui.EndMenuBar();
    }

    //  Non-LCD Engine Warning 

    private void RenderNonLcdEngineWarning()
    {
        var vehicle = VehicleProvider.GetControlledVehicle();
        if (vehicle == null) return;

        // Only check vehicles that have at least one registered blinky grid
        bool hasGrid = false;
        foreach (var gs in BlinkyGridManager.Grids.Values)
        {
            if (gs.VehicleId == vehicle.Id) { hasGrid = true; break; }
        }
        if (!hasGrid) return;

        if (!NonLcdEngineCache.AnyActive(vehicle)) return;

        ImGui.TextColored(KSAColor.Xkcd.CandyPink, $"Vehicle '{vehicle.Id}' has non-LCD engines active");
        ImGui.SameLine(0, 4);
        ImGui.TextDisabled("(?)");
        ImGui.SetItemTooltip($"Typically you don't want any non-LCD engines active\n\nThese will cause the craft to rotate\n\nUnless that's what you want...");

        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(KSAColor.Xkcd.BloodOrange));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(KSAColor.Xkcd.PaleGrey));

        if (ImGui.SmallButton(" Deactivate ##nonlcd"))
            NonLcdEngineCache.DeactivateAll(vehicle);

        ImGui.PopStyleColor();
        ImGui.PopStyleColor();

        ImGui.Spacing();
    }

    //  Ignition Warning 

    /// <summary>
    /// Pixels are lit by activating real engines, so nothing shows unless the vehicle is
    /// ignited with a non-zero throttle. Surfaces that here rather than leaving the player
    /// staring at a dark grid.
    /// </summary>
    private void RenderIgnitionWarning()
    {
        var vehicle = VehicleProvider.GetControlledVehicle();
        if (vehicle == null) return;

        bool hasGrid = false;
        foreach (var gs in BlinkyGridManager.Grids.Values)
        {
            if (gs.VehicleId == vehicle.Id) { hasGrid = true; break; }
        }
        if (!hasGrid) return;

        bool ignited = vehicle.IsSet(VehicleEngine.MainIgnite, false);
        float throttle = vehicle.GetManualThrottle();
        bool autoBurn = vehicle.FlightComputer.BurnMode == FlightComputerBurnMode.Auto;

        if (ignited && throttle > 0f && !autoBurn) return;

        string reason = autoBurn
            ? "burn mode is Auto, which clears ignition every frame"
            : !ignited ? "the vehicle is not ignited" : "the throttle is at zero";

        ImGui.TextColored(KSAColor.Xkcd.CandyPink, $"Pixels cannot light — {reason}");
        ImGui.SameLine(0, 4);
        ImGui.TextDisabled("(?)");
        ImGui.SetItemTooltip("Each pixel is a real engine.\nIt only fires while the vehicle is ignited and the throttle is above zero.");

        if (!ignited && !autoBurn)
        {
            ImGui.SameLine(0, 8);
            if (ImGui.SmallButton(" Ignite ##bk_ignite"))
                vehicle.SetEnum(VehicleEngine.MainIgnite);
        }

        ImGui.Spacing();
    }

    //  Create Section 

    private void RenderCreateSection()
    {
        if (!ImGui.CollapsingHeader("Create Blinky Grid (?)", ImGuiTreeNodeFlags.DefaultOpen))
            return;
        ImGui.SetItemTooltip("Build a dynamic NxN grid of engine parts on a vehicle.\nEach pixel is an a/b engine pair for net zero thrust.\nUse patterns or the RPC API to control individual pixels.");

        // ---- Grid parameters table (4 even columns) ----
        var tableFlags = ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##blinky_params", 4, tableFlags))
        {
            // Row: Columns / Rows
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Columns");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.DragInt("##bk_cols", ref _configCols, 0.5f, 1, 256);
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Rows");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.DragInt("##bk_rows", ref _configRows, 0.5f, 1, 256);

            // Row: Spacing / Engine Scale
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Spacing (m)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.DragFloat("##bk_space", ref _configSpacing, 0.01f, 0.0f, 100.0f);
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Engine Scale");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.DragFloat("##bk_scale", ref _configEngineScale, 0.001f, 0.001f, 1.0f);

            // Row: Position X / Y / Z
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Offset: x,y,z");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.DragFloat("##bk_ox", ref _configOffsetX, 0.1f);
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.DragFloat("##bk_oy", ref _configOffsetY, 0.1f);
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.DragFloat("##bk_oz", ref _configOffsetZ, 0.1f);

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding

        // ---- Layout radio buttons (outside table) ----
        if (ImGui.RadioButton("Flat##bk", _configLayoutIndex == 0)) _configLayoutIndex = 0;
        ImGui.SameLine();
        if (ImGui.RadioButton("Cylinder##bk", _configLayoutIndex == 1)) _configLayoutIndex = 1;

        // ---- Engine / Vehicle / Grid Name table (2 columns: 1/4 label, 3/4 widget) ----
        var table2Flags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##blinky_select", 2, table2Flags))
        {
            ImGui.TableSetupColumn("##bk_lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##bk_widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            // Row: Engine
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Engine");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); RenderEngineCombo();

            // Row: Vehicle
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Vehicle");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); RenderVehicleCombo();

            // Row: Grid Name
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Grid Name");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); ImGui.InputText("##bk_gridname", _newGridName);

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding

        // ---- Create button + total parts ----
        if (ImGui.Button(" Create ##bk"))
            DoBuildGrid();
        ImGui.SameLine(0, 12);
        int totalParts = _configCols * _configRows * 2;
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"Total parts: {totalParts}");
        ImGui.SameLine(0, 4);
        ImGui.TextDisabled("(?)");
        ImGui.SetItemTooltip($"{_configCols} cols \u00d7 {_configRows} rows \u00d7 2 = {totalParts} parts.\nEach pixel has an a/b engine pair that thrust in\nopposite directions to have net zero force.");

        ImGui.Spacing();

        // Status message
        if (!string.IsNullOrEmpty(_createMessage))
        {
            var msgColor = _createMessageIsError
                ? new float4(1f, 0.3f, 0.3f, 1f)
                : new float4(0.4f, 1f, 0.4f, 1f);
            ImGui.TextColored(msgColor, _createMessage);
        }
    }

    //  Per-Grid Section 

    private void RenderGridSection(GridState gs)
    {
        var vehicleId = gs.VehicleId;
        var gridName = gs.GridName;
        var gridId = $"{vehicleId}_{gridName}";
        

        if (!ImGui.CollapsingHeader($"LCD Grid: '{gridName}' on '{vehicleId}##grid_{gridId}'"))
            return;

        var wpadX = ImGui.GetStyle().WindowPadding.X;
        float childW = ImGui.GetContentRegionAvail().X + wpadX * 2;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() - wpadX);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(20f, 10f));
        ImGui.BeginChild($"child_{gridId}", new float2(childW, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleVar(); // WindowPadding

        // Info table
        var tableFlags = ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable($"##gridinfo_{gridId}", 4, tableFlags))
        {

            // Name
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Name");
            ImGui.TableNextColumn(); ImGui.Text(gridName);
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Rows");
            ImGui.TableNextColumn(); ImGui.Text($"{gs.BlinkyGrid.Grid.Rows}");

            // Layout + Size
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Discovery");
            ImGui.TableNextColumn(); ImGui.Text(gs.BlinkyGrid.IsOwned ? "Owned" : "Scanned");
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Columns");
            ImGui.TableNextColumn(); ImGui.Text($"{gs.BlinkyGrid.Grid.Cols}");

            // Status
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Status");
            ImGui.TableNextColumn();
            if (_pendingDestroy.Contains((vehicleId, gridName)))
                ImGui.TextColored(new float4(1f, 0.8f, 0.2f, 1f), "Destroying...");
            else
                ImGui.TextDisabled("Ready");
            ImGui.TableNextColumn(); // skip
            ImGui.TableNextColumn(); // skip

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding

        // Pattern buttons
        if (ImGui.Button($" Off ##{gridId}"))
            BlinkyGridManager.TurnOff(vehicleId, gridName);
        ImGui.SameLine(0, 8);
        if (ImGui.Button($" All ##{gridId}"))
            BlinkyGridManager.ApplyPattern(vehicleId, gridName, PixelPatterns.AllOn);
        ImGui.SameLine(0, 8);
        if (ImGui.Button($" Rows ##{gridId}"))
            BlinkyGridManager.ApplyPattern(vehicleId, gridName, PixelPatterns.AlternatingRows);
        ImGui.SameLine(0, 8);
        if (ImGui.Button($" Columns ##{gridId}"))
            BlinkyGridManager.ApplyPattern(vehicleId, gridName, PixelPatterns.AlternatingCols);
        ImGui.SameLine(0, 8);
        if (ImGui.Button($" Checkers ##{gridId}"))
            BlinkyGridManager.ApplyPattern(vehicleId, gridName, PixelPatterns.Checkerboard);

        ImGui.Spacing();

        // Destroy button (red border)
        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(KSAColor.Xkcd.Scarlet));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(KSAColor.Xkcd.PaleGrey));

        if (ImGui.Button($"Destroy##{gridId}"))
            ScheduleDestroy(gs);

        ImGui.PopStyleColor();
        ImGui.PopStyleColor();

        ImGui.SameLine(0, 12);
        if (ImGui.Button($"Diagnose##{gridId}"))
            DiagnoseGrid(gs);

        ImGui.SameLine(0, 12);
        if (ImGui.Button($"Repair Feed##{gridId}"))
            RepairGridFeed(gs);
        ImGui.SameLine(0, 4);
        ImGui.TextDisabled("(?)");
        ImGui.SetItemTooltip("Re-wire this grid's engines into the vehicle's propellant network.\nNeeded for grids found by scanning, or built before the feed-wiring fix.\nWithout a declared feed connection an engine can never light.");
        
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.EndChild();
    }

    //  Engine Combo 

    private void RenderEngineCombo()
    {
        if (!ImGui.BeginCombo("##bk_engine", _enginePartId))
            return;

        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
            _engineFilter.Clear();
        }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##bk_engine_filter", "filter..."u8, _engineFilter);
        string engineFilterText = _engineFilter.ToString().Trim();
        for (int i = 0; i < EnginePresets.Length; i++)
        {
            if (engineFilterText.Length > 0 && !EnginePresets[i].Contains(engineFilterText, StringComparison.OrdinalIgnoreCase)) continue;
            bool sel = _enginePresetIndex == i;
            if (ImGui.Selectable(EnginePresets[i], sel))
            {
                _enginePresetIndex = i;
                _enginePartId = EnginePresets[i];
            }
            if (sel) ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    //  Vehicle Combo 

    private void RenderVehicleCombo()
    {
        var vehicles = VehicleProvider.GetAllVehicles();
        string preview = _selectedVehicleIndex >= 0 && _selectedVehicleIndex < vehicles.Count
            ? vehicles[_selectedVehicleIndex].Id
            : "Select...";

        if (!ImGui.BeginCombo("##bk_vehicle", preview))
            return;

        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
            _vehicleFilter.Clear();
        }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##bk_vehicle_filter", "filter..."u8, _vehicleFilter);
        string vehicleFilterText = _vehicleFilter.ToString().Trim();
        for (int i = 0; i < vehicles.Count; i++)
        {
            var vid = vehicles[i].Id;
            if (vehicleFilterText.Length > 0 && !vid.Contains(vehicleFilterText, StringComparison.OrdinalIgnoreCase)) continue;
            bool sel = _selectedVehicleIndex == i;
            if (ImGui.Selectable(vid + "##bkv", sel))
                _selectedVehicleIndex = i;
            if (sel) ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    /// <summary>Returns the vehicle selected in the vehicle combo, or null.</summary>
    private Vehicle? GetSelectedVehicle()
    {
        var vehicles = VehicleProvider.GetAllVehicles();
        if (_selectedVehicleIndex >= 0 && _selectedVehicleIndex < vehicles.Count)
            return vehicles[_selectedVehicleIndex];
        return null;
    }

    public void Dispose()
    {
        BlinkyGridManager.Clear();
        NonLcdEngineCache.Clear();
        _pendingDestroy.Clear();
    }

    //  Grid Build 

    private void DoBuildGrid()
    {
        var vehicle = GetSelectedVehicle();
        if (vehicle == null)
        {
            SetCreateMessage("Select a vehicle first", true);
            return;
        }

        var vehicleId = vehicle.Id;
        var gridName = _newGridName.ToString().Trim();

        if (!ValidateGridName(gridName, vehicleId))
            return;

        try
        {
            var config = new LcdGridConfig
            {
                Width = _configCols,
                Height = _configRows,
                Spacing = _configSpacing,
                OffsetX = _configOffsetX,
                OffsetY = _configOffsetY,
                OffsetZ = _configOffsetZ,
                PartScale = _configEngineScale,
                EnginePartId = _enginePartId,
                Layout = _configLayoutIndex == 1 ? GridLayout.Cylinder : GridLayout.Flat,
            };

            var grid = LcdGridBuilder.BuildGrid(vehicle, gridName, config);
            if (grid != null)
            {
                BlinkyGridManager.Register(vehicle, gridName, grid);
                SetCreateMessage($"Built grid '{gridName}': {grid.Grid.Cols}x{grid.Grid.Rows} ({grid.OwnedParts.Count} parts)", false);
                _newGridName.Clear();
            }
            else
            {
                SetCreateMessage("Build failed — check console log", true);
            }
        }
        catch (Exception ex)
        {
            SetCreateMessage($"Build error: {ex.Message}", true);
            Console.WriteLine($"blinky: Build error: {ex}");
        }
    }

    //  Global Scan 

    private void DoGlobalScan()
    {
        try
        {
            var (discovered, names) = BlinkyGridManager.ScanAllVehicles();
            if (discovered == 0)
                SetCreateMessage("No blinky grids found on any vehicle", true);
            else
                SetCreateMessage($"Discovered {discovered} grid(s): {string.Join(", ", names)}", false);
        }
        catch (Exception ex)
        {
            SetCreateMessage($"Scan error: {ex.Message}", true);
            Console.WriteLine($"blinky: Global scan error: {ex}");
        }
    }

    //  Destroy 

    private void ScheduleDestroy(GridState gs)
    {
        var capturedVehicleId = gs.VehicleId;
        var capturedGridName = gs.GridName;
        var capturedVehicle = gs.Vehicle;
        var capturedGrid = gs.BlinkyGrid;

        _pendingDestroy.Add((capturedVehicleId, capturedGridName));

        ScheduleDeferred(0, () =>
        {
            Console.WriteLine($"blinky: destroy step 1  shutting down grid '{capturedGridName}'");
            BlinkyGridManager.TurnOff(capturedVehicleId, capturedGridName);
        });
        ScheduleDeferred(2.0, () =>
        {
            try
            {
                Console.WriteLine($"blinky: destroy step 2 — removing parts for grid '{capturedGridName}'");
                LcdGridBuilder.DestroyGrid(capturedVehicle, capturedGrid);
                BlinkyGridManager.Unregister(capturedVehicleId, capturedGridName);
                _pendingDestroy.Remove((capturedVehicleId, capturedGridName));
                SetCreateMessage($"Grid '{capturedGridName}' destroyed", false);
            }
            catch (Exception ex)
            {
                _pendingDestroy.Remove((capturedVehicleId, capturedGridName));
                SetCreateMessage($"Destroy failed: {ex.Message}", true);
                Console.WriteLine($"blinky: Destroy error: {ex}");
            }
        });
    }

    //  Validation 

    private bool ValidateGridName(string gridName, string vehicleId)
    {
        if (string.IsNullOrWhiteSpace(gridName))
        {
            SetCreateMessage("Grid name is required", true);
            return false;
        }
        if (!PixelGrid.IsValidGridName(gridName))
        {
            SetCreateMessage("Grid name must contain only letters, digits, and hyphens (no underscores)", true);
            return false;
        }
        if (BlinkyGridManager.Get(vehicleId, gridName) != null)
        {
            SetCreateMessage($"Grid '{gridName}' already exists for this vehicle", true);
            return false;
        }
        return true;
    }

    //  Helpers 

    private void SetCreateMessage(string msg, bool isError)
    {
        _createMessage = msg;
        _createMessageIsError = isError;
        Console.WriteLine($"blinky: {msg}");
    }

    //  Fuel Feed Repair 

    private void RepairGridFeed(GridState gs)
    {
        try
        {
            int fed = LcdGridBuilder.RepairFuelFeeds(gs.Vehicle, gs.BlinkyGrid);
            int total = gs.BlinkyGrid.Grid.Count * 2;
            SetCreateMessage(fed > 0
                ? $"Grid '{gs.GridName}': {fed}/{total} pixel parts wired to propellant"
                : $"Grid '{gs.GridName}': could not wire any pixel part to propellant — check console log", fed == 0);
        }
        catch (Exception ex)
        {
            SetCreateMessage($"Repair failed: {ex.Message}", true);
            Console.WriteLine($"blinky: Repair error: {ex}");
        }
    }

    //  Ignition Diagnostics 

    /// <summary>
    /// Logs why a grid's pixel engines are or are not lighting. Output goes to
    /// Console.WriteLine, which surfaces in the game's log / debug console.
    ///
    /// A pixel lights only when all of these hold: the vehicle is ignited with a non-zero
    /// throttle, the EngineController is active, and the engine's Combustor resolved a
    /// ResourceManager whose ConsumptionOrder reaches at least one tank. That last one is
    /// the usual failure — it needs a connection on a declared feed connector.
    /// </summary>
    private static void DiagnoseGrid(GridState gs)
    {
        Console.WriteLine($"blinky: ===== DIAGNOSE grid '{gs.GridName}' on '{gs.VehicleId}' =====");

        var vehicle = gs.Vehicle;
        var grid = gs.BlinkyGrid.Grid;

        // Vehicle-level engine inputs — no throttle or no ignition means no pixel lights
        Console.WriteLine($"blinky:   vehicle EngineOn (MainIgnite) = {vehicle.IsSet(VehicleEngine.MainIgnite, false)}");
        Console.WriteLine($"blinky:   vehicle.GetManualThrottle()   = {vehicle.GetManualThrottle():F4}");
        Console.WriteLine($"blinky:   vehicle.FlightComputer.BurnMode = {vehicle.FlightComputer.BurnMode} (Auto clears EngineOn every frame)");

        int pairCount = grid.Engines.Count;
        Console.WriteLine($"blinky:   pixel pairs (positions) in grid = {pairCount}");
        if (pairCount == 0)
        {
            Console.WriteLine("blinky:   WARNING: grid has no pixel pairs — grid scan may have failed");
            Console.WriteLine("blinky: ===== END DIAGNOSE =====");
            return;
        }

        // Grid-wide propellant tally — the single most useful number here
        int fedControllers = 0, totalControllers = 0;
        foreach (var controllers in grid.Engines.Values)
        {
            for (int i = 0; i < controllers.Length; i++)
            {
                totalControllers++;
                if (ReachesPropellant(controllers[i])) fedControllers++;
            }
        }
        Console.WriteLine($"blinky:   controllers reaching propellant = {fedControllers}/{totalControllers}");
        if (fedControllers == 0)
            Console.WriteLine("blinky:   >>> no pixel can light. Press 'Repair Feed' to wire the engines' declared feed connectors to a tank.");

        // Sample the first pixel engine controller in detail
        var firstEntry = System.Linq.Enumerable.First(grid.Engines);
        var (row, col) = firstEntry.Key;
        var sample = firstEntry.Value;
        Console.WriteLine($"blinky:   sampling pixel ({row},{col}): {sample.Length} controller(s)");

        for (int ci = 0; ci < sample.Length; ci++)
            DiagnoseController(sample[ci], ci);

        Console.WriteLine("blinky: ===== END DIAGNOSE =====");
    }

    /// <summary>True when any Combustor core of this controller resolved a tank to draw from.</summary>
    private static bool ReachesPropellant(EngineController controller)
    {
        var cores = controller.Cores;
        if (cores == null) return false;

        for (int i = 0; i < cores.Length; i++)
        {
            if (cores[i] is not Combustor combustor) continue;
            if (PropellantFeedDiagnostics.CountTanks(combustor.ResourceManager) > 0)
                return true;
        }
        return false;
    }

    private static void DiagnoseController(EngineController ctrl, int index)
    {
        var part = ctrl.Parent.FullPart;

        Console.WriteLine($"blinky:   controller[{index}]:");
        Console.WriteLine($"blinky:     IsActive           = {ctrl.IsActive}");
        Console.WriteLine($"blinky:     Parent.FullPart.Id = {part.Id}");
        Console.WriteLine($"blinky:     Part.Stage         = {part.Stage}");
        Console.WriteLine($"blinky:     MinimumThrottle    = {ctrl.MinimumThrottle:F4}");

        var cores = ctrl.Cores;
        if (cores == null || cores.Length == 0)
        {
            Console.WriteLine("blinky:     Cores is null or empty");
        }
        else
        {
            for (int i = 0; i < cores.Length; i++)
            {
                // Since KSA 2026.7.9.5018 the ResourceManager lives on Combustor, not on the
                // RocketCore base; SolidMotor cores legitimately have none.
                if (cores[i] is not Combustor combustor)
                {
                    Console.WriteLine($"blinky:     core[{i}] '{cores[i].TemplateId}' is not a Combustor (no resource manager)");
                    continue;
                }

                var rm = combustor.ResourceManager;
                if (rm == null)
                {
                    Console.WriteLine($"blinky:     core[{i}] '{combustor.TemplateId}': ResourceManager is null");
                    continue;
                }

                var order = rm.ConsumptionOrder;
                int tanks = PropellantFeedDiagnostics.CountTanks(rm);
                Console.WriteLine($"blinky:     core[{i}] '{combustor.TemplateId}': FlowRule={rm.FlowRule}, levels={order.LevelCount}, tanks={tanks}, feedConnectors={combustor.FeedConnectors.Length}");

                var feeds = combustor.FeedConnectors;
                for (int f = 0; f < feeds.Length; f++)
                {
                    var conn = feeds[f].Connection;
                    var other = conn?.OtherPart(part);
                    Console.WriteLine($"blinky:       feed connector '{feeds[f].Id}' -> {(other == null ? "NOT CONNECTED" : $"'{other.Id}' stage={other.Stage}")}");
                }
            }
        }

        Console.WriteLine($"blinky:     Part.Connections.Count = {part.Connections.Count}");
        foreach (var conn in part.Connections)
        {
            var other = conn.OtherPart(part);
            Console.WriteLine($"blinky:       conn -> '{other?.Id}' stage={other?.Stage}");
        }
    }
}
