using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using MeowSci.DohLib.Spawning;
using MeowSci.WarPaintLib;

namespace MeowSci.DohLib;

public sealed partial class DohSubmod
{
    private void RenderKittenList()
    {
        var kittens = _registry?.GetAll() ?? Array.Empty<SpawnedKittenEntry>();
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##doh_kittens"u8, 3,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("Name"u8, ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Model"u8, ImGuiTableColumnFlags.WidthStretch);
            float smiteWidth = ImGui.CalcTextSize("Smite"u8).X + ImGui.GetStyle().FramePadding.X * 2f;
            ImGui.TableSetupColumn("##doh_actions"u8, ImGuiTableColumnFlags.WidthFixed, smiteWidth);
            ImGui.TableHeadersRow();
            foreach (var entry in kittens)
            {
                ImGui.PushID(entry.KittenId);
                RenderKittenItem(entry);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        if (kittens.Count == 0)
        {
            ImGui.TextDisabled("No spawned kittens."u8);
            return;
        }

        ImGui.Spacing();
        if (DangerButton(" Smite All ##doh"u8, new float2(-1, 0)))
        {
            int count = _spawner?.RequestSmiteAll() ?? 0;
            LogStatus($"Smite requested for {count} kitten(s) through KSA's native kill action.", count == 0);
        }
    }

    private void RenderKittenItem(SpawnedKittenEntry entry)
    {
        string name = entry.Vehicle?.Id ?? entry.KittenId;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(name);
        ImGui.SetItemTooltip(name);
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(entry.CharacterId);
        ImGui.SetItemTooltip(entry.CharacterId);
        ImGui.TableNextColumn();
        if (DangerButton("Smite##doh"u8, new float2(-1, 0)))
        {
            bool queued = _spawner?.RequestSmite(entry.KittenId) ?? false;
            LogStatus(queued ? $"Smite requested for '{name}'." : "That kitten is no longer available.", !queued);
        }
    }

    private static bool DangerButton(ImString label, float2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(KSAColor.Xkcd.Scarlet));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new float4(.95f, .25f, .2f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new float4(.7f, .1f, .08f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(KSAColor.Xkcd.PaleGrey));
        bool pressed = ImGui.Button(label, size);
        ImGui.PopStyleColor(4);
        return pressed;
    }

    private void RenderPaintPresetRow()
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("War Paint preset"u8);
        ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
        var presets = WarPaintService.Shared.Presets;
        string preview = _paintPresetName == null ? "(stock appearance)"
            : presets.Names.Contains(_paintPresetName) ? _paintPresetName : $"{_paintPresetName} (saved colors)";
        if (ImGui.BeginCombo("##doh_paint_preset"u8, preview))
        {
            if (ImGui.Selectable("(stock appearance)"u8, _paintPresetName == null))
                SelectPaintPreset(null);
            foreach (string name in presets.Names)
                if (ImGui.Selectable(name, name == _paintPresetName))
                    SelectPaintPreset(name);
            ImGui.EndCombo();
        }
    }

    private void SelectPaintPreset(string? name)
    {
        _paintPresetName = name;
        _spawnPaint = name != null && WarPaintService.Shared.Presets.TryGet(name, out var paint) ? paint.Clone() : null;
        _useCustomColor = _uniquePerKitten = false;
        _tintColor = float4.One;
    }

    // ---- Actions ----

    private void DoSpawn()
    {
        CancelPlacement();
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
            VehicleName = _spawnName.ToString(),
            CharacterId = characterId,
            PaintPresetName = _paintPresetName,
            PaintSettings = _spawnPaint,
        };

        ReportSpawnResult(_spawner.Spawn(request), n => $"Spawned {n} kitten(s) at '{target.Id}'.");
    }

}
