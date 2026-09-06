using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using KSA;
using MeowSci.KsaAbstractions;
using WeldEasingType = MeowSci.KsaAbstractions.EasingType;

namespace MeowSci.GarrysTorchLib;

public sealed class GarrysTorchSubmod : ISubmod
{
    public string Name => "Garry's Torch - Vehicle Welding";
    public string Tooltip => "Welds vehicle parts together with adjustable position, rotation, and XYZ scale.";

    public static GarrysTorchSubmod? Instance { get; private set; }

    private readonly List<WeldEntry> _welds = new();
    public IReadOnlyList<WeldEntry> Welds => _welds;
    private readonly PresetManager _presetManager = new();
    private readonly WeldAnimationManager _animationManager = new();

    public WeldAnimationManager AnimationManager => _animationManager;

    // Create weld form state
    private int _pendingSourceIndex = -1;
    private int _pendingTargetIndex = -1;
    private int _selectedPresetIndex = -1;
    private float3 _pendingPosition = new float3(0f, 0f, 0f);
    private float3 _pendingRotation = new float3(0f, 0f, 0f);
    private float3 _pendingScale = WeldScale.Identity;
    private bool _pendingLockRotation = true;
    private string? _weldError;

    // Combo filters
    private readonly ImInputString _sourceFilter = new(128);
    private readonly ImInputString _targetFilter = new(128);
    private readonly ImInputString _presetFilter = new(128);
    private readonly ImInputString _targetPartFilter = new(128);

    // Target part selection (create form)
    private readonly List<Part> _targetParts = new();
    private int _targetPartIndex = -1;
    private int _prevTargetIndex = -1;

    // Deferred modal open flags (popups must be opened at matching ID scope)
    private bool _openDeleteModal;
    private bool _openSaveModal;

    // Delete preset modal state
    private string? _deleteConfirmName;

    // Save preset modal state
    private readonly ImInputString _savePresetName = new ImInputString(128);
    private string? _savePresetError;
    private WeldPreset _pendingSavePreset;

    public void Initialize()
    {
        Instance = this;
        _presetManager.Initialize();
    }

    public void Update(double dt)
    {
    }

    /// <summary>
    /// Called only by GarrysTorchPatches in PrepareFrame, after completed results are applied
    /// and before cloth/vehicle/orbit workers start. Retains player-time weld animation pacing.
    /// </summary>
    internal void UpdateBeforeVehicleSolvers(double dt, UniverseTime stateTime)
    {
        // Applying results may have destroyed a source or target. Cancel its animations before
        // they can write scale through disposed parts; surviving sources still restore scale.
        for (int i = _welds.Count - 1; i >= 0; i--)
            if (_welds[i].Source.IsDisposed || _welds[i].Target.IsDisposed)
                RemoveWeld(_welds[i]);

        _animationManager.Update(dt);

        if (_welds.Count == 0) return;

        var toRemove = new List<WeldEntry>();
        foreach (var weld in _welds)
            if (!WeldEngine.UpdateWeld(weld, stateTime)) toRemove.Add(weld);
        foreach (var weld in toRemove)
            RemoveWeld(weld);
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##gt_content");

        RenderCreateSection();

        if (_welds.Count > 0)
        {
            ImGui.Spacing();
            ImGui.SeparatorText($"Active Welds ( {_welds.Count} )");

            WeldEntry? toRemove = null;
            for (int i = 0; i < _welds.Count; i++)
                RenderWeldSection(_welds[i], i, ref toRemove);
            if (toRemove != null)
                RemoveWeld(toRemove);
        }

        // Deferred popup opens at content area scope
        if (_openDeleteModal)
        {
            ImGui.OpenPopup("Delete preset##gt");
            _openDeleteModal = false;
        }
        if (_openSaveModal)
        {
            ImGui.OpenPopup("Save as preset##gt");
            _openSaveModal = false;
        }
        RenderDeletePresetModal();
        RenderSavePresetModal();

        SubmodUI.EndContentArea();
    }

