using Brutal.Numerics;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>Restore user control settings without rewinding burn progress or solver results.</summary>
internal sealed partial class IronManFlightSettings
{
    private readonly FlightComputerAttitudeMode _attitude;
    private readonly FlightComputerBurnMode _burn;
    private readonly FlightComputerManualThrustMode _thrust;
    private readonly FlightComputerRCSMode _rcs;
    private readonly VehicleReferenceFrame _frame;
    private readonly FlightComputerAttitudeTrackTarget _trackTarget;
    private readonly double3 _customTarget;
    private readonly FlightComputerRollMode _roll;
    private readonly float _deadband;
    private readonly float _rateLimit;

    public IronManFlightSettings(FlightComputer computer)
    {
        _attitude = computer.AttitudeMode;
        _burn = computer.BurnMode;
        _thrust = computer.ManualThrustMode;
        _rcs = computer.RCSMode;
        _frame = computer.AttitudeFrame;
        _trackTarget = computer.AttitudeTrackTarget;
        _customTarget = computer.CustomAttitudeTarget;
        _roll = computer.RollMode;
        _deadband = computer.AngleDeadband;
        _rateLimit = computer.RateLimit;
    }

    public void Restore(FlightComputer computer)
    {
        computer.AttitudeMode = _attitude;
        computer.BurnMode = _burn;
        computer.SetManualThrustMode(_thrust);
        computer.RCSMode = _rcs;
        computer.AttitudeFrame = _frame;
        computer.AttitudeTrackTarget = _trackTarget;
        computer.CustomAttitudeTarget = _customTarget;
        computer.RollMode = _roll;
        computer.AngleDeadband = _deadband;
        computer.RateLimit = _rateLimit;
    }
}
