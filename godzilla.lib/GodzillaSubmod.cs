using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using MeowSci.GarrysTorchLib;
using MeowSci.KsaAbstractions;

namespace MeowSci.GodzillaLib;

public sealed class GodzillaSubmod : ISubmod
{
    private const string Owner = "Godzilla";
    private sealed record Session(VesselScaleSnapshot Snapshot, bool Smart, float3 Factor, bool ScalePhysics, bool ScaleColliders);
    private readonly Dictionary<Vehicle, Session> _sessions = new();
    private readonly ImInputString _filter = new(128);
    private string? _vehicleId;
    private bool _smart = true;
    private float _uniform = 1;
    private float3 _axes = new(1);
    private string _status = "Choose a vessel, set its size, then Apply.";
    private bool _disposed;

    public bool ScalePhysics { get; private set; } = true;
    public bool ScaleColliders { get; private set; } = true;
    public bool VisualOnly => !ScalePhysics && !ScaleColliders;

    /// <summary>Compatibility shortcut: enable/disable both physical scaling channels.</summary>
    public void SetVisualOnly(bool value) => SetChannels(!value, !value);
    public void SetScalePhysics(bool value) => SetChannels(value, ScaleColliders);
    public void SetScaleColliders(bool value) => SetChannels(ScalePhysics, value);

    private void SetChannels(bool physics, bool colliders)
    {
        if (_disposed || (ScalePhysics == physics && ScaleColliders == colliders)) return;
        ScalePhysics = physics;
        ScaleColliders = colliders;
        PhysicsFrameHook.Enqueue(() =>
        {
            if (_disposed) return;
            foreach (var (vehicle, session) in _sessions.ToArray())
                ApplyNow(vehicle, session.Smart, session.Factor, physics, colliders);
        });
    }

    public string Name => "Godzilla";
    public string Tooltip => "Resize vessels and kittens. Smart scaling preserves the craft's layout.";
    public void Initialize() => PhysicsFrameHook.BeforePhysics += CheckSessions;
    public void Update(double dt) { }

    /// <summary>Schedule a scale edit at the next safe physics handoff.</summary>
    public void RequestApply(Vehicle vehicle, bool smart, float3 factor)
    {
        if (!WeldScale.IsValid(factor)) { _status = "Each scale must be positive and finite."; return; }
        if (smart) factor = new float3(factor.X);
        _status = $"Applying to {vehicle.Id}…";
        bool physics = ScalePhysics, colliders = ScaleColliders;
        PhysicsFrameHook.Enqueue(() => ApplyNow(vehicle, smart, factor, physics, colliders));
    }

    private void ApplyNow(Vehicle vehicle, bool smart, float3 factor, bool physics, bool colliders)
    {
        if (_disposed) return;
        if (!IsLive(vehicle)) { _status = "That vessel is no longer available."; return; }
        if (!VehicleScaleOwnership.TryAcquire(vehicle, Owner))
        {
            _status = $"Release {VehicleScaleOwnership.GetOwner(vehicle)} control of this vessel first (unweld a source).";
            return;
        }
        VesselScaleSnapshot? snapshot = null;
        bool mutationStarted = false;
        try
        {
            snapshot = _sessions.TryGetValue(vehicle, out var session) ? session.Snapshot : new(vehicle);
            snapshot.ValidateApply(factor, physics, colliders);
            mutationStarted = true;
            snapshot.Apply(smart, factor, physics, colliders);
            _sessions[vehicle] = new(snapshot, smart, factor, physics, colliders);
            _status = $"Applied {(smart ? "Smart" : "XYZ")} scaling (physics {(physics ? "on" : "off")}, colliders {(colliders ? "on" : "off")}) to {vehicle.Id}.";
        }
        catch (Exception ex)
        {
            _status = $"Could not scale {vehicle.Id}: {ex.Message}";
            Console.WriteLine($"godzilla: {ex}");
            if (!mutationStarted)
            {
                if (!_sessions.ContainsKey(vehicle)) VehicleScaleOwnership.Release(vehicle, Owner);
                return; // Unsupported settings must not undo an existing successful session.
            }
            // Roll back partial mutations before the next worker can snapshot them.
            try
            {
                snapshot?.Restore();
                _sessions.Remove(vehicle);
                VehicleScaleOwnership.Release(vehicle, Owner);
            }
            catch (Exception restoreError)
            {
                // Keep the snapshot and ownership so Restore remains available for retry.
                if (snapshot != null) _sessions[vehicle] = new(snapshot, smart, factor, physics, colliders);
                _status += " Restoration also failed; use Restore to retry.";
                Console.WriteLine($"godzilla: restore failed: {restoreError}");
            }
        }
    }

    public void RequestRestore(Vehicle vehicle)
    {
        _status = $"Restoring {vehicle.Id}…";
        PhysicsFrameHook.Enqueue(() => { if (!_disposed) Restore(vehicle); });
    }

    private void Restore(Vehicle vehicle)
    {
        if (!_sessions.TryGetValue(vehicle, out var session)) return;
        try
        {
            if (IsLive(vehicle)) session.Snapshot.Restore();
            else ColliderScalePatches.Forget(vehicle);
            VisualScalePatches.ClearScale(vehicle);
            _sessions.Remove(vehicle);
            VehicleScaleOwnership.Release(vehicle, Owner);
            _status = $"Restored {vehicle.Id}.";
        }
        catch (Exception ex)
        {
            _status = $"Restore failed for {vehicle.Id}: {ex.Message}";
            Console.WriteLine($"godzilla: {ex}");
        }
    }

    private static bool IsLive(Vehicle vehicle) => !vehicle.IsDisposed && VehicleProvider.GetAllVehicles(true).Contains(vehicle);

