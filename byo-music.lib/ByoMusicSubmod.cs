using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.ImGuiApi;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.ByoMusicLib;

public sealed class ByoMusicSubmod : ISubmod
{
    private readonly LibraryFileBrowser _browser = new(SoundLibrary.Files, "byo_music", "Import sound — OGG, WAV, MP3");
    private readonly ImInputString _soundFilter = new(128), _vesselFilter = new(128);
    private readonly List<VesselSound> _sounds = new();
    private string[] _files = Array.Empty<string>();
    private string? _file, _targetId;
    private bool _repeat;
    private float _gap, _volume = .5f, _range = 1000;
    private double _scanTimer;
    private string _status = "Import a sound, choose a vessel, then Play.";
    public string Name => "BYO Music";
    public string Tooltip => "Attach imported sounds to vessels in 3D, with repeat and gaps.";
    public void Initialize() => _files = SoundLibrary.Files.Scan();
    public void Update(double dt)
    {
        _scanTimer += dt;
        if (_scanTimer >= 2) { _scanTimer = 0; _files = SoundLibrary.Files.Scan(); }
        foreach (var sound in _sounds) sound.Update(dt);
    }
    public void RenderFloatingWindows() => _browser.Render(name =>
    {
        _file = name;
        _files = SoundLibrary.Files.Scan();
        _status = $"Imported {name}.";
    });

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##byo_music");
        if (ImGui.Button("Import sound…##byo")) _browser.Open();
        ImGui.SameLine();
        if (ImGui.Button("Refresh library##byo")) _files = SoundLibrary.Files.Scan();
        ImGui.TextDisabled(SoundLibrary.Files.DirectoryPath);
        if (ImGui.BeginCombo("Sound##byo", _file ?? "Choose a sound"))
        {
            ImGui.InputTextWithHint("##byo_sound_filter", "Filter sounds…"u8, _soundFilter);
            foreach (string file in _files)
                if (file.Contains(_soundFilter.ToString().Trim(), StringComparison.OrdinalIgnoreCase) &&
                    ImGui.Selectable(file, file == _file)) _file = file;
            if (_files.Length == 0) ImGui.TextDisabled("Import an OGG, WAV or MP3 to begin.");
            ImGui.EndCombo();
        }
        var vehicles = VehicleProvider.GetAllVehicles().Where(v => !v.IsDisposed).ToList();
        _targetId ??= VehicleProvider.GetControlledVehicle()?.Id;
        if (ImGui.BeginCombo("Vessel##byo", _targetId ?? "Choose a vessel"))
        {
            ImGui.InputTextWithHint("##byo_vessel_filter", "Filter vessels…"u8, _vesselFilter);
            foreach (var vehicle in vehicles)
                if (vehicle.Id.Contains(_vesselFilter.ToString().Trim(), StringComparison.OrdinalIgnoreCase) &&
                    ImGui.Selectable(vehicle.Id, vehicle.Id == _targetId)) _targetId = vehicle.Id;
            ImGui.EndCombo();
        }
        ImGui.Checkbox("Repeat##byo", ref _repeat);
        ImGui.BeginDisabled(!_repeat);
        ImGui.DragFloat("Gap between plays (s)##byo", ref _gap, .1f, 0, 3600);
        ImGui.EndDisabled();
        ImGui.DragFloat("Volume##byo", ref _volume, .01f, 0, 1);
        ImGui.DragFloat("Audible range (m)##byo", ref _range, 10, 1, 100000);
        ImGui.TextWrapped("Sound follows this vessel relative to the audio camera and uses the game's SFX volume. A zero gap repeats continuously. Plays in real time, including in space and time warp.");
        var target = vehicles.FirstOrDefault(v => v.Id == _targetId);
        ImGui.BeginDisabled(target == null || _file == null || !_files.Contains(_file));
        if (ImGui.Button("Play on vessel##byo") && target != null && _file != null)
        {
            try
            {
                // Replaying the same file on the same vessel replaces it rather than doubling gain.
                foreach (var old in _sounds.Where(s => s.Target == target && s.FileName == _file)) old.Stop();
                _sounds.RemoveAll(s => s.Target == target && s.FileName == _file);
                _sounds.Add(new(target, _file, _repeat, _gap, _volume, _range));
                _status = $"Loading {_file} on {target.Id}…";
            }
            catch (Exception ex) { _status = ex.Message; Console.WriteLine($"byo-music: {ex}"); }
        }
        ImGui.EndDisabled();
        ImGui.TextWrapped(_status);
        if (_sounds.Count > 0)
        {
            ImGui.SeparatorText("Vessel sounds"u8);
            if (ImGui.Button("Stop all##byo")) foreach (var sound in _sounds) sound.Stop();
            ImGui.SameLine();
            if (ImGui.Button("Clear stopped##byo")) _sounds.RemoveAll(s => s.Finished);
            for (int i = 0; i < _sounds.Count; i++) RenderSound(_sounds[i], i);
        }
        SubmodUI.EndContentArea();
    }

    private static void RenderSound(VesselSound sound, int index)
    {
        ImGui.PushID(index);
        ImGui.Text($"{sound.FileName} → {sound.Target.Id}");
        ImGui.TextWrapped(sound.Status);
        ImGui.BeginDisabled(sound.Finished);
        if (ImGui.SmallButton("Stop"u8)) sound.Stop();
        ImGui.SameLine();
        ImGui.Checkbox("Repeat"u8, ref sound.Repeat);
        ImGui.DragFloat("Gap (s)"u8, ref sound.GapSeconds, .1f, 0, 3600);
        ImGui.DragFloat("Volume"u8, ref sound.Volume, .01f, 0, 1);
        ImGui.DragFloat("Range (m)"u8, ref sound.RangeMetres, 10, 1, 100000);
        ImGui.EndDisabled();
        ImGui.Separator();
        ImGui.PopID();
    }
    public void Dispose()
    {
        foreach (var sound in _sounds) sound.Dispose();
        _sounds.Clear();
    }
}
