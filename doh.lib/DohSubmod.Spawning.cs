using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using MeowSci.DohLib.Spawning;
using MeowSci.WarPaintLib;

namespace MeowSci.DohLib;

public sealed partial class DohSubmod
{
    private void RenderKittenItem(SpawnedKittenEntry entry, int index)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"{entry.Vehicle?.Id ?? entry.KittenId} ({entry.CharacterId})");
        ImGui.SameLine();
        if (ImGui.SmallButton("Smite##doh"u8))
        {
            bool queued = _spawner?.RequestSmite(entry.KittenId) ?? false;
            SetStatus(queued ? $"Smite requested for '{entry.Vehicle?.Id ?? entry.KittenId}'." : "That kitten is no longer available.", !queued);
        }
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
        ImGui.TextDisabled("Create presets and edit live kittens in War Paint."u8);
        if (_paintPresetName != null)
            ImGui.TextDisabled("Selected colors are kept; reselect a preset to refresh."u8);
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