    public void Dispose()
    {
        _animationManager.Clear();
        foreach (var weld in _welds)
            if (!weld.Source.IsDisposed)
                WeldEngine.ApplyVehicleScale(weld.Source, WeldScale.Identity);
        _welds.Clear();
        Instance = null;
    }

    // ---- Create Section ----

    private void RenderCreateSection()
    {
        bool headerOpen = ImGui.CollapsingHeader("Create Weld (?)", ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.SetItemTooltip("Weld two vehicles together.\nThe source vehicle is positioned relative to\nthe target at the specified offset, rotation, and scale.");
        if (!headerOpen)
            return;

        var vehicles = VehicleProvider.GetAllVehicles();
        if (vehicles.Count == 0)
        {
            ImGui.Text("No vehicles available.");
            return;
        }

        var vehicleIds = new string[vehicles.Count];
        for (int i = 0; i < vehicles.Count; i++)
            vehicleIds[i] = vehicles[i].Id;

        if (_pendingSourceIndex >= vehicles.Count) _pendingSourceIndex = -1;
        if (_pendingTargetIndex >= vehicles.Count) _pendingTargetIndex = -1;

        // Rebuild target parts list whenever the selected target vehicle changes
        if (_pendingTargetIndex != _prevTargetIndex)
        {
            _prevTargetIndex = _pendingTargetIndex;
            _targetParts.Clear();
            _targetPartIndex = -1;
            if (_pendingTargetIndex >= 0 && _pendingTargetIndex < vehicles.Count)
            {
                foreach (var p in vehicles[_pendingTargetIndex].Parts.Parts)
                    _targetParts.Add(p);
            }
        }
        if (_targetPartIndex >= _targetParts.Count) _targetPartIndex = -1;

        var targetPartLabels = new string[_targetParts.Count];
        for (int i = 0; i < _targetParts.Count; i++)
            targetPartLabels[i] = $"{_targetParts[i].Template.Id}  [{_targetParts[i].Id}]";

        var presetNames = _presetManager.GetPresetNames();

        // Source / Target / Target Part / Preset table
        var style = ImGui.GetStyle();
        float labelW = ImGui.CalcTextSize("Target Part").X + style.ItemSpacing.X;
        float delW = ImGui.CalcTextSize(" del ").X + style.FramePadding.X * 2f;

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        var formFlags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX;
        if (ImGui.BeginTable("##gt_form", 3, formFlags))
        {
            ImGui.TableSetupColumn("##gt_lbl", ImGuiTableColumnFlags.WidthFixed, labelW);
            ImGui.TableSetupColumn("##gt_widget", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##gt_btns", ImGuiTableColumnFlags.WidthFixed, delW);

            // Source
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Source");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
            RenderFilteredCombo("##gt_src", vehicleIds, ref _pendingSourceIndex, _sourceFilter);
            ImGui.TableNextColumn();

            // Target
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Target");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
            RenderFilteredCombo("##gt_tgt", vehicleIds, ref _pendingTargetIndex, _targetFilter);
            ImGui.TableNextColumn();

            // Target Part
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Target Part");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
            bool noTarget = _pendingTargetIndex < 0 || _targetParts.Count == 0;
            if (noTarget) ImGui.BeginDisabled();
            RenderFilteredCombo("##gt_tpart", targetPartLabels, ref _targetPartIndex, _targetPartFilter);
            if (noTarget) ImGui.EndDisabled();
            ImGui.TableNextColumn();

            // Preset
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Preset");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
            RenderPresetCombo(presetNames);
            ImGui.TableNextColumn();
            bool hasPresetSelection = _selectedPresetIndex >= 0 && _selectedPresetIndex < presetNames.Length;
            if (!hasPresetSelection) ImGui.BeginDisabled();
            if (ImGui.Button(" del ##gt_del"))
            {
                _deleteConfirmName = presetNames[_selectedPresetIndex];
                _openDeleteModal = true;
            }
            if (!hasPresetSelection) ImGui.EndDisabled();

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding

        // Position / Rotation / Scale + Lock Rotation
        RenderDataFields("##gt_create", ref _pendingPosition, ref _pendingRotation,
            ref _pendingScale, ref _pendingLockRotation);

        // Create button
        ImGui.Spacing();
        bool canCreate = _pendingSourceIndex >= 0 && _pendingTargetIndex >= 0
            && _pendingSourceIndex != _pendingTargetIndex
            && _targetPartIndex >= 0;
        if (!canCreate) ImGui.BeginDisabled();
        if (ImGui.Button(" Create Weld ##gt_addweld"))
        {
            InitiateWeld(vehicles[_pendingSourceIndex], vehicles[_pendingTargetIndex],
                _targetParts[_targetPartIndex],
                _pendingPosition, _pendingRotation, _pendingScale, _pendingLockRotation);
        }
        if (!canCreate) ImGui.EndDisabled();

        // Validation / error messages
        if (_pendingSourceIndex >= 0 && _pendingTargetIndex >= 0
            && _pendingSourceIndex == _pendingTargetIndex)
        {
            ImGui.Spacing();
            ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), "Source and target must differ.");
        }
        if (_pendingTargetIndex >= 0 && _targetParts.Count > 0 && _targetPartIndex < 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), "Select a target part to anchor the weld.");
        }
        if (!string.IsNullOrEmpty(_weldError))
        {
            ImGui.Spacing();
            ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), _weldError);
        }
    }

    // ---- Weld Section ----

    private void RenderWeldSection(WeldEntry weld, int index, ref WeldEntry? toRemove)
    {
        string partSuffix = weld.TargetPart != null ? $"/{weld.TargetPart.Id}" : "";
        if (!ImGui.CollapsingHeader($"Weld: {weld.Source.Id} -> {weld.Target.Id}{partSuffix}##gt_weld_{index}",
            ImGuiTreeNodeFlags.DefaultOpen))
            return;

        // Bordered child window flush under the header
        var wpadX = ImGui.GetStyle().WindowPadding.X;
        float childW = ImGui.GetContentRegionAvail().X + wpadX * 2;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() - wpadX);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(20f, 10f));
        ImGui.BeginChild($"gt_child_{index}", new float2(childW, 0),
            ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysUseWindowPadding,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleVar();

        string anchorDesc = weld.TargetPart != null
            ? $"{weld.Source.Id}  →  {weld.Target.Id} / {weld.TargetPart.Id}"
            : $"{weld.Source.Id}  →  {weld.Target.Id}  (vehicle body frame)";
        ImGui.Text(anchorDesc);

        float3 prevScale = weld.Scale;
        RenderDataFields($"##gt_w{index}", ref weld.Position, ref weld.Rotation,
            ref weld.Scale, ref weld.LockRotation);
        if (!WeldScale.Equals(weld.Scale, prevScale))
            WeldEngine.ApplyVehicleScale(weld.Source, weld.Scale);

        ImGui.Spacing();
        ImGui.Checkbox($"Weld Enabled##gt_w{index}_enabled", ref weld.WeldEnabled);

        ImGui.Spacing();
        if (ImGui.Button($" Save settings as preset... ##gt_save_{index}"))
        {
            _pendingSavePreset = new WeldPreset
            {
                Position = weld.Position,
                Rotation = weld.Rotation,
                Scale = weld.Scale,
                LockRotation = weld.LockRotation,
            };
            _savePresetName.Clear();
            _savePresetError = null;
            _openSaveModal = true;
        }
        ImGui.SameLine(0, 8);
        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(KSAColor.Xkcd.Scarlet));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(KSAColor.Xkcd.PaleGrey));
        if (ImGui.Button($" Unweld ##gt_unweld_{index}"))
            toRemove = weld;
        ImGui.PopStyleColor();
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.EndChild();
    }

    // ---- Shared Data Fields ----

    private void RenderDataFields(string idPrefix, ref float3 position, ref float3 rotation,
        ref float3 scale, ref bool lockRotation)
    {
        ImGui.Text("Position (x, y, z) in meters");
        ImGui.SetNextItemWidth(-1f);
        ImGui.DragFloat3($"{idPrefix}_pos", ref position, 0.001f, 0f, 0f);

        ImGui.Spacing();
        ImGui.Text("Rotation (pitch, yaw, roll) in degrees");
        ImGui.SetNextItemWidth(-1f);
        ImGui.DragFloat3($"{idPrefix}_rot", ref rotation, 0.025f, -180f, 180f);

        ImGui.Spacing();
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        var flags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        if (ImGui.BeginTable($"{idPrefix}_scaletbl", 2, flags))
        {
            ImGui.TableSetupColumn("##s_lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##s_val", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Scale XYZ");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
            ImGui.DragFloat3($"{idPrefix}_scaleval", ref scale, 0.001f,
                WeldScale.Minimum, WeldScale.Maximum);

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding
        ImGui.Spacing();
        ImGui.Checkbox($"Lock Rotation{idPrefix}_lockrot", ref lockRotation);
    }

    // ---- Filterable Combos ----

    private void RenderFilteredCombo(string id, string[] items, ref int selectedIndex,
        ImInputString filter)
    {
        string preview = selectedIndex >= 0 && selectedIndex < items.Length
            ? items[selectedIndex] : "Select...";

        if (!ImGui.BeginCombo(id, preview))
            return;

        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
            filter.Clear();
        }
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint($"{id}_filter", "filter..."u8, filter);
        string filterText = filter.ToString().Trim();

        for (int i = 0; i < items.Length; i++)
        {
            if (filterText.Length > 0 && !items[i].Contains(filterText, StringComparison.OrdinalIgnoreCase)) continue;
            bool sel = selectedIndex == i;
            ImGui.PushID(i);
            if (ImGui.Selectable(items[i], sel))
                selectedIndex = i;
            ImGui.PopID();
            if (sel) ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    private void RenderPresetCombo(string[] presetNames)
    {
        string preview = _selectedPresetIndex >= 0 && _selectedPresetIndex < presetNames.Length
            ? presetNames[_selectedPresetIndex] : "Select...";

        if (!ImGui.BeginCombo("##gt_preset", preview))
            return;

        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
            _presetFilter.Clear();
        }
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##gt_preset_filter", "filter..."u8, _presetFilter);
        string filterText = _presetFilter.ToString().Trim();

        for (int i = 0; i < presetNames.Length; i++)
        {
            if (filterText.Length > 0 && !presetNames[i].Contains(filterText, StringComparison.OrdinalIgnoreCase)) continue;
            bool sel = _selectedPresetIndex == i;
            if (ImGui.Selectable(presetNames[i], sel))
            {
                _selectedPresetIndex = i;
                var preset = _presetManager.GetPreset(presetNames[i]);
                if (preset != null)
                {
                    _pendingPosition = preset.Value.Position;
                    _pendingRotation = preset.Value.Rotation;
                    _pendingScale = preset.Value.Scale;
                    _pendingLockRotation = preset.Value.LockRotation;
                }
            }
            if (sel) ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    // ---- Modals ----

    private void RenderDeletePresetModal()
    {
        bool open = true;
        if (!ImGui.BeginPopupModal("Delete preset##gt", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.Text($"Are you sure you want to delete\npreset '{_deleteConfirmName ?? string.Empty}'?");
        ImGui.Spacing();
        if (ImGui.Button(" You bet ##gt_delyes"))
        {
            if (_deleteConfirmName != null)
                _presetManager.DeletePreset(_deleteConfirmName);
            _selectedPresetIndex = -1;
            _deleteConfirmName = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine(0, 8);
        if (ImGui.Button(" Cancel ##gt_delno"))
        {
            _deleteConfirmName = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void RenderSavePresetModal()
    {
        bool open = true;
        if (!ImGui.BeginPopupModal("Save as preset##gt", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.InputText("##gt_savename", _savePresetName);
        ImGui.Spacing();
        if (ImGui.Button(" Save ##gt_savebtn"))
        {
            var name = _savePresetName.ToString().Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                _savePresetError = "Name is required";
            }
            else if (_presetManager.PresetExists(name))
            {
                _savePresetError = "A preset with this name already exists";
            }
            else
            {
                _presetManager.SavePreset(name, _pendingSavePreset);
                _savePresetError = null;
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine(0, 8);
        if (ImGui.Button(" Cancel ##gt_savecancel"))
        {
            _savePresetError = null;
            ImGui.CloseCurrentPopup();
        }
        if (!string.IsNullOrEmpty(_savePresetError))
        {
            ImGui.Spacing();
            ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), _savePresetError);
        }
        ImGui.EndPopup();
    }

    // ---- Weld Logic (Public API) ----

    /// <summary>Creates a weld between two vehicles by their IDs.</summary>
    public (WeldEntry? Weld, string? Error) CreateWeld(
        string sourceVehicleId, string targetVehicleId,
        float3 position, float3 rotation, float3 scale, bool lockRotation,
        Part? targetPart = null)
    {
        if (!WeldScale.IsValid(scale))
            return (null, $"Scale axes must each be between {WeldScale.Minimum} and {WeldScale.Maximum}.");

        if (sourceVehicleId == targetVehicleId)
            return (null, "Source and target must be different vehicles.");

        var vehicles = VehicleProvider.GetAllVehicles();
        var source = vehicles.FirstOrDefault(v => v.Id == sourceVehicleId);
        if (source == null)
            return (null, $"Source vehicle '{sourceVehicleId}' not found.");

        var target = vehicles.FirstOrDefault(v => v.Id == targetVehicleId);
        if (target == null)
            return (null, $"Target vehicle '{targetVehicleId}' not found.");

        foreach (var weld in _welds)
        {
            if (weld.Source == source)
                return (null, $"Vehicle {source.Id} is already welded as a source.");
        }

        var entry = new WeldEntry
        {
            Source = source,
            Target = target,
            TargetPart = targetPart,
            Position = position,
            Rotation = rotation,
            Scale = scale,
            LockRotation = lockRotation,
        };
        _welds.Add(entry);

        if (!WeldScale.Equals(scale, WeldScale.Identity))
            WeldEngine.ApplyVehicleScale(source, scale);

        SortWelds();
        Console.WriteLine($"garrys-torch: Welded {source.Id} to {target.Id}");
        return (entry, null);
    }

    /// <summary>Backwards-compatible overload for callers that create a uniformly scaled weld.</summary>
    public (WeldEntry? Weld, string? Error) CreateWeld(
        string sourceVehicleId, string targetVehicleId,
        float3 position, float3 rotation, float scale, bool lockRotation,
        Part? targetPart = null) =>
        CreateWeld(sourceVehicleId, targetVehicleId, position, rotation,
            WeldScale.Uniform(scale), lockRotation, targetPart);

    /// <summary>Finds a weld by its source vehicle ID.</summary>
    public WeldEntry? FindWeld(string sourceVehicleId)
    {
        for (int i = 0; i < _welds.Count; i++)
            if (_welds[i].Source.Id == sourceVehicleId)
                return _welds[i];
        return null;
    }

    /// <summary>Modifies an existing weld. Only non-null fields are updated.</summary>
    public (WeldEntry? Weld, string? Error) ModifyWeld(
        string sourceVehicleId, float3? position, float3? rotation, float3? scale, bool? lockRotation)
    {
        var weld = FindWeld(sourceVehicleId);
        if (weld == null)
            return (null, $"No weld found with source vehicle '{sourceVehicleId}'.");

        if (scale.HasValue && !WeldScale.IsValid(scale.Value))
            return (null, $"Scale axes must each be between {WeldScale.Minimum} and {WeldScale.Maximum}.");

        if (position.HasValue) weld.Position = position.Value;
        if (rotation.HasValue) weld.Rotation = rotation.Value;
        if (lockRotation.HasValue) weld.LockRotation = lockRotation.Value;

        if (scale.HasValue && !WeldScale.Equals(scale.Value, weld.Scale))
        {
            weld.Scale = scale.Value;
            WeldEngine.ApplyVehicleScale(weld.Source, weld.Scale);
        }

        return (weld, null);
    }

    /// <summary>Backwards-compatible overload for uniformly scaled partial updates.</summary>
    public (WeldEntry? Weld, string? Error) ModifyWeld(
        string sourceVehicleId, float3? position, float3? rotation, float? scale, bool? lockRotation) =>
        ModifyWeld(sourceVehicleId, position, rotation,
            scale.HasValue ? WeldScale.Uniform(scale.Value) : null, lockRotation);

    /// <summary>Removes a weld by its source vehicle ID.</summary>
    public bool RemoveWeld(string sourceVehicleId)
    {
        var weld = FindWeld(sourceVehicleId);
        if (weld == null) return false;
        RemoveWeld(weld);
        return true;
    }

    // ---- Preset API ----

    public string[] GetPresetNames() => _presetManager.GetPresetNames();
    public WeldPreset? GetPreset(string name) => _presetManager.GetPreset(name);
    public bool PresetExists(string name) => _presetManager.PresetExists(name);
    public bool SavePreset(string name, WeldPreset preset) => _presetManager.SavePreset(name, preset);
    public bool DeletePreset(string name) => _presetManager.DeletePreset(name);

    /// <summary>Starts or queues an animated transition of a weld's position, rotation, and scale.</summary>
    public string? AnimateWeld(
        string sourceVehicleId,
        float3 targetPosition, float3 targetRotation, float3 targetScale,
        double durationSeconds, WeldEasingType easing,
        double easingPowerStart = 3.0, double easingPowerEnd = 3.0)
    {
        var weld = FindWeld(sourceVehicleId);
        if (weld == null)
            return $"No active weld found with source: {sourceVehicleId}";

        if (durationSeconds <= 0)
            return "Duration must be greater than 0";

        if (!WeldScale.IsValid(targetScale))
            return $"Scale axes must each be between {WeldScale.Minimum} and {WeldScale.Maximum}.";

        var animation = new WeldAnimation(
            weld.Position, weld.Rotation, weld.Scale,
            targetPosition, targetRotation, targetScale,
            durationSeconds, easing, easingPowerStart, easingPowerEnd);

        _animationManager.Enqueue(weld, animation);
        return null;
    }

    /// <summary>Backwards-compatible overload for animations targeting a uniform scale.</summary>
    public string? AnimateWeld(
        string sourceVehicleId,
        float3 targetPosition, float3 targetRotation, float targetScale,
        double durationSeconds, WeldEasingType easing,
        double easingPowerStart = 3.0, double easingPowerEnd = 3.0) =>
        AnimateWeld(sourceVehicleId, targetPosition, targetRotation,
            WeldScale.Uniform(targetScale), durationSeconds, easing,
            easingPowerStart, easingPowerEnd);

    // ---- Weld Logic (Internal) ----

    private void InitiateWeld(Vehicle source, Vehicle target, Part targetPart, float3 position, float3 rotation,
        float3 scale, bool lockRotation)
    {
        var (_, error) = CreateWeld(source.Id, target.Id, position, rotation, scale, lockRotation, targetPart);
        if (error != null)
        {
            _weldError = error;
            return;
        }

        _weldError = null;
        _pendingPosition = new float3(0f, 0f, 0f);
        _pendingRotation = new float3(0f, 0f, 0f);
        _pendingScale = WeldScale.Identity;
        _pendingLockRotation = true;
    }

    private void RemoveWeld(WeldEntry entry)
    {
        _animationManager.CancelAll(entry);
        if (!entry.Source.IsDisposed)
            WeldEngine.ApplyVehicleScale(entry.Source, WeldScale.Identity);
        Console.WriteLine($"garrys-torch: Unwelded {entry.Source.Id} from {entry.Target.Id}");
        _welds.Remove(entry);
    }

    private void SortWelds()
    {
        var sorted = WeldEngine.TopologicalSort(_welds);
        _welds.Clear();
        foreach (var w in sorted)
            _welds.Add(w);
    }
}
