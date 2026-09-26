using System;
using System.Text.Json;
using Brutal.ImGuiApi;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.DentWizardLib;

/// <summary>Click-driven camera launcher. Pending gestures never survive world load or disposal.</summary>
public sealed partial class DentWizardSubmod : ISubmod, ISaveParticipant
{
    public string Name => "Dent Wizard";
    public string Tooltip => "Fire any vessel or EVA kitten from the camera at a clicked target, matching its orbital velocity.";
    private readonly ImInputString _filter = new(128);
    private Vehicle? _source;
    private float _speed = 5f;
    private bool _armed;
    private bool _automatic;
    private bool _initialized;
    private bool _error;
    private string? _status;
    private LaunchRequest? _pending;

    public void Initialize()
    {
        if (_initialized) return;
        PhysicsFrameHook.BeforePhysics += FirePending;
        _initialized = true;
    }

    public void Update(double dt)
    {
        if (_source != null && (_source.IsDisposed || !VehicleProvider.GetAllVehicles(true).Contains(_source)))
        {
            _source = null;
            Cancel("Source no longer exists; select another vessel.");
        }
        if (Program.EditorFlag && (_armed || _pending != null)) Cancel("Launch cancelled — return to flight.");
    }

    private void FirePending(double dt, UniverseTime stateTime)
    {
        var request = _pending;
        _pending = null;
        if (request == null) return;
        // The launch teleports the source, which disposes orbit points the hover job may read.
        PhysicsFrameHook.JoinOrbitReaders();
        try { SetStatus(request.Execute(stateTime), false); }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    internal void Arm(bool automatic)
    {
        _automatic = automatic;
        _armed = true;
        _status = null;
    }

    internal void ToggleAutomaticMode()
    {
        if (_automatic) Cancel("Automatic mode off.");
        else Arm(automatic: true);
    }

    internal void AcceptPick(LaunchRequest? request)
    {
        if (!_armed || _pending != null) return;
        if (request == null)
        {
            SetStatus("No target within 10 km. Click again or press Esc.", true);
            return;
        }
        _pending = request;
        _armed = _automatic;
        SetStatus("Launch queued for the next physics handoff.", false);
    }

    private void SetStatus(string message, bool error)
    {
        _status = message;
        _error = error;
        Console.WriteLine($"dent-wizard: {message}");
    }

    private void Cancel(string message)
    {
        _armed = false;
        _automatic = false;
        _pending = null;
        SetStatus(message, false);
    }

    public void Dispose()
    {
        PhysicsFrameHook.BeforePhysics -= FirePending;
        _initialized = false;
        ResetState();
    }

    // Launch effects are ordinary native vessel state. Form/gesture state is deliberately transient.
    public string SaveId => "dent-wizard";
    public JsonElement CaptureState() => SaveJson.ToElement(new { });
    public void ResetState()
    {
        _source = null;
        _pending = null;
        _armed = false;
        _automatic = false;
        _speed = 5f;
        _status = null;
        _error = false;
        _filter.Clear();
    }
    public Action PrepareRestore(JsonElement state, SaveRestoreContext context) => () => { };
}
