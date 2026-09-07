using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KSA;
using MeowSci.IronManLib;
using Brutal.Numerics;

internal static class FlightComputerChecks
{
    private static void Require(bool ok, string message)
    {
        if (!ok) throw new Exception("Flight computer: " + message);
    }

    public static void Run()
    {
        var harmony = new Harmony("iron-man.fixture.flight-computer");
        var submod = new IronManSubmod();
        IronManSubmod.Instance = submod;
        var enabled = new KittenEva();
        var ordinaryEva = new KittenEva();
        var vehicle = new Vehicle();
        var autopilot = new GaugeCanvas { VisibleInContext = [GaugeVisibilityFlag.Vehicle] };
        var evaPanel = new GaugeCanvas { VisibleInContext = [GaugeVisibilityFlag.EVA] };
        var up = new GaugeButtonFlightComputer(FlightComputerAttitudeTrackTarget.Up);
        var evaAction = new GaugeButtonFlightComputer(KittenEvaAction.ViewMode);
        KSA.Program.ControlledVehicle = enabled;
        // Warm all patched targets and the generic dispatch before Harmony installs.
        autopilot.IsContextVisible(); up.IsDisabled(); up.PackData();
        vehicle.IsFlightComputerDisabled<Enum>(FlightComputerAttitudeMode.Manual);

        for (int cycle = 0; cycle < 2; cycle++)
        {
            IronManFlightComputerPatches.Apply(harmony);
            IronManFlightComputerPatches.Apply(harmony);
            Require(!autopilot.IsContextVisible() && evaPanel.IsContextVisible(), "default-off HUD stays EVA");
            Require(up.IsDisabled() && !evaAction.IsDisabled(), "default-off buttons keep EVA policy");

            submod.Enabled.Add(enabled);
            Require(autopilot.IsContextVisible() && !evaPanel.IsContextVisible(), "enabled kitten receives vessel HUD");
            autopilot.Enabled = false;
            Require(!autopilot.CanDraw(), "player-hidden HUD remains hidden");
            autopilot.Enabled = true;
            Require(autopilot.CanDraw(), "player may reveal available HUD");
            CheckContextGates(enabled);

            int baseCalls = enabled.BaseAvailabilityCalls;
            int evaCalls = enabled.EvaAvailabilityCalls;
            Require(!up.IsDisabled(), "attitude target available");
            Require(enabled.BaseAvailabilityCalls == baseCalls + 1 && enabled.EvaAvailabilityCalls == evaCalls,
                "enabled call bypasses EVA override through actual base generic method");
            Require(evaAction.IsDisabled(), "EVA locomotion actions disabled under vessel policy");
            enabled.CustomAvailabilityLock = true;
            Require(up.IsDisabled(), "base-only policy change takes effect without duplicating its logic");
            enabled.CustomAvailabilityLock = false;
            CheckButtonStates(enabled, up);
            CheckStockRestrictions(enabled);

            KSA.Program.ControlledVehicle = ordinaryEva;
            Require(!autopilot.IsContextVisible() && evaPanel.IsContextVisible() && up.IsDisabled(),
                "switch to a second unenabled kitten keeps all stock gates");
            KSA.Program.ControlledVehicle = vehicle;
            Require(autopilot.IsContextVisible() && !evaPanel.IsContextVisible() && !up.IsDisabled(),
                "ordinary vessels unaffected");
            vehicle.Controllable = false;
            Require(up.IsDisabled(), "ordinary uncontrollable vessel still restricted");
            vehicle.Controllable = true;
            KSA.Program.ControlledVehicle = null;
            Require(!autopilot.IsContextVisible() && !evaPanel.IsContextVisible() && new GaugeCanvas().IsContextVisible(),
                "null controlled vessel preserves empty-context exception");
            Require(up.IsDisabled() && up.PackData() == 0, "null controlled vehicle remains safe");
            up.OnReleased(); Require(InputEvents.Commands.Count == 0, "null controlled vehicle cannot queue commands");

            KSA.Program.ControlledVehicle = enabled;
            submod.Enabled.Remove(enabled);
            Require(!autopilot.IsContextVisible() && evaPanel.IsContextVisible() && up.IsDisabled(),
                "disable immediately restores EVA panel and button policy");
            submod.Enabled.Add(enabled);
            IronManSubmod.Instance = null;
            Require(!autopilot.IsContextVisible() && up.IsDisabled(), "missing mod state is default-off");
            IronManSubmod.Instance = submod;
            IronManFlightComputerPatches.Remove(harmony);
            Require(!autopilot.IsContextVisible() && evaPanel.IsContextVisible() && up.IsDisabled(),
                "unload restores originals even when activation membership remains");
            IronManFlightComputerPatches.Remove(harmony);
            submod.Enabled.Clear();
        }
        CheckUnexpectedIl();
        CheckSettingsRestoration();
        KSA.Program.ControlledVehicle = null;
        Console.WriteLine("PASS: flight-computer production HUD transpilers, stock context conjunction, native base Enum dispatch, button rendering/click queue, restrictions, instance switching, default-off/disable/unload/reapply, malformed-IL guards, and complete settings restoration");
    }

