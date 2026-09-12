using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.Unscience;

/// <summary>Host wiring only; storage and feature ownership live in the shared/feature libraries.</summary>
internal sealed class UnscienceSaves : IDisposable
{
    private readonly SceneSaveCoordinator _coordinator;
    private readonly ConditionalWeakTable<GameSave, SaveDocument> _captures = new();
    private bool _details;

    public UnscienceSaves(IEnumerable<ISubmod> submods)
    {
        var participants = submods.SelectMany(submod => submod is ISaveParticipantSource source
            ? source.SaveParticipants : submod is ISaveParticipant participant ? new[] { participant } : Array.Empty<ISaveParticipant>());
        _coordinator = new(participants);
        NativeSaveHooks.Capturing = Capture;
        NativeSaveHooks.Written = Write;
        NativeSaveHooks.Loading = Load;
        NativeSaveHooks.Resetting = _coordinator.ResetWorld;
        NativeSaveHooks.Restoring = _coordinator.RestoreWorld;
        NativeSaveHooks.LoadFinished = _coordinator.FinishLoad;
    }

    private void Capture(GameSave save)
    {
        _captures.Remove(save);
        _captures.Add(save, _coordinator.Capture());
    }

    private void Write(UncompressedSave save)
    {
        if (!_captures.TryGetValue(save, out var document))
        {
            _coordinator.Report("Native save had no matching state capture; Unscience state was not written.");
            return;
        }
        try
        {
            SaveStorage.Write(save.Directory.FullName, document);
            _coordinator.MarkWritten(save.Id);
        }
        catch (Exception ex) { _coordinator.Report($"KSA saved '{save.Id}', but Unscience state could not be written: {ex.Message}"); }
        finally { _captures.Remove(save); }
    }

    private void Load(UncompressedSave save)
    {
        try { _coordinator.PrepareLoad(SaveStorage.Read(save.Directory.FullName)); }
        catch (Exception ex)
        {
            _coordinator.PrepareLoad(null);
            _coordinator.Report($"Loading native save without Unscience state: {ex.Message}");
        }
    }

    public void RenderStatus()
    {
        if (!NativeSaveHooks.IsApplied)
        {
            ImGui.TextColored(new float4(1, .4f, .3f, 1), "Scene saving unavailable — see game log."u8);
            return;
        }
        ImGui.TextWrapped(_coordinator.Status);
        if (_coordinator.Messages.Count > 0)
            ImGui.TextColored(new float4(1, .7f, .2f, 1), $"Save/load warnings: {_coordinator.Messages.Count}");
        if (ImGui.SmallButton(_details ? "Hide save details" : "Scene save details")) _details = !_details;
        if (!_details) return;
        ImGui.TextWrapped("Use KSA's Save / Load. Scene setups are stored with each save. Window layout autosave is separate. Sounds and camera sequences restore stopped or paused."u8);
        ImGui.TextWrapped("Keep imported PNG, GLB and sound libraries and runtime part mods installed. Copy those dependencies when moving saves to another computer."u8);
        ImGui.Text($"Registered state adapters: {_coordinator.ParticipantCount}. Retained incompatible records: {_coordinator.RetainedCount}.");
        if (_coordinator.RetainedCount > 0)
        {
            ImGui.TextWrapped("Unrestored records are kept in future saves, including missing entries. To save your current edits instead, discard those retained feature records below; the original save file is unchanged until you overwrite it."u8);
            if (ImGui.Button("Use current setup for future saves"u8)) _coordinator.UseCurrentSetup();
        }
        if (_coordinator.Messages.Count > 0)
        {
            if (ImGui.BeginChild("##scene_save_warnings"u8, new float2(0, 140), ImGuiChildFlags.Borders))
                foreach (string message in _coordinator.Messages) ImGui.TextWrapped(message);
            ImGui.EndChild();
        }
        ImGui.Separator();
    }

    public void Dispose()
    {
        NativeSaveHooks.Capturing = null;
        NativeSaveHooks.Written = null;
        NativeSaveHooks.Loading = null;
        NativeSaveHooks.Resetting = null;
        NativeSaveHooks.Restoring = null;
        NativeSaveHooks.LoadFinished = null;
        _coordinator.FinishLoad();
        _captures.Clear();
    }
}
