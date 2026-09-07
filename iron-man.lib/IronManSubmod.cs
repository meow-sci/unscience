using System;
using System.Collections.Generic;
using System.Linq;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.IronManLib;

/// <summary>Session-only, explicit per-kitten authorization for editor and rigid-vessel flight.</summary>
public sealed partial class IronManSubmod : ISubmod
{
    private readonly Dictionary<KittenEva, IronManFlightSettings> _enabled = new();
    private bool _disposed;
    private bool _pending;
    private string _status = "Off by default. Control an EVA kitten to begin.";

    public static IronManSubmod? Instance { get; private set; }
    public string Name => "Iron Man";
    public string Tooltip => "Opt an EVA kitten into vessel editing, attachment nodes and rocket flight.";
    public bool IsEnabled(KittenEva kitten) => !_disposed && _enabled.ContainsKey(kitten);
    private static KittenEva? Target => Program.Editor?.ExistingVehicle as KittenEva
        ?? Program.ControlledVehicle as KittenEva;

    public void Initialize() => Instance = this;

    public void Update(double dt)
    {
        // A new save/system never inherits activation, even if its vehicle ids are identical.
        if (Universe.CurrentSystem == null) _pending = false;
        if (_enabled.Count == 0) return;
        var live = VehicleProvider.GetAllVehicles(includeDebris: true);
        foreach (var kitten in _enabled.Keys.ToArray())
            if (kitten.IsDisposed || !live.Contains(kitten))
                _enabled.Remove(kitten);
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
        if (!IronManPatches.Ready)
            throw new InvalidOperationException("Iron Man integration is unavailable; see the game log.");
        if (Program.IsEditorOpen)
            throw new InvalidOperationException("Return to flight before enabling Iron Man.");
        if (kitten.LocomotionState.Mode == LocomotionMode.Ladder)
            throw new InvalidOperationException("Release the ladder before enabling Iron Man.");
        var original = new IronManFlightSettings(kitten.FlightComputer);
        IronManConnectors.EnsureDefaults(kitten.Parts.Root);
        kitten.Parts.RecomputeAllDerivedData();
        kitten.UpdateVehicleConfiguration();
        kitten.ClearHeldPlayerInput();
        kitten.SetEnum(VehicleEngine.MainShutdown);
        kitten.FlightComputer.AttitudeMode = FlightComputerAttitudeMode.Manual;
        kitten.FlightComputer.BurnMode = FlightComputerBurnMode.Manual;
        kitten.FlightComputer.SetManualThrustMode(FlightComputerManualThrustMode.Direct);
        _enabled.Add(kitten, original);
        _status = "Enabled for this kitten. Open the editor to attach equipment.";
        Console.WriteLine($"iron-man: enabled {kitten.Id}");
    }

    private void Disable(KittenEva kitten)
    {
        if (!_enabled.TryGetValue(kitten, out var original)) return;
        if (Program.IsEditorOpen)
            throw new InvalidOperationException("Return to flight before disabling Iron Man.");
        StopEngines(kitten);
        original.Restore(kitten.FlightComputer);
        _enabled.Remove(kitten);
        _status = "Disabled. EVA movement restored; attached equipment and nodes remain.";
        Console.WriteLine($"iron-man: disabled {kitten.Id}");
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
        if (!IsEnabled(kitten) || Program.IsEditorOpen || Program.ControlledVehicle != kitten)
            throw new InvalidOperationException("Control the enabled kitten in flight before opening the editor.");
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
        if (Program.Editor?.ExistingVehicle is KittenEva editing && IsEnabled(editing))
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
            try { StopEngines(pair.Key); pair.Value.Restore(pair.Key.FlightComputer); }
            catch (Exception ex) { Console.WriteLine($"iron-man: teardown {pair.Key.Id}: {ex}"); }
        }
        _enabled.Clear();
        _disposed = true;
        if (ReferenceEquals(Instance, this)) Instance = null;
    }
}
