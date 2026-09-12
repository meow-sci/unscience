using System;
using System.Collections.Generic;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.ThugLifeLib;

/// <summary>
/// ImGui surface for the thug-life mod: create new anchored sunglasses entries on a
/// selected vehicle/part/subpart and tune position, rotation, and size per entry.
/// </summary>
public sealed partial class ThugLifeSubmod : ISubmod, MeowSci.KsaAbstractions.Persistence.ISaveParticipantSource
{
    public string Name => "Thug Life - Sunglasses Anchor";
    public string Tooltip => "Apply the thug-life sunglasses meme as a 2D quad anchored to any part/subpart on a vehicle.";

    public static ThugLifeSubmod? Instance { get; private set; }

    private ThugLifeRenderManager? _manager;

    // Create-form state
    private int _pendingVehicleIndex = -1;
    private int _pendingPartIndex = -1;
    private int _pendingSubPartIndex = -1;
    private int _prevVehicleIndex = -2;
    private int _prevPartIndex = -2;
    private float3 _pendingPosition = new(0f, 0f, 0f);
    private float3 _pendingRotation = new(0f, 0f, 0f);
    private float _pendingWidth = 0.975f;
    private float _pendingHeight = 0.1875f;
    private string? _createError;

    // Cached lists for the selected target
    private readonly List<Part> _topLevelParts = new();
    private readonly List<Part> _subParts = new();

    // Combo filters
    private readonly ImInputString _vehicleFilter = new(128);
    private readonly ImInputString _partFilter = new(128);
    private readonly ImInputString _subPartFilter = new(128);

    public void Initialize()
    {
        Instance = this;
        _manager = new ThugLifeRenderManager();
    }

    public void Update(double dt)
    {
        _manager?.Update(dt);
    }

    public void Dispose()
    {
        _manager?.Dispose();
        _manager = null;
        Instance = null;
    }

    public ThugLifeRenderManager? Manager => _manager;

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##tl_content");