    private void CheckSessions(double dt, UniverseTime time)
    {
        foreach (var (vehicle, session) in _sessions.ToArray())
        {
            if (!IsLive(vehicle))
            {
                VisualScalePatches.ClearScale(vehicle);
                ColliderScalePatches.Forget(vehicle);
                _sessions.Remove(vehicle);
                VehicleScaleOwnership.Release(vehicle, Owner);
            }
            else if (!session.Snapshot.TopologyMatches() || !ColliderScalePatches.TopologyMatches(vehicle))
            {
                Restore(vehicle);
                _status = $"{vehicle.Id}'s parts changed; restored its remaining parts. Detached pieces keep their current size.";
            }
        }
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##godzilla_content");
        var vehicles = VehicleProvider.GetAllVehicles().Where(v => !v.IsDisposed).ToList();
        if (_vehicleId == null) _vehicleId = VehicleProvider.GetControlledVehicle()?.Id;
        var target = vehicles.FirstOrDefault(v => v.Id == _vehicleId);
        if (ImGui.BeginCombo("Vessel##godzilla", target?.Id ?? "Choose a vessel"))
        {
            ImGui.InputTextWithHint("##godzilla_filter", "Filter vessels…"u8, _filter);
            string query = _filter.ToString().Trim();
            foreach (var vehicle in vehicles)
            {
                if (!vehicle.Id.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                if (ImGui.Selectable(vehicle.Id, vehicle == target))
                {
                    _vehicleId = vehicle.Id;
                    if (_sessions.TryGetValue(vehicle, out var session))
                    {
                        _smart = session.Smart;
                        _uniform = session.Factor.X;
                        _axes = session.Factor;
                    }
                }
            }
            ImGui.EndCombo();
        }
        if (ImGui.Button("Use controlled vessel##godzilla"))
            _vehicleId = VehicleProvider.GetControlledVehicle()?.Id;
        bool physics = ScalePhysics, colliders = ScaleColliders;
        if (ImGui.Checkbox("Scale physics##godzilla"u8, ref physics)) SetScalePhysics(physics);
        if (ImGui.Checkbox("Scale colliders##godzilla"u8, ref colliders)) SetScaleColliders(colliders);
        ImGui.TextWrapped("Rendering always resizes. Physics controls mass, inertia, aero and nominal bubble size. Colliders controls collision size, not whether collisions are enabled. Changes apply to all scaled vessels."u8);
        if (!ScalePhysics && ScaleColliders)
            ImGui.TextWrapped("Collider-only: contacts still exert forces. Original bubble and terrain coverage can miss contacts at very large sizes. XYZ shapes use the largest axis."u8);
        ImGui.Checkbox("Smart scaling##godzilla", ref _smart);
        if (_smart)
        {
            ImGui.TextWrapped("Uniform size, preserving part spacing and original proportions. Child animations inherit the new size.");
            ImGui.DragFloat("Size multiplier##godzilla", ref _uniform, 0.01f, WeldScale.Minimum, WeldScale.Maximum);
        }
        else
        {
            if (!ScalePhysics)
                ImGui.TextWrapped("Visual XYZ multiplies the entire craft along its assembly/body axes, including part spacing."u8);
            else ImGui.TextWrapped("Basic sets every part and subpart's absolute XYZ scale. Part spacing stays fixed; overlaps and exaggerated child sizes are intentional. The game uses the largest axis for collider size.");
            ImGui.DragFloat3("XYZ scale##godzilla", ref _axes, 0.01f, WeldScale.Minimum, WeldScale.Maximum);
        }
        ImGui.TextWrapped("Changes last for this session; Restore returns the captured original size and layout. Growing on the ground can push geometry into the terrain.");
        ImGui.BeginDisabled(target == null);
        if (ImGui.Button("Apply##godzilla") && target != null)
            RequestApply(target, _smart, _smart ? new float3(_uniform) : _axes);
        ImGui.SameLine();
        ImGui.BeginDisabled(target == null || !_sessions.ContainsKey(target));
        if (ImGui.Button("Restore original##godzilla") && target != null) RequestRestore(target);
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        if (_sessions.Count > 0)
        {
            ImGui.SeparatorText("Scaled vessels"u8);
            foreach (var (vehicle, session) in _sessions.ToArray())
            {
                ImGui.PushID(vehicle.Id);
                ImGui.Text($"{vehicle.Id} — {(session.Smart ? "Smart" : "XYZ")} / physics {(session.ScalePhysics ? "on" : "off")}, colliders {(session.ScaleColliders ? "on" : "off")}");
                ImGui.SameLine();
                if (ImGui.SmallButton("Restore"u8)) RequestRestore(vehicle);
                ImGui.PopID();
            }
            if (ImGui.Button("Restore all##godzilla"))
                foreach (var vehicle in _sessions.Keys.ToArray()) RequestRestore(vehicle);
        }
        ImGui.TextWrapped(_status);
        SubmodUI.EndContentArea();
    }

    public void Dispose()
    {
        _disposed = true;
        PhysicsFrameHook.BeforePhysics -= CheckSessions;
        JobSystems.OrbitSolvers.Wait();
        JobSystems.VehicleSolver.Wait();
        JobSystems.ClothSolvers.Wait();
        foreach (var vehicle in _sessions.Keys.ToArray()) Restore(vehicle);
        foreach (var vehicle in _sessions.Keys)
        {
            VisualScalePatches.ClearScale(vehicle);
            ColliderScalePatches.Clear(vehicle);
            VehicleScaleOwnership.Release(vehicle, Owner);
        }
        _sessions.Clear();
    }
}
