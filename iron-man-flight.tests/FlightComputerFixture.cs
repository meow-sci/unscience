using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Brutal.Numerics;

namespace KSA;

public enum GaugeVisibilityFlag { Burn, Engines, EVA, Vehicle, Sequence, Target, IVA, Thrusters, Atmosphere }
public enum FlightComputerAttitudeMode : byte { Manual, Auto }
public enum FlightComputerManualThrustMode : byte { Direct, Pulse }
public enum FlightComputerBurnMode : byte { Manual, Auto }
public enum FlightComputerRCSMode : byte { Disabled, Enabled }
public enum FlightComputerRollMode : byte { Decoupled, Up, Down }
public enum FlightComputerAttitudeProfile : byte { Strict, Balanced, Relaxed }
public enum FlightComputerAttitudeTrackTarget : byte { Up, Prograde, PositiveDv, Toward }
public enum FlightComputerAction : byte { None, DeleteNextBurn, WarpToNextBurn }
public enum VehicleReferenceFrame : byte { EclBody, EnuBody, BurnBody, Dock }
public enum VehicleEngine : byte { MainIgnite, MainShutdown }
public enum KittenEvaAction : byte { ViewMode, DirectMode, Rcs, ManualTrim, RateTrim }

public class FlightComputer
{
    public FlightComputerAttitudeMode AttitudeMode;
    public FlightComputerBurnMode BurnMode;
    public FlightComputerManualThrustMode ManualThrustMode;
    public FlightComputerRCSMode RCSMode;
    public VehicleReferenceFrame AttitudeFrame;
    public FlightComputerAttitudeTrackTarget AttitudeTrackTarget;
    public double3 CustomAttitudeTarget;
    public FlightComputerRollMode RollMode;
    public float AngleDeadband, RateLimit;
    public object? Burn;
    public float3 ErrorRates;
    public float PlannedBurnThrottle;
    public int SetManualThrustModeCalls;
    public void SetManualThrustMode(FlightComputerManualThrustMode mode) { ManualThrustMode = mode; SetManualThrustModeCalls++; }
}

// Fixtures reproduce the stock dispatch seams, not its solver or flight controller.
public partial class Vehicle
{
    public int BaseAvailabilityCalls, EvaAvailabilityCalls;
    public bool Controllable = true, HasBurn, BurnHasDuration, BurnIsFuture, HasTarget, EngineActive;
    public bool HasEngines, HasSequence, IsIva, HasThrusters, HasAtmosphere;
    public bool CustomAvailabilityLock;
    public Enum Selected = FlightComputerAttitudeMode.Manual;
    public readonly List<Enum> AppliedCommands = new();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual bool IsFlightComputerDisabled<T>(T value) where T : Enum
    {
        BaseAvailabilityCalls++;
        if (CustomAvailabilityLock) return true;
        return value switch
        {
            FlightComputerAction.None => false,
            FlightComputerAction.DeleteNextBurn => !Controllable || !HasBurn,
            FlightComputerAction.WarpToNextBurn => !Controllable || !HasBurn || !BurnIsFuture,
            FlightComputerBurnMode => !Controllable || !HasBurn || !BurnHasDuration,
            FlightComputerAttitudeTrackTarget.PositiveDv => !Controllable || !HasBurn,
            FlightComputerAttitudeTrackTarget.Toward => !Controllable || !HasTarget,
            VehicleReferenceFrame.BurnBody => !Controllable || !HasBurn,
            VehicleReferenceFrame.Dock => !Controllable || !HasTarget,
            VehicleEngine => !Controllable || !EngineActive,
            KittenEvaAction => true,
            _ => !Controllable
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual bool IsSet<T>(T value, bool clicked) where T : Enum => Selected.Equals(value);

    public void SetEnum(Enum value) { Selected = value; AppliedCommands.Add(value); }
}

public partial class KittenEva
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override bool IsFlightComputerDisabled<T>(T value)
    {
        EvaAvailabilityCalls++;
        return value is not KittenEvaAction;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public override bool IsSet<T>(T value, bool clicked) => base.IsSet(value, clicked);
}

public class GaugeCanvas
{
    public List<GaugeVisibilityFlag> VisibleInContext = new();
    public bool Enabled = true;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool IsContextVisible()
    {
        if (VisibleInContext.Count == 0) return true;
        Vehicle? controlledVehicle = Program.ControlledVehicle;
        if (controlledVehicle == null) return false;
        foreach (GaugeVisibilityFlag item in VisibleInContext)
        {
            bool flag = item switch
            {
                GaugeVisibilityFlag.Burn => controlledVehicle.HasBurn,
                GaugeVisibilityFlag.Engines => controlledVehicle.HasEngines,
                GaugeVisibilityFlag.EVA => controlledVehicle is KittenEva,
                GaugeVisibilityFlag.Vehicle => !(controlledVehicle is KittenEva),
                GaugeVisibilityFlag.Sequence => controlledVehicle.HasSequence,
                GaugeVisibilityFlag.Target => controlledVehicle.HasTarget,
                GaugeVisibilityFlag.IVA => controlledVehicle.IsIva,
                GaugeVisibilityFlag.Thrusters => controlledVehicle.HasThrusters,
                GaugeVisibilityFlag.Atmosphere => controlledVehicle.HasAtmosphere,
                _ => true,
            };
            if (!flag) return false;
        }
        return true;
    }

    public bool CanDraw() => Enabled && IsContextVisible();
}

public class GaugeButtonFlightComputer(Enum value)
{
    private readonly Enum _enumValue = value;
    public bool Clicked;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool IsDisabled() => Program.ControlledVehicle == null || Program.ControlledVehicle.IsFlightComputerDisabled(_enumValue);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public uint PackData()
    {
        if (Program.ControlledVehicle == null) return 0;
        bool selected = Program.ControlledVehicle.IsSet(_enumValue, Clicked);
        bool disabled = Program.ControlledVehicle.IsFlightComputerDisabled(_enumValue);
        return (Clicked && !disabled ? 1u : 0) | (selected ? 2u : 0) | (disabled ? 4u : 0) | (!disabled ? 8u : 0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnReleased()
    {
        if (!IsDisabled() && Program.ControlledVehicle != null)
            InputEvents.Commands.Add((Program.ControlledVehicle, _enumValue));
    }
}

public static class InputEvents
{
    public static readonly List<(Vehicle Vehicle, Enum Value)> Commands = new();
    public static void ApplyAll()
    {
        foreach (var command in Commands) command.Vehicle.SetEnum(command.Value);
        Commands.Clear();
    }
}