        // The GPU resources come up lazily on the first entry (see ThugLifeRenderManager),
        // so "not ready" here means a real fault, not "not built yet".
        if (_manager == null || !_manager.IsReady)
        {
            ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f),
                _manager?.LastError ?? "thug-life renderer not initialized.");
            SubmodUI.EndContentArea();
            return;
        }

        RenderCreateSection();

        var entries = _manager.Entries;
        if (entries.Count > 0)
        {
            ImGui.Spacing();
            ImGui.SeparatorText($"Active Sunglasses ( {entries.Count} )");

            ThugLifeEntry? toRemove = null;
            for (int i = 0; i < entries.Count; i++)
                RenderEntrySection(entries[i], i, ref toRemove);
            if (toRemove != null)
                _manager.Remove(toRemove);
        }

        SubmodUI.EndContentArea();
    }

    // ---- Create Section ----

    private void RenderCreateSection()
    {
        bool open = ImGui.CollapsingHeader("Anchor New Sunglasses (?)", ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.SetItemTooltip("Pick a vehicle, then a part, then optionally a subpart to anchor to.\nThe quad is positioned in that part's local frame using\nthe offset and rotation below.");
        if (!open) return;

        var vehicles = VehicleProvider.GetAllVehicles();
        if (vehicles.Count == 0)
        {
            ImGui.Text("No vehicles available.");
            return;
        }

        var vehicleIds = new string[vehicles.Count];
        for (int i = 0; i < vehicles.Count; i++) vehicleIds[i] = vehicles[i].Id;
        if (_pendingVehicleIndex >= vehicles.Count) _pendingVehicleIndex = -1;

        if (_pendingVehicleIndex != _prevVehicleIndex)
        {
            _prevVehicleIndex = _pendingVehicleIndex;
            _topLevelParts.Clear();
            _pendingPartIndex = -1;
            _subParts.Clear();
            _pendingSubPartIndex = -1;
            if (_pendingVehicleIndex >= 0)
                foreach (var p in vehicles[_pendingVehicleIndex].Parts.Parts)
                    _topLevelParts.Add(p);
        }

        var partLabels = new string[_topLevelParts.Count];
        for (int i = 0; i < _topLevelParts.Count; i++)
            partLabels[i] = $"{_topLevelParts[i].Template.Id}  [{_topLevelParts[i].Id}]";
        if (_pendingPartIndex >= _topLevelParts.Count) _pendingPartIndex = -1;

        if (_pendingPartIndex != _prevPartIndex)
        {
            _prevPartIndex = _pendingPartIndex;
            _subParts.Clear();
            _pendingSubPartIndex = -1;
            if (_pendingPartIndex >= 0)
                foreach (var sp in _topLevelParts[_pendingPartIndex].SubParts)
                    _subParts.Add(sp);
        }

        // The subpart combo always offers "(use this part)" at index 0, then the actual subparts.
        var subPartLabels = new string[_subParts.Count + 1];
        subPartLabels[0] = "(use this part)";
        for (int i = 0; i < _subParts.Count; i++)
            subPartLabels[i + 1] = $"{_subParts[i].Template.Id}  [{_subParts[i].Id}]";
        if (_pendingSubPartIndex < 0) _pendingSubPartIndex = 0;
        if (_pendingSubPartIndex >= subPartLabels.Length) _pendingSubPartIndex = 0;

        var style = ImGui.GetStyle();
        float labelW = ImGui.CalcTextSize("SubPart").X + style.ItemSpacing.X + 8f;

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        var formFlags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX;
        if (ImGui.BeginTable("##tl_form", 2, formFlags))
        {
            ImGui.TableSetupColumn("##tl_lbl", ImGuiTableColumnFlags.WidthFixed, labelW);
            ImGui.TableSetupColumn("##tl_widget", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Vehicle");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
            RenderFilteredCombo("##tl_veh", vehicleIds, ref _pendingVehicleIndex, _vehicleFilter);

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Part");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
            bool noVehicle = _pendingVehicleIndex < 0 || _topLevelParts.Count == 0;
            if (noVehicle) ImGui.BeginDisabled();
            RenderFilteredCombo("##tl_part", partLabels, ref _pendingPartIndex, _partFilter);
            if (noVehicle) ImGui.EndDisabled();

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("SubPart");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
            bool noPart = _pendingPartIndex < 0;
            if (noPart) ImGui.BeginDisabled();
            RenderFilteredCombo("##tl_sp", subPartLabels, ref _pendingSubPartIndex, _subPartFilter);
            if (noPart) ImGui.EndDisabled();

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        RenderTransformFields("##tl_create",
            ref _pendingPosition, ref _pendingRotation,
            ref _pendingWidth, ref _pendingHeight);

        ImGui.Spacing();
        bool canCreate = _pendingVehicleIndex >= 0 && _pendingPartIndex >= 0;
        if (!canCreate) ImGui.BeginDisabled();
        if (ImGui.Button(" Add Sunglasses ##tl_add"))
        {
            CreateEntry(vehicles[_pendingVehicleIndex]);
        }
        if (!canCreate) ImGui.EndDisabled();

        // One-click preset. Only offered for a kitten, since KittenGlassesPreset's pose is
        // tuned to land on a cat's face and means nothing on a rocket.
        Vehicle? selected = _pendingVehicleIndex >= 0 ? vehicles[_pendingVehicleIndex] : null;
        if (KittenGlassesPreset.IsKitten(selected))
        {
            ImGui.SameLine();
            if (ImGui.Button(" animate thug ##tl_anim"))
                AnimateKittenGlasses(selected!);
            ImGui.SetItemTooltip(
                "Drop the tuned sunglasses onto this kitten's face.\n"
                + "No part selection needed - falls back to the kitten's root part.");
        }

        if (!string.IsNullOrEmpty(_createError))
        {
            ImGui.Spacing();
            ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), _createError);
        }
    }

    private void CreateEntry(Vehicle vehicle)
    {
        Part? anchor = ResolveSelectedAnchor();
        if (anchor == null)
        {
            _createError = "Select a part first.";
            return;
        }

        if (_manager == null) { _createError = "Renderer not ready."; return; }

        var entry = new ThugLifeEntry
        {
            Vehicle = vehicle,
            Part = anchor,
            Position = _pendingPosition,
            Rotation = _pendingRotation,
            Width = _pendingWidth,
            Height = _pendingHeight,
            Visible = true,
        };
        // The first Add is what brings the GPU pipeline up, so this is where a render
        // fault surfaces to the player.
        if (!_manager.Add(entry))
        {
            _createError = _manager.LastError ?? "thug-life renderer is unavailable.";
            return;
        }

        _createError = null;
        Console.WriteLine($"thug-life: anchored sunglasses to {vehicle.Id} / {anchor.Id}");

        // Reset offsets but keep dropdowns where they are so the user can iterate.
        _pendingPosition = new float3(0f, 0f, 0f);
        _pendingRotation = new float3(0f, 0f, 0f);
    }

    /// <summary>
    /// One-click kitten glasses: anchors the tuned preset and slides it onto the face.
    /// The create form's position/rotation/size fields are deliberately ignored — the whole
    /// point of the button is that it needs no tuning.
    /// </summary>
    private void AnimateKittenGlasses(Vehicle kitten)
    {
        if (_manager == null) { _createError = "Renderer not ready."; return; }

        Part? anchor = ResolveSelectedAnchor() ?? FirstTopLevelPart(kitten);
        if (anchor == null)
        {
            _createError = $"'{kitten.Id}' has no part to anchor to.";
            return;
        }

        var entry = new ThugLifeEntry
        {
            Vehicle = kitten,
            Part = anchor,
            Position = KittenGlassesPreset.StartPosition,
            Rotation = KittenGlassesPreset.Rotation,
            Width = KittenGlassesPreset.Width,
            Height = KittenGlassesPreset.Height,
            Visible = true,
            Slide = new ThugLifeSlide(
                KittenGlassesPreset.StartPosition,
                KittenGlassesPreset.EndPosition,
                KittenGlassesPreset.SlideSeconds),
        };

        if (!_manager.Add(entry))
        {
            _createError = _manager.LastError ?? "thug-life renderer is unavailable.";
            return;
        }

        _createError = null;
        Console.WriteLine($"thug-life: animating sunglasses onto kitten {kitten.Id} / {anchor.Id}");
    }

    /// <summary>The part (or subpart) currently picked in the create form, or null if none.</summary>
    private Part? ResolveSelectedAnchor()
    {
        if (_pendingPartIndex < 0 || _pendingPartIndex >= _topLevelParts.Count) return null;
        if (_pendingSubPartIndex > 0 && _pendingSubPartIndex - 1 < _subParts.Count)
            return _subParts[_pendingSubPartIndex - 1];
        return _topLevelParts[_pendingPartIndex];
    }

    /// <summary>A kitten on EVA is rooted at its MMU backpack part; that is the fallback anchor.</summary>
    private static Part? FirstTopLevelPart(Vehicle vehicle)
    {
        foreach (var part in vehicle.Parts.Parts)
            return part;
        return null;
    }

    // ---- Entry Section ----

    private void RenderEntrySection(ThugLifeEntry entry, int index, ref ThugLifeEntry? toRemove)
    {
        string label = $"Sunglasses: {entry.Vehicle.Id} / {entry.Part.Id}##tl_e{index}";
        if (!ImGui.CollapsingHeader(label, ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var wpadX = ImGui.GetStyle().WindowPadding.X;
        float childW = ImGui.GetContentRegionAvail().X + wpadX * 2;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() - wpadX);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(20f, 10f));
        ImGui.BeginChild($"tl_child_{index}", new float2(childW, 0),
            ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysUseWindowPadding,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleVar();

        ImGui.Text($"{entry.Vehicle.Id}  →  {entry.Part.Id}");

        RenderTransformFields($"##tl_e{index}",
            ref entry.Position, ref entry.Rotation,
            ref entry.Width, ref entry.Height);

        ImGui.Spacing();
        ImGui.Checkbox($"Visible##tl_e{index}_vis", ref entry.Visible);

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(KSAColor.Xkcd.Scarlet));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(KSAColor.Xkcd.PaleGrey));
        if (ImGui.Button($" Remove ##tl_e{index}_rm"))
            toRemove = entry;
        ImGui.PopStyleColor();
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.EndChild();
    }

    // ---- Shared Fields ----

    private static void RenderTransformFields(string idPrefix,
        ref float3 position, ref float3 rotation,
        ref float width, ref float height)
    {
        ImGui.Spacing();
        ImGui.Text("Position (x, y, z) in meters — anchor part local frame");
        ImGui.SetNextItemWidth(-1f);
        ImGui.DragFloat3($"{idPrefix}_pos", ref position, 0.001f, 0f, 0f);

        ImGui.Spacing();
        ImGui.Text("Rotation (pitch, yaw, roll) in degrees");
        ImGui.SetNextItemWidth(-1f);
        ImGui.DragFloat3($"{idPrefix}_rot", ref rotation, 0.25f, -180f, 180f);

        ImGui.Spacing();
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        var flags = ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX;
        if (ImGui.BeginTable($"{idPrefix}_size", 4, flags))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Width");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
            ImGui.DragFloat($"{idPrefix}_w", ref width, 0.001f, 0.001f, 50f);
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Height");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
            ImGui.DragFloat($"{idPrefix}_h", ref height, 0.001f, 0.001f, 50f);
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private static void RenderFilteredCombo(string id, string[] items, ref int selectedIndex,
        ImInputString filter)
    {
        string preview = selectedIndex >= 0 && selectedIndex < items.Length
            ? items[selectedIndex] : "Select...";

        if (!ImGui.BeginCombo(id, preview)) return;

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
}
