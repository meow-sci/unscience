using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Brutal.Numerics;
using KSA;
using MeowSci.IronManLib;
using MeowSci.KsaAbstractions;

internal static class ModeChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Invoke(IronManSubmod mod, string name, params object[] arguments)
    {
        try
        {
            typeof(IronManSubmod).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(mod, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }

    private static void Queue(IronManSubmod mod, KittenEva kitten, string action) =>
        Invoke(mod, "Queue", kitten, (Action)(() => Invoke(mod, action, kitten)));

    private static void Reject(IronManSubmod mod, string action, KittenEva kitten, string message)
    {
        bool rejected = false;
        try { Invoke(mod, action, kitten); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, message);
    }

    private static (IronManSubmod Mod, KittenEva Kitten, KittenEva Other) Setup()
    {
        KSA.Program.ControlledVehicle = null;
        KSA.Program.Editor = null;
        KSA.Program.EditorFlag = false;
        Universe.CurrentSystem = new();
        PhysicsFrameHook.Actions.Clear();
        VehicleProvider.Live.Clear();
        IronManPatches.Ready = true;
        var kitten = new KittenEva();
        var other = new KittenEva { Id = "other" };
        VehicleProvider.Live.AddRange([kitten, other]);
        KSA.Program.ControlledVehicle = kitten;
        var mod = new IronManSubmod();
        mod.Initialize();
        return (mod, kitten, other);
    }

    private static void Main()
    {
        CheckEditorWithoutRocket();
        CheckRepeatedModes();
        CheckRemovedControlPart();
        CheckGuardsAndQueueLifetime();
        CheckPruningAndDispose();
        Console.WriteLine("PASS: actual IronManSubmod editor configuration, queued full mode transitions, per-kitten isolation, repeated EVA snapshots, disarmed rocket entry/exit, guards, pruning and configured-EVA editor teardown");
    }

    private static void CheckEditorWithoutRocket()
    {
        var (mod, kitten, other) = Setup();
        Require(!mod.IsEnabled(kitten) && !mod.IsConfigured(kitten), "fresh kittens default to ordinary EVA");
        kitten.FlightComputer.AttitudeMode = FlightComputerAttitudeMode.Auto;
        Queue(mod, kitten, "OpenEditor");
        Require(!mod.IsConfigured(kitten) && !KSA.Program.EditorFlag, "editor configuration waits for joined handoff");
        PhysicsFrameHook.Handoff();
        Require(mod.IsConfigured(kitten) && !mod.IsEnabled(kitten) && KSA.Program.EditorFlag,
            "opening editor configures EVA without entering rocket mode");
        Require(!mod.IsConfigured(other) && !mod.IsEnabled(other), "editor configuration remains per-instance");
        Require(kitten.FlightComputer.AttitudeMode == FlightComputerAttitudeMode.Auto,
            "editing while EVA preserves flight-computer settings");
        Require(kitten.Parts.Root.EnsureDefaultsCalls > 0, "configured editor creates default connectors");
        var editor = new VehicleEditor { ExistingVehicle = kitten };
        KSA.Program.Editor = editor;
        int crewCloses = CrewAssignmentWindow.Closes;
        int saveCloses = VehicleSaves.Closes;
        mod.Dispose();
        Require(editor.Disposed && KSA.Program.Editor == null && !KSA.Program.EditorFlag,
            "dispose finalizes configured kitten editor even while in EVA mode");
        Require(CrewAssignmentWindow.Closes == crewCloses + 1 && VehicleSaves.Closes == saveCloses + 1,
            "configured-EVA editor teardown closes stock associated windows");
        Require(!mod.IsConfigured(kitten) && !mod.IsEnabled(kitten) && IronManSubmod.Instance == null,
            "dispose clears configuration, active snapshot and singleton");
    }

    private static void CheckRepeatedModes()
    {
        var (mod, kitten, other) = Setup();
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var computer = kitten.FlightComputer;
            computer.AttitudeMode = FlightComputerAttitudeMode.Auto;
            computer.BurnMode = FlightComputerBurnMode.Auto;
            computer.ManualThrustMode = FlightComputerManualThrustMode.Pulse;
            computer.RCSMode = cycle % 2 == 0 ? FlightComputerRCSMode.Disabled : FlightComputerRCSMode.Enabled;
            computer.AttitudeFrame = cycle % 2 == 0 ? VehicleReferenceFrame.EnuBody : VehicleReferenceFrame.Dock;
            computer.AttitudeTrackTarget = FlightComputerAttitudeTrackTarget.Prograde;
            computer.CustomAttitudeTarget = new double3(cycle, cycle + 1, cycle + 2);
            computer.RollMode = FlightComputerRollMode.Down;
            computer.AngleDeadband = cycle + 0.25f;
            computer.RateLimit = cycle + 0.75f;
            var original = new IronManFlightSettings(computer);
            var expected = new FlightComputer(); original.Restore(expected);
            var originalPart = new Part { Tree = kitten.Parts };
            var originalConnector = new Part.Connector();
            kitten.ControlPart = originalPart;
            kitten.ControlConnector = originalConnector;
            KittenControlMode originalMode = cycle % 2 == 0 ? KittenControlMode.Direct : KittenControlMode.View;
            kitten.ControlMode = originalMode;
            kitten.EngineOn = kitten.HeldInput = true;
            kitten.Parts.Modules.Engines[0].Active = true;
            int cacheBefore = kitten.CacheInvalidations;
            Queue(mod, kitten, "Enable");
            Queue(mod, kitten, "Enable");
            Require(PhysicsFrameHook.Actions.Count == 1 && !mod.IsEnabled(kitten), "pending mode transitions are queued once");
            PhysicsFrameHook.Handoff();
            Require(mod.IsEnabled(kitten) && mod.IsConfigured(kitten) && !mod.IsEnabled(other), "rocket activation is per-kitten");
            Require(!kitten.EngineOn && !kitten.HeldInput && !kitten.Parts.Modules.Engines[0].Active,
                "each rocket entry disarms existing engines and clears held EVA input");
            Require(computer.AttitudeMode == FlightComputerAttitudeMode.Manual && computer.BurnMode == FlightComputerBurnMode.Manual
                && computer.ManualThrustMode == FlightComputerManualThrustMode.Direct, "each rocket entry initializes manual controls");
            Require(kitten.CacheInvalidations == cacheBefore + 1, "rocket entry invalidates orientation-dependent authority");
            Invoke(mod, "Enable", kitten);
            Require(kitten.CacheInvalidations == cacheBefore + 1, "repeated selected-mode request does not recapture EVA snapshot");
            computer.AttitudeFrame = VehicleReferenceFrame.EclBody;
            computer.AttitudeTrackTarget = FlightComputerAttitudeTrackTarget.Up;
            computer.CustomAttitudeTarget = new double3(9, 8, 7);
            computer.RollMode = FlightComputerRollMode.Up;
            computer.RCSMode = FlightComputerRCSMode.Enabled;
            computer.AngleDeadband = 9;
            computer.RateLimit = 8;
            kitten.ControlPart = new Part { Tree = kitten.Parts };
            kitten.ControlConnector = null;
            kitten.ControlMode = originalMode == KittenControlMode.Direct ? KittenControlMode.View : KittenControlMode.Direct;
            var currentBurn = new object(); computer.Burn = currentBurn;
            kitten.EngineOn = kitten.HeldInput = kitten.Parts.Modules.Engines[0].Active = true;
            Queue(mod, kitten, "Disable");
            Require(mod.IsEnabled(kitten), "EVA return waits for joined handoff");
            PhysicsFrameHook.Handoff();
            Require(!mod.IsEnabled(kitten) && mod.IsConfigured(kitten), "EVA return retains editing configuration");
            Require(!kitten.EngineOn && !kitten.HeldInput && !kitten.Parts.Modules.Engines[0].Active, "EVA return stops attached engines");
            Require(kitten.CacheInvalidations == cacheBefore + 2, "EVA return invalidates authority cache");
            SameSettings(computer, expected);
            Require(ReferenceEquals(kitten.ControlPart, originalPart) && ReferenceEquals(kitten.ControlConnector, originalConnector)
                && kitten.ControlMode == originalMode, "return to EVA restores control references and native control mode from this cycle");
            Require(kitten.SetControlPartCalls == cycle + 1 && kitten.SetControlModeCalls == cycle + 1,
                "restoration routes through native validating setters");
            Require(ReferenceEquals(computer.Burn, currentBurn), "mode switch preserves current burn progress object");
        }
        mod.Dispose();
    }

    private static void CheckRemovedControlPart()
    {
        var (mod, kitten, _) = Setup();
        var oldPart = new Part { Tree = kitten.Parts };
        kitten.ControlPart = oldPart;
        kitten.ControlConnector = new Part.Connector();
        Invoke(mod, "Enable", kitten);
        oldPart.Tree = new PartTree();
        Invoke(mod, "Disable", kitten);
        Require(kitten.ControlPart == null && kitten.ControlConnector == null,
            "EVA restoration drops stale control reference removed from original part tree");
        mod.Dispose();
    }

    private static void SameSettings(FlightComputer actual, FlightComputer expected)
    {
        Require(actual.AttitudeMode == expected.AttitudeMode && actual.BurnMode == expected.BurnMode
            && actual.ManualThrustMode == expected.ManualThrustMode && actual.RCSMode == expected.RCSMode,
            "return to EVA restores settings captured for this cycle");
        Require(actual.AttitudeFrame == expected.AttitudeFrame && actual.AttitudeTrackTarget == expected.AttitudeTrackTarget
            && actual.CustomAttitudeTarget.Equals(expected.CustomAttitudeTarget) && actual.RollMode == expected.RollMode
            && actual.AngleDeadband == expected.AngleDeadband && actual.RateLimit == expected.RateLimit,
            "return to EVA restores all selected frame/target/profile settings");
    }

    private static void CheckGuardsAndQueueLifetime()
    {
        var (mod, kitten, other) = Setup();
        IronManPatches.Ready = false;
        Reject(mod, "Enable", kitten, "cannot enter rocket mode without patches");
        IronManPatches.Ready = true;
        kitten.LocomotionState.Mode = LocomotionMode.Ladder;
        Reject(mod, "Enable", kitten, "cannot enter rocket mode while gripping ladder");
        Reject(mod, "OpenEditor", kitten, "cannot edit while gripping ladder");
        kitten.LocomotionState.Mode = LocomotionMode.Ground;
        KSA.Program.Editor = new VehicleEditor();
        Reject(mod, "Enable", kitten, "cannot enter rocket mode while editing");
        KSA.Program.Editor = null;
        KSA.Program.EditorFlag = true;
        Reject(mod, "Enable", kitten, "cannot enter rocket mode while editor opening is pending");
        KSA.Program.EditorFlag = false;
        KSA.Program.ControlledVehicle = other;
        Reject(mod, "OpenEditor", kitten, "cannot open editor for uncontrolled kitten");
        KSA.Program.ControlledVehicle = kitten;
        Invoke(mod, "Enable", kitten);
        KSA.Program.Editor = new VehicleEditor { ExistingVehicle = kitten };
        Reject(mod, "Disable", kitten, "cannot switch to EVA midway through editor transaction");
        KSA.Program.Editor = null;
        Queue(mod, kitten, "Disable");
        VehicleProvider.Live.Remove(kitten);
        PhysicsFrameHook.Handoff();
        Require(mod.IsEnabled(kitten), "queued action rechecks selected kitten lifetime before mutation");
        mod.Update(0);
        Require(!mod.IsEnabled(kitten) && !mod.IsConfigured(kitten), "pruning removes stale active/configured state");
        mod.Dispose();

        (mod, kitten, _) = Setup();
        Queue(mod, kitten, "Enable");
        mod.Dispose();
        PhysicsFrameHook.Handoff();
        Require(!mod.IsEnabled(kitten) && !mod.IsConfigured(kitten), "queued callback cannot resurrect disposed mod state");
    }

    private static void CheckPruningAndDispose()
    {
        var (mod, kitten, other) = Setup();
        Invoke(mod, "OpenEditor", kitten);
        KSA.Program.EditorFlag = false;
        kitten.IsDisposed = true;
        mod.Update(0);
        Require(!mod.IsConfigured(kitten), "inactive configured EVA is pruned even when active set empty");
        KSA.Program.ControlledVehicle = other;
        other.FlightComputer.ManualThrustMode = FlightComputerManualThrustMode.Pulse;
        Invoke(mod, "Enable", other);
        other.EngineOn = other.Parts.Modules.Engines[0].Active = true;
        int vehicleWaits = JobSystems.VehicleSolver.Waits;
        int clothWaits = JobSystems.ClothSolvers.Waits;
        mod.Dispose();
        Require(JobSystems.VehicleSolver.Waits == vehicleWaits + 1 && JobSystems.ClothSolvers.Waits == clothWaits + 1,
            "dispose joins vehicle and cloth workers before control/module restoration");
        Require(!other.EngineOn && !other.Parts.Modules.Engines[0].Active
            && other.FlightComputer.ManualThrustMode == FlightComputerManualThrustMode.Pulse,
            "dispose restores active kitten's EVA settings and shuts down its engines");
        Require(!mod.IsEnabled(other) && !mod.IsConfigured(other), "dispose clears configured and active membership");
        mod.Dispose();
        Require(JobSystems.VehicleSolver.Waits == vehicleWaits + 1, "dispose is idempotent");
    }
}
