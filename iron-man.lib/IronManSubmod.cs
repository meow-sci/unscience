using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.IronManLib;

/// <summary>Session-only, explicit per-kitten authorization for editor and rigid-vessel flight.</summary>
public sealed partial class IronManSubmod : ISubmod
{
    private readonly Dictionary<KittenEva, IronManEvaSettings> _enabled = new();
    private readonly HashSet<KittenEva> _configured = new();
    private KittenEva[] _enabledSnapshot = Array.Empty<KittenEva>();
    private bool _disposed;
    private bool _pending;
    private string _status = "EVA mode by default. Edit your equipment or choose iron man to fly.";

    public static IronManSubmod? Instance { get; private set; }
    public string Name => "Iron Man";
    public string Tooltip => "Opt an EVA kitten into vessel editing, attachment nodes and rocket flight.";
    // The control frame and RCS mapping are also read by physics workers. Publish membership
    // snapshots so pruning a disposed kitten never races a worker's Dictionary lookup.
    public bool IsEnabled(KittenEva kitten) => Array.IndexOf(Volatile.Read(ref _enabledSnapshot), kitten) >= 0;
    public bool IsConfigured(KittenEva kitten) => !_disposed && _configured.Contains(kitten);
    private void PublishEnabled() => Volatile.Write(ref _enabledSnapshot, _enabled.Keys.ToArray());
    private static KittenEva? Target => Program.Editor?.ExistingVehicle as KittenEva
        ?? Program.ControlledVehicle as KittenEva;

    public void Initialize() => Instance = this;

    public void Update(double dt)
    {
        // A new save/system never inherits activation, even if its vehicle ids are identical.
        if (Universe.CurrentSystem == null) _pending = false;
        if (_configured.Count == 0) return;
        var live = VehicleProvider.GetAllVehicles(includeDebris: true);
        bool changed = false;
        foreach (var kitten in _configured.ToArray())
            if (kitten.IsDisposed || !live.Contains(kitten))
            {
                _configured.Remove(kitten);
                changed |= _enabled.Remove(kitten);
            }
        if (changed) PublishEnabled();
    }

    private void Queue(KittenEva kitten, Action action)
    {
        if (_pending) return;
        _pending = true;
        PhysicsFrameHook.Enqueue(() =>
        {
            _pending = false;
            if (_disposed) return;
            try
            {
                if (kitten.IsDisposed || !VehicleProvider.GetAllVehicles(includeDebris: true).Contains(kitten))
                    throw new InvalidOperationException("The selected kitten is no longer in the current system.");
                action();
            }
            catch (Exception ex)
            {
                _status = ex.Message;
                Console.WriteLine($"iron-man: {ex}");
            }
        });
    }

    private void Enable(KittenEva kitten)
    {
        if (IsEnabled(kitten)) return;
        CheckCanEnter(kitten);
        Configure(kitten);
        var original = new IronManEvaSettings(kitten);
        StopEngines(kitten);
        kitten.FlightComputer.AttitudeMode = FlightComputerAttitudeMode.Manual;
        kitten.FlightComputer.BurnMode = FlightComputerBurnMode.Manual;
        kitten.FlightComputer.SetManualThrustMode(FlightComputerManualThrustMode.Direct);
        _enabled.Add(kitten, original);
        PublishEnabled();
        IronManRcsOrientationPatches.InvalidateCache(kitten);
        _status = "Iron Man mode. Rocket controls are manual; arm and ignite when ready.";
        Console.WriteLine($"iron-man: rocket mode {kitten.Id}");
    }

    private static void CheckCanEnter(KittenEva kitten)
    {
        if (!IronManPatches.Ready)
            throw new InvalidOperationException("Iron Man integration is unavailable; see the game log.");
        if (Program.IsEditorOpen)
            throw new InvalidOperationException("Return to flight before changing modes.");
        if (kitten.LocomotionState.Mode == LocomotionMode.Ladder)
            throw new InvalidOperationException("Release the ladder before editing or entering Iron Man mode.");
    }

    private void Configure(KittenEva kitten)
    {
        if (IsConfigured(kitten)) return;
        IronManConnectors.EnsureDefaults(kitten.Parts.Root);
        kitten.Parts.RecomputeAllDerivedData();
        kitten.UpdateVehicleConfiguration();
        _configured.Add(kitten);
    }

    private void Disable(KittenEva kitten)
    {
        if (!_enabled.TryGetValue(kitten, out var original)) return;
        if (Program.IsEditorOpen)
            throw new InvalidOperationException("Return to flight before changing modes.");
        StopEngines(kitten);
        original.Restore(kitten);
        _enabled.Remove(kitten);
        PublishEnabled();
        IronManRcsOrientationPatches.InvalidateCache(kitten);
        _status = "EVA mode. Native kitten controls restored; equipment and editor remain available.";
        Console.WriteLine($"iron-man: EVA mode {kitten.Id}");
    }

    private static void StopEngines(KittenEva kitten)
    {
        kitten.ClearHeldPlayerInput();
        kitten.SetEnum(VehicleEngine.MainShutdown);
        foreach (var engine in kitten.Parts.Modules.Get<EngineController>())
            engine.SetIsActive(null, false);
        kitten.Parts.RecomputeAllDerivedData();
    }

    private void OpenEditor(KittenEva kitten)
    {
        CheckCanEnter(kitten);
        if (Program.ControlledVehicle != kitten)
            throw new InvalidOperationException("Control this kitten in flight before opening the editor.");
        Configure(kitten);
        StopEngines(kitten);
        Program.EditorFlag = true;
        _status = "Editing the existing kitten. Return to flight applies the assembled equipment.";
    }

    public void Dispose()
    {
        if (_disposed) return;
        // Lifecycle teardown is outside the normal queued handoff: wait before touching modules.
        JobSystems.VehicleSolver.Wait();
        JobSystems.ClothSolvers.Wait();
        if (Program.Editor?.ExistingVehicle is KittenEva editing && IsConfigured(editing))
        {
            // Finish the existing-vessel transaction while the avatar and root guards still
            // exist. This applies the edited tree just as the stock frame-loop teardown does.
            Program.Editor.Dispose();
            Program.Editor = null;
            Program.EditorFlag = false;
            CrewAssignmentWindow.Close();
            VehicleSaves.Close();
        }
        foreach (var pair in _enabled)
        {
            if (pair.Key.IsDisposed) continue;
            try
            {
                StopEngines(pair.Key);
                pair.Value.Restore(pair.Key);
                IronManRcsOrientationPatches.InvalidateCache(pair.Key);
            }
            catch (Exception ex) { Console.WriteLine($"iron-man: teardown {pair.Key.Id}: {ex}"); }
        }
        _enabled.Clear();
        _configured.Clear();
        PublishEnabled();
        _disposed = true;
        if (ReferenceEquals(Instance, this)) Instance = null;
    }
}
