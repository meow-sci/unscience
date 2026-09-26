using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using MeowSci.DohLib.Materials;
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
    public string Tooltip => "Programmatic kitten spawning with per-kitten material customization.";

    // Core systems
    private MaterialFactory? _materialFactory;
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
    private float3 _offset = new float3(0f, 0f, 10f);
    private int _spawnCount = 1;
    private bool _useCustomColor;
    private float4 _tintColor = new float4(1f, 1f, 1f, 1f);
    private bool _uniquePerKitten;

    // UI state — XKCD color picker
    private string _selectedXkcdName = "";
    private readonly ImInputString _xkcdFilterText = new(64);

    // UI state — feedback
    private string? _statusMessage;
    private bool _statusIsError;

    public void Initialize()
    {
        _materialFactory = new MaterialFactory();
        _registry = new SpawnedKittenRegistry();
        _spawner = new KittenSpawner(_materialFactory, _registry);

        _availableCharacters = _spawner.GetAvailableCharacters();
        Console.WriteLine("doh: DohSubmod initialized");
    }

    public void Update(double dt)
    {
        // Kittens the game removes itself (boarding, destruction) must not keep GPU material slots.
        _spawner?.PruneDisposed();
        // Loads reset and restore inside one native call, so a detached set still cached here was
        // left by a load with no DOH record; its kittens are gone.
        ReleaseStaleSavedMaterials();
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##doh_content");

        RenderStatus();
        RenderSpawnControls();
        ImGui.SeparatorText("Spawned Kittens");
        RenderKittenList();

        SubmodUI.EndContentArea();
    }

    public void Dispose()
    {
        _spawner?.DespawnAll();
        _savedMaterialCache.Clear();
        _materialFactory?.Cleanup();
        MaterialSystemAccessor.Cleanup();
        Console.WriteLine("doh: DohSubmod disposed");
    }

    // ---- Status ----

    private void RenderStatus()
    {
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            if (_statusIsError)
                ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), _statusMessage);
            else
                ImGui.TextColored(new float4(0.4f, 1f, 0.4f, 1f), _statusMessage);
            ImGui.Spacing();
        }

        if (!MaterialSystemAccessor.IsInitialized)
        {
            ImGui.TextColored(new float4(1f, 0.8f, 0.3f, 1f),
                "MaterialSystem not yet initialized (will init on first spawn).");
            ImGui.SameLine(0, 12);
            if (ImGui.SmallButton("Init Now"))
            {
                if (MaterialSystemAccessor.Initialize())
                    SetStatus("MaterialSystem initialized.", false);
                else
                    SetStatus($"Init failed: {MaterialSystemAccessor.LastError}", true);
            }
            ImGui.Spacing();
        }

        ImGui.TextDisabled($"Spawned: {_registry?.Count ?? 0} kittens  |  Materials: {_materialFactory?.CreatedSets.Count ?? 0}");
        RenderMaterialPoolStatus();
        ImGui.Spacing();
    }

    private static void RenderMaterialPoolStatus()
    {
        if (!MaterialSystemAccessor.IsInitialized) return;
        int capacity = MaterialSystemAccessor.GetCapacity();
        int free = MaterialSystemAccessor.GetFreeSlotCount();
        if (capacity < 0 || free < 0) return;

        int available = MaterialSystemAccessor.GetAvailableSlots();
        string text = $"GPU material slots: {capacity - free}/{capacity} used, room for ~" +
            $"{available / MaterialSystemAccessor.EstimatedSlotsPerSet} more tinted set(s)";
        if (available < MaterialSystemAccessor.EstimatedSlotsPerSet)
            ImGui.TextColored(new float4(1f, 0.8f, 0.3f, 1f), text);
        else
            ImGui.TextDisabled(text);
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

            // Custom color toggle
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Custom Color");
            ImGui.TableNextColumn();
            ImGui.Checkbox("##doh_usecolor", ref _useCustomColor);

            if (_useCustomColor)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Tint");
                ImGui.TableNextColumn();
                var prevColor = _tintColor;
                ImGui.ColorEdit4("##doh_tint", ref _tintColor,
                    ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar);
                if (_tintColor.X != prevColor.X || _tintColor.Y != prevColor.Y
                    || _tintColor.Z != prevColor.Z || _tintColor.W != prevColor.W)
                    _selectedXkcdName = "";

                // XKCD color combo
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("XKCD Color");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                RenderXkcdCombo();

                if (_spawnCount > 1)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Unique Each");
                    ImGui.TableNextColumn();
                    ImGui.Checkbox("##doh_unique", ref _uniquePerKitten);
                }
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding

        ImGui.Spacing();

        // Spawn button
        bool canSpawn = _selectedVehicle != null && _spawner != null;
        if (!canSpawn) ImGui.BeginDisabled();
        if (ImGui.Button(" Spawn Kitten(s) ##doh"))
            DoSpawn();
        ImGui.SameLine(0, 8);
        ImGui.PushStyleColor(ImGuiCol.Button, (float4)KSAColor.Xkcd.NeonPurple);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, (float4)KSAColor.Xkcd.BrightMagenta);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, (float4)KSAColor.Xkcd.HotPink);
        ImGui.PushStyleColor(ImGuiCol.Text, (float4)KSAColor.Xkcd.PaleYellow);
        if (ImGui.Button(" I'm Feeling Lucky ##doh"))
            DoFeelingLucky();
        ImGui.PopStyleColor(4);
        if (!canSpawn) ImGui.EndDisabled();

        ImGui.SameLine(0, 12);
        if (ImGui.Button(" Refresh Characters ##doh"))
        {
            _availableCharacters = _spawner?.GetAvailableCharacters() ?? Array.Empty<string>();
            SetStatus($"Found {_availableCharacters.Length} characters.", false);
        }

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

    // ---- Kitten List ----

    private void RenderKittenList()
    {
        if (_registry == null || _registry.Count == 0)
        {
            ImGui.TextDisabled("No spawned kittens.");
            return;
        }

        var kittens = _registry.GetAll();

        for (int i = 0; i < kittens.Count; i++)
        {
            var entry = kittens[i];
            ImGui.PushID(i);
            RenderKittenItem(entry, i);
            ImGui.PopID();
        }

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(KSAColor.Xkcd.Scarlet));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(KSAColor.Xkcd.PaleGrey));
        if (ImGui.Button(" Despawn All ##doh"))
        {
            _spawner?.DespawnAll();
            SetStatus("All kittens despawned.", false);
        }
        ImGui.PopStyleColor(2);
    }

    private void RenderKittenItem(SpawnedKittenEntry entry, int index)
    {
        string header = entry.MaterialSet != null
            ? $"{entry.KittenId}  ({entry.CharacterId})##kit_{index}"
            : $"{entry.KittenId}  ({entry.CharacterId})  [no materials]##kit_{index}";

        if (!ImGui.CollapsingHeader(header))
            return;

        // Bordered child container (matches garrys-torch pattern)
        var wpadX = ImGui.GetStyle().WindowPadding.X;
        float childW = ImGui.GetContentRegionAvail().X + wpadX * 2;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() - wpadX);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(20f, 10f));
        ImGui.BeginChild($"doh_kit_{index}", new float2(childW, 0),
            ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysUseWindowPadding,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleVar();

        RenderMaterialDetails(entry);

        ImGui.Spacing();
        ImGui.EndChild();
    }

    private void RenderMaterialDetails(SpawnedKittenEntry entry)
    {
        var matSet = entry.MaterialSet;

        // Action buttons row
        if (matSet != null)
        {
            var allColor = matSet.TintColor;
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Tint All:");
            ImGui.SameLine(0, 8);
            if (ImGui.ColorEdit4("##all_mats", ref allColor,
                ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
            {
                matSet.UpdateTint(allColor);
            }
            ImGui.SameLine(0, 8);
            if (ImGui.SmallButton(" Reset All ##reset"))
            {
                matSet.UpdateTint(matSet.TintColor);
            }
        }

        ImGui.SameLine(0, 8);
        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(KSAColor.Xkcd.Scarlet));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(KSAColor.Xkcd.PaleGrey));
        if (ImGui.SmallButton(" Despawn ##despawn"))
        {
            _spawner?.Despawn(entry.KittenId);
            SetStatus($"Despawned '{entry.KittenId}'.", false);
        }
        ImGui.PopStyleColor(2);

        // Per-material table
        if (matSet == null || matSet.Materials.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("No per-material data available.");
            return;
        }

        ImGui.Spacing();
        ImGui.SeparatorText("Individual Materials");

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 4f));
        var tableFlags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX
                       | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH;
        if (ImGui.BeginTable("##mat_detail", 3, tableFlags))
        {
            ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 100f);
            ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed, 40f);
            ImGui.TableHeadersRow();

            for (int m = 0; m < matSet.Materials.Count; m++)
            {
                var mat = matSet.Materials[m];
                ImGui.PushID(m);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(mat.Name);

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled(mat.Source);

                ImGui.TableNextColumn();
                var matColor = mat.Color;
                if (ImGui.ColorEdit4("##mc", ref matColor,
                    ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                {
                    mat.Color = matColor;
                    mat.ApplyColor();
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding
    }

    // ---- Actions ----

    private void DoSpawn()
    {
        var target = GetSpawnTarget();
        if (_spawner == null || target == null) return;

        string? characterId = _selectedCharacterIndex >= 0 && _selectedCharacterIndex < _availableCharacters.Length
            ? _availableCharacters[_selectedCharacterIndex]
            : null;

        var request = new SpawnRequest
        {
            ReferenceVehicle = target,
            ReferenceVehicleId = target.Id,
            OffsetBodyFrame = new double3(_offset.X, _offset.Y, _offset.Z),
            Count = _spawnCount,
            CharacterId = characterId,
            TintColor = _useCustomColor ? _tintColor : null,
            UniqueMaterialsPerKitten = _uniquePerKitten,
        };

        ReportSpawnResult(_spawner.Spawn(request), n => $"Spawned {n} kitten(s) at '{target.Id}'.");
    }

    private void DoFeelingLucky()
    {
        var target = GetSpawnTarget();
        if (_spawner == null || target == null) return;

        string? characterId = _selectedCharacterIndex >= 0 && _selectedCharacterIndex < _availableCharacters.Length
            ? _availableCharacters[_selectedCharacterIndex]
            : null;

        var colors = XkcdColorHelper.GetAll();
        var perKittenColors = PickRandomUniqueColors(colors, _spawnCount);

        var request = new SpawnRequest
        {
            ReferenceVehicle = target,
            ReferenceVehicleId = target.Id,
            OffsetBodyFrame = new double3(_offset.X, _offset.Y, _offset.Z),
            Count = _spawnCount,
            CharacterId = characterId,
            TintColor = perKittenColors[0],
            PerKittenColors = perKittenColors,
            UniqueMaterialsPerKitten = true,
        };

        ReportSpawnResult(_spawner.Spawn(request), n => $"Feeling lucky! Spawned {n} rainbow kitten(s) at '{target.Id}'.");
    }

    /// <summary>The selected vehicle if it still exists; otherwise clears it and reports why.</summary>
    private Vehicle? GetSpawnTarget()
    {
        if (_selectedVehicle == null || _selectedVehicle.IsDisposed)
        {
            _selectedVehicle = null;
            SetStatus("No vehicle selected (the previous selection no longer exists).", true);
            return null;
        }

        return _selectedVehicle;
    }

    private void ReportSpawnResult(SpawnResult result, Func<int, string> successMessage)
    {
        if (!result.Success)
            SetStatus(result.Error ?? "Spawn failed.", true);
        else if (result.Warning != null)
            SetStatus(successMessage(result.Count) + " " + result.Warning, true);
        else
            SetStatus(successMessage(result.Count), false);
    }

    private static float4[] PickRandomUniqueColors((string Name, float4 Color)[] palette, int count)
    {
        if (count >= palette.Length)
        {
            // More kittens than colors — shuffle the whole palette and take what we need
            var shuffled = palette.OrderBy(_ => Random.Shared.Next()).ToArray();
            var result = new float4[count];
            for (int i = 0; i < count; i++)
                result[i] = shuffled[i % shuffled.Length].Color;
            return result;
        }

        // Fisher-Yates partial shuffle to pick `count` unique indices
        var indices = Enumerable.Range(0, palette.Length).ToArray();
        for (int i = 0; i < count; i++)
        {
            int j = Random.Shared.Next(i, indices.Length);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        var colors = new float4[count];
        for (int i = 0; i < count; i++)
            colors[i] = palette[indices[i]].Color;
        return colors;
    }

    // ---- XKCD Color Combo ----

    private void RenderXkcdCombo()
    {
        string previewText = _selectedXkcdName.Length > 0 ? _selectedXkcdName : "Pick XKCD color...";
        if (ImGui.BeginCombo("##xkcd_combo", previewText))
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##xkcd_filter", "filter..."u8, _xkcdFilterText);

            string filterStr = _xkcdFilterText.ToString();
            var colors = XkcdColorHelper.GetAll();
            foreach (var (name, color) in colors)
            {
                if (filterStr.Length > 0
                    && !name.Contains(filterStr, StringComparison.OrdinalIgnoreCase))
                    continue;

                ImGui.ColorButton($"##swatch_{name}", color,
                    ImGuiColorEditFlags.NoTooltip, new float2(14, 14));
                ImGui.SameLine();

                if (ImGui.Selectable(name, name == _selectedXkcdName))
                {
                    _selectedXkcdName = name;
                    _tintColor = color;
                    _useCustomColor = true;
                }
            }
            ImGui.EndCombo();
        }
    }

    private void SetStatus(string message, bool isError)
    {
        _statusMessage = message;
        _statusIsError = isError;
    }
}
