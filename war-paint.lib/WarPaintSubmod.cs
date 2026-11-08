using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.WarPaintLib;

/// <summary>The shared War Paint panel, used by Unscience and the development host.</summary>
public sealed partial class WarPaintSubmod : ISubmod, ISaveParticipantSource
{
    public string Name => "War Paint";
    public string Tooltip => "Paint any EVA kitten independently, edit its materials, and save reusable presets for DOH.";

    private readonly WarPaintService _service = WarPaintService.Shared;
    private readonly ImInputString _targetFilter = new(128);
    private readonly ImInputString _presetName = new(128);
    private ImGuiTextFilter _materialFilter = new();
    private KittenEva? _target;
    private PaintSettings _draft = new();
    private string? _selectedPreset;
    private bool _liveEditing;
    private bool _pendingApply;
    private bool _draftChanged;
    private string? _status;
    private bool _statusIsError;

    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return _service.SaveParticipant; }
    }

    public void Initialize()
    {
        if (!_service.Initialize()) SetStatus(_service.LastError ?? "The kitten material system is unavailable.", true);
    }

    public void Update(double dt)
    {
        _service.Update();
        if (_target != null && !GetKittens().Any(k => ReferenceEquals(k, _target)))
        {
            _target = null;
            _liveEditing = false;
            if (!_pendingApply)
            {
                _draft = new();
                _draftChanged = false;
                _selectedPreset = null;
                _presetName.Clear();
            }
            SetStatus("The selected kitten is no longer on EVA. Select a target to continue.", true);
        }
        if (_target != null && _liveEditing && !_service.TryGetPaint(_target, out _))
            _liveEditing = false;
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##wp_content");
        try
        {
            RenderTargetPicker();
            ImGui.Spacing();
            RenderPresets();
            ImGui.Spacing();
            RenderBrush();
            ImGui.Spacing();
            RenderActivePaints();
            if (_status != null)
            {
                ImGui.Spacing();
                ImGui.TextColored(_statusIsError ? new float4(1f, .3f, .3f, 1f) : new float4(.4f, 1f, .4f, 1f), _status);
            }
        }
        finally { SubmodUI.EndContentArea(); }
    }

    public void Dispose()
    {
        _target = null;
        _liveEditing = false;
        _pendingApply = false;
        _draft = new();
        _draftChanged = false;
        _selectedPreset = null;
        _presetName.Clear();
        _service.Cleanup();
    }

    private static List<KittenEva> GetKittens() => VehicleProvider.GetAllVehicles(includeDebris: true)
        .OfType<KittenEva>().Where(k => !k.IsDisposed).OrderBy(k => k.Id, StringComparer.Ordinal).ToList();

    private void RenderTargetPicker()
    {
        ImGui.SeparatorText("Target Kitten"u8);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##wp_target"u8, _target?.Id ?? "Select an EVA kitten..."))
        {
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##wp_target_filter"u8, "Filter kittens..."u8, _targetFilter);
            string filter = _targetFilter.ToString();
            foreach (var kitten in GetKittens())
            {
                if (!kitten.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                bool selected = ReferenceEquals(kitten, _target);
                if (ImGui.Selectable(kitten.Id, selected)) SelectTarget(kitten);
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.TextDisabled("Includes all live EVA kittens, including native crew and DOH spawns."u8);
    }

    private void SelectTarget(KittenEva kitten)
    {
        _target = kitten;
        if (_pendingApply)
        {
            _liveEditing = false;
            SetStatus($"Brush ready for '{kitten.Id}'. Click Apply to kitten.");
            return;
        }
        var snapshot = _service.Snapshot(kitten);
        _draft = snapshot ?? new PaintSettings();
        _liveEditing = snapshot != null;
        _draftChanged = false;
        _selectedPreset = null;
        _presetName.Clear();
        _status = null;
    }

    private void SetStatus(string message, bool error = false)
    {
        _status = message;
        _statusIsError = error;
    }
}
