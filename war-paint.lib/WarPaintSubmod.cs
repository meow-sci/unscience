using System;
using System.Collections.Generic;
using Brutal.ImGuiApi;
using Brutal.Logging;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.WarPaintLib;

/// <summary>The shared preset editor, used by Unscience and the development host.</summary>
public sealed partial class WarPaintSubmod : ISubmod, ISaveParticipantSource
{
    public string Name => "War Paint";
    public string Tooltip => "Create kitten paint presets, preview their colors live, and apply them to EVA kittens.";

    private readonly WarPaintService _service = WarPaintService.Shared;
    private readonly PaintPresetEditor _editor = new(WarPaintService.Shared);
    private readonly ImInputString _presetFilter = new(128);
    private readonly ImInputString _presetName = new(512);
    private readonly HashSet<KittenEva> _applySelection = new();
    private readonly ImInputString _materialFilter = new(256);
    private float4 _brushColor = float4.One;
    private string? _dialogError;
    private int _sceneRevision;
    private bool _closeDialogs;
    private long _nextMaterialDiscoveryAt;
    private string? _lastPreviewError;

    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return _service.SaveParticipant; }
    }

    public void Initialize()
    {
        if (!_service.Initialize()) LogStatus(_service.LastError ?? "The kitten material system is unavailable.", true);
        _ = _service.Presets.Names;
        if (_service.Presets.LastError != null) LogStatus(_service.Presets.LastError, true);
    }

    public void Update(double dt)
    {
        _service.Update();
        if (_sceneRevision != _service.SceneRevision)
        {
            _sceneRevision = _service.SceneRevision;
            _editor.Clear();
            _applySelection.Clear();
            _presetName.Clear();
            _presetFilter.Clear();
            _materialFilter.Clear();
            _brushColor = float4.One;
            _dialogError = null;
            _lastPreviewError = null;
            _closeDialogs = true;
            _nextMaterialDiscoveryAt = 0;
        }
        _applySelection.RemoveWhere(k => k.IsDisposed || !ReferenceEquals(VehicleProvider.FindVehicle(k.Id), k));
        // Native material assets can become ready after the first partial catalog. Retry even
        // when some rows exist, using wall time so a paused game also finishes discovery.
        long now = Environment.TickCount64;
        if (now >= _nextMaterialDiscoveryAt)
        {
            _editor.DiscoverMaterials();
            _nextMaterialDiscoveryAt = now + 500;
        }
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##wp_content");
        try
        {
            RenderPresets();
            ImGui.Spacing();
            RenderPresetColors();
            ImGui.Spacing();
            RenderVisorControl();
            ImGui.Spacing();
            RenderActivePaints();
            RenderNewPresetDialog();
            RenderDeletePresetDialog();
            RenderApplyDialog();
            _closeDialogs = false;
        }
        finally { SubmodUI.EndContentArea(); }
    }

    public void Dispose()
    {
        _applySelection.Clear();
        _editor.Clear();
        KittenVisorPatches.Hidden = false;
        _service.Cleanup();
    }

    private void SelectPreset(string? name)
    {
        bool selected = name == null ? _editor.Deselect() : _editor.Select(name);
        if (!selected) { LogStatus(_editor.LastError ?? "Could not select the preset.", true); return; }
        _brushColor = _editor.Draft.Tint;
        _nextMaterialDiscoveryAt = 0;
        _materialFilter.Clear();
        _lastPreviewError = null;
    }

    private void PreviewChanges()
    {
        string? error = _editor.Preview() ? null : _editor.LastError ?? "Some kittens could not preview these colors.";
        if (error != null && error != _lastPreviewError) LogStatus(error, true);
        _lastPreviewError = error;
    }

    private static void LogStatus(string message, bool error = false)
    {
        if (error) DefaultCategory.Log.Error($"war-paint: {message}");
        else DefaultCategory.Log.Info($"war-paint: {message}");
    }

    private static void RenderVisorControl()
    {
        ImGui.SeparatorText("Kitten Visors"u8);
        bool showGlass = !KittenVisorPatches.Hidden;
        ImGui.BeginDisabled(!KittenVisorPatches.IsApplied);
        if (ImGui.Checkbox("Show visor glass (global setting)##wp_visor"u8, ref showGlass))
            KittenVisorPatches.Hidden = !showGlass;
        ImGui.EndDisabled();
    }
}