    private static void CheckContextGates(KittenEva kitten)
    {
        (GaugeVisibilityFlag Flag, Action<bool> Set)[] gates =
        [
            (GaugeVisibilityFlag.Burn, value => kitten.HasBurn = value),
            (GaugeVisibilityFlag.Engines, value => kitten.HasEngines = value),
            (GaugeVisibilityFlag.Sequence, value => kitten.HasSequence = value),
            (GaugeVisibilityFlag.Target, value => kitten.HasTarget = value),
            (GaugeVisibilityFlag.IVA, value => kitten.IsIva = value),
            (GaugeVisibilityFlag.Thrusters, value => kitten.HasThrusters = value),
            (GaugeVisibilityFlag.Atmosphere, value => kitten.HasAtmosphere = value)
        ];
        foreach (var (flag, set) in gates)
        {
            var canvas = new GaugeCanvas { VisibleInContext = [GaugeVisibilityFlag.Vehicle, flag] };
            set(false); Require(!canvas.IsContextVisible(), $"Vehicle AND {flag} retains unmet requirement");
            set(true); Require(canvas.IsContextVisible(), $"Vehicle AND {flag} accepts met requirement");
            canvas.VisibleInContext.Reverse();
            Require(canvas.IsContextVisible(), $"{flag} AND Vehicle retains order-independent requirement");
            set(false); Require(!canvas.IsContextVisible(), $"{flag} AND Vehicle retains unmet requirement");
        }
        Require(!new GaugeCanvas { VisibleInContext = [GaugeVisibilityFlag.EVA, GaugeVisibilityFlag.Vehicle] }.IsContextVisible(),
            "mutually exclusive vehicle/EVA conjunction stays false");
        Require(new GaugeCanvas { VisibleInContext = [(GaugeVisibilityFlag)999] }.IsContextVisible(),
            "unknown context retains stock fallback");
    }

    private static void CheckButtonStates(KittenEva kitten, GaugeButtonFlightComputer up)
    {
        kitten.Selected = FlightComputerAttitudeMode.Manual;
        up.Clicked = true;
        Require(up.PackData() == 9, "available clicked button packs enabled/click bits without selected bit");
        up.OnReleased();
        Require(InputEvents.Commands.Count == 1 && !kitten.Selected.Equals(FlightComputerAttitudeTrackTarget.Up),
            "release uses availability adapter and queues command without early mutation");
        InputEvents.ApplyAll();
        Require(kitten.Selected.Equals(FlightComputerAttitudeTrackTarget.Up) && up.PackData() == 11,
            "queued action commits and inherited IsSet selects HUD button");
        up.Clicked = false;
        Require(up.PackData() == 10, "selected unclicked button packs correctly");
        kitten.CustomAvailabilityLock = true;
        up.Clicked = true;
        Require(up.PackData() == 6, "disabled selected button retains selection and suppresses click/enable bits");
        up.OnReleased(); Require(InputEvents.Commands.Count == 0, "disabled rendering agrees with click rejection");
        kitten.CustomAvailabilityLock = false;
        up.Clicked = false;
    }

    private static void CheckStockRestrictions(KittenEva kitten)
    {
        Enum[] alwaysAvailable = [FlightComputerAttitudeMode.Manual, FlightComputerManualThrustMode.Pulse,
            FlightComputerRCSMode.Enabled, FlightComputerRollMode.Up, FlightComputerAttitudeProfile.Strict,
            FlightComputerAttitudeTrackTarget.Prograde, VehicleReferenceFrame.EclBody];
        foreach (Enum value in alwaysAvailable)
            Require(!new GaugeButtonFlightComputer(value).IsDisabled(), $"regular {value.GetType().Name} policy available");
        (Enum Value, Action<bool> Set)[] restricted =
        [
            (FlightComputerBurnMode.Auto, value => { kitten.HasBurn = value; kitten.BurnHasDuration = value; }),
            (FlightComputerAction.DeleteNextBurn, value => kitten.HasBurn = value),
            (FlightComputerAction.WarpToNextBurn, value => { kitten.HasBurn = value; kitten.BurnIsFuture = value; }),
            (FlightComputerAttitudeTrackTarget.PositiveDv, value => kitten.HasBurn = value),
            (VehicleReferenceFrame.BurnBody, value => kitten.HasBurn = value),
            (FlightComputerAttitudeTrackTarget.Toward, value => kitten.HasTarget = value),
            (VehicleReferenceFrame.Dock, value => kitten.HasTarget = value),
            (VehicleEngine.MainIgnite, value => kitten.EngineActive = value)
        ];
        foreach (var (value, set) in restricted)
        {
            var button = new GaugeButtonFlightComputer(value);
            set(false); Require(button.IsDisabled() && (button.PackData() & 4) != 0, $"{value} retains missing prerequisite gate");
            button.OnReleased(); Require(InputEvents.Commands.Count == 0, $"{value} gate prevents command queue");
            set(true); Require(!button.IsDisabled() && (button.PackData() & 4) == 0, $"{value} unlocks when prerequisite supplied");
            set(false);
        }
        kitten.HasBurn = true; kitten.BurnHasDuration = false; kitten.BurnIsFuture = false;
        Require(new GaugeButtonFlightComputer(FlightComputerBurnMode.Auto).IsDisabled(), "existing zero-duration burn stays restricted");
        Require(new GaugeButtonFlightComputer(FlightComputerAction.WarpToNextBurn).IsDisabled(), "existing past burn stays restricted");
        kitten.HasBurn = false;
    }

