using System;
using Brutal.Numerics;
using KSA;

namespace MeowSci.IronManLib;

internal sealed partial class IronManFlightSettings
{
    public sealed class Saved
    {
        public FlightComputerAttitudeMode Attitude { get; set; }
        public FlightComputerBurnMode Burn { get; set; }
        public FlightComputerManualThrustMode Thrust { get; set; }
        public FlightComputerRCSMode Rcs { get; set; }
        public VehicleReferenceFrame Frame { get; set; }
        public FlightComputerAttitudeTrackTarget TrackTarget { get; set; }
        public double3 CustomTarget { get; set; }
        public FlightComputerRollMode Roll { get; set; }
        public float Deadband { get; set; }
        public float RateLimit { get; set; }
    }
    public Saved Capture() => new() { Attitude = _attitude, Burn = _burn, Thrust = _thrust, Rcs = _rcs, Frame = _frame, TrackTarget = _trackTarget, CustomTarget = _customTarget, Roll = _roll, Deadband = _deadband, RateLimit = _rateLimit };
    public static void Validate(Saved data)
    {
        if (data == null || !Enum.IsDefined(data.Attitude) || !Enum.IsDefined(data.Burn) || !Enum.IsDefined(data.Thrust)
            || !Enum.IsDefined(data.Rcs) || !Enum.IsDefined(data.Frame) || !Enum.IsDefined(data.TrackTarget) || !Enum.IsDefined(data.Roll)
            || !float.IsFinite(data.Deadband) || !float.IsFinite(data.RateLimit)
            || !double.IsFinite(data.CustomTarget.X) || !double.IsFinite(data.CustomTarget.Y) || !double.IsFinite(data.CustomTarget.Z))
            throw new InvalidOperationException("Invalid saved flight-computer settings.");
    }
    public IronManFlightSettings(Saved data)
    {
        _attitude = data.Attitude;
        _burn = data.Burn;
        _thrust = data.Thrust;
        _rcs = data.Rcs;
        _frame = data.Frame;
        _trackTarget = data.TrackTarget;
        _customTarget = data.CustomTarget;
        _roll = data.Roll;
        _deadband = data.Deadband;
        _rateLimit = data.RateLimit;
    }
}