    private static void CheckUnexpectedIl()
    {
        var methods = typeof(IronManFlightComputerPatches).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(method => typeof(IEnumerable<CodeInstruction>).IsAssignableFrom(method.ReturnType)).ToArray();
        Require(methods.Length >= 2, "both context and availability IL transformations discovered");
        foreach (MethodInfo method in methods)
        {
            var parameters = method.GetParameters();
            object?[] args = parameters.Select(parameter => parameter.ParameterType == typeof(IEnumerable<CodeInstruction>)
                ? (object)new[] { new CodeInstruction(OpCodes.Ret) }
                : parameter.ParameterType == typeof(MethodBase) ? AccessTools.Method(typeof(GaugeCanvas), nameof(GaugeCanvas.IsContextVisible))
                : null).ToArray();
            bool rejected = false;
            try { ((IEnumerable<CodeInstruction>)method.Invoke(null, args)!).ToArray(); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejected = true; }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, $"{method.Name} rejects absent expected IL instead of silently widening behavior");
        }
    }

    private static void CheckSettingsRestoration()
    {
        var computer = new FlightComputer
        {
            AttitudeMode = FlightComputerAttitudeMode.Auto,
            BurnMode = FlightComputerBurnMode.Auto,
            ManualThrustMode = FlightComputerManualThrustMode.Pulse,
            RCSMode = FlightComputerRCSMode.Disabled,
            AttitudeFrame = VehicleReferenceFrame.Dock,
            AttitudeTrackTarget = FlightComputerAttitudeTrackTarget.Toward,
            CustomAttitudeTarget = new double3(12, 23, 34),
            RollMode = FlightComputerRollMode.Down,
            AngleDeadband = 0.123f,
            RateLimit = 0.456f,
            Burn = new object()
        };
        var saved = new IronManFlightSettings(computer);
        computer.AttitudeMode = FlightComputerAttitudeMode.Manual;
        computer.BurnMode = FlightComputerBurnMode.Manual;
        computer.ManualThrustMode = FlightComputerManualThrustMode.Direct;
        computer.RCSMode = FlightComputerRCSMode.Enabled;
        computer.AttitudeFrame = VehicleReferenceFrame.EnuBody;
        computer.AttitudeTrackTarget = FlightComputerAttitudeTrackTarget.Up;
        computer.CustomAttitudeTarget = new double3(45, 56, 67);
        computer.RollMode = FlightComputerRollMode.Up;
        computer.AngleDeadband = 9;
        computer.RateLimit = 8;
        var currentBurn = new object();
        computer.Burn = currentBurn;
        computer.ErrorRates = new float3(1, 2, 3);
        computer.PlannedBurnThrottle = 0.8f;
        saved.Restore(computer);
        Require(computer.AttitudeMode == FlightComputerAttitudeMode.Auto && computer.BurnMode == FlightComputerBurnMode.Auto
            && computer.ManualThrustMode == FlightComputerManualThrustMode.Pulse && computer.RCSMode == FlightComputerRCSMode.Disabled,
            "original flight modes restored after native panel edits");
        Require(computer.AttitudeFrame == VehicleReferenceFrame.Dock && computer.AttitudeTrackTarget == FlightComputerAttitudeTrackTarget.Toward
            && computer.CustomAttitudeTarget.Equals(new double3(12, 23, 34)) && computer.RollMode == FlightComputerRollMode.Down
            && computer.AngleDeadband == 0.123f && computer.RateLimit == 0.456f,
            "frame/target/custom direction/profile/roll settings restored after native panel edits");
        Require(computer.SetManualThrustModeCalls == 1, "restoration uses the native thrust-mode setter");
        Require(ReferenceEquals(computer.Burn, currentBurn) && computer.ErrorRates.Equals(new float3(1, 2, 3))
            && computer.PlannedBurnThrottle == 0.8f, "restoration keeps current burn and solver telemetry");
    }
}
