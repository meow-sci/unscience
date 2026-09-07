using System;
using System.Collections.Generic;
using Brutal.Numerics;

namespace KSA
{
    public enum LocomotionMode { Ground, Ladder }
    public enum KittenControlMode { View, Direct }
    public enum VehicleEngine { MainShutdown }
    public enum FlightComputerAttitudeMode { Manual, Auto }
    public enum FlightComputerBurnMode { Manual, Auto }
    public enum FlightComputerManualThrustMode { Direct, Pulse }
    public enum FlightComputerRCSMode { Disabled, Enabled }
    public enum VehicleReferenceFrame { EclBody, EnuBody, Dock }
    public enum FlightComputerAttitudeTrackTarget { None, Up, Prograde }
    public enum FlightComputerRollMode { Decoupled, Up, Down }
    public class LocomotionState { public LocomotionMode Mode; }

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
        public void SetManualThrustMode(FlightComputerManualThrustMode mode) => ManualThrustMode = mode;
    }

    public class Vehicle { public string Id = "kitten"; public bool IsDisposed; }
    public class KittenEva : Vehicle
    {
        public FlightComputer FlightComputer = new();
        public PartTree Parts = new();
        public LocomotionState LocomotionState = new();
        public Part? ControlPart;
        public Part.Connector? ControlConnector;
        public KittenControlMode ControlMode;
        public int SetControlPartCalls, SetControlModeCalls;
        public bool HeldInput;
        public bool EngineOn;
        public int ClearInputCalls, ShutdownCalls, ConfigurationUpdates, CacheInvalidations;
        public void ClearHeldPlayerInput() { HeldInput = false; ClearInputCalls++; }
        public void SetEnum(VehicleEngine action) { EngineOn = false; ShutdownCalls++; }
        public void UpdateVehicleConfiguration() => ConfigurationUpdates++;
        public void SetControlPart(Part? part, Part.Connector? connector)
        {
            ControlPart = part; ControlConnector = connector; SetControlPartCalls++;
        }
        public void SetControlMode(KittenControlMode mode) { ControlMode = mode; SetControlModeCalls++; }
    }

    public class Part
    {
        public int EnsureDefaultsCalls;
        public PartTree? Tree;
        public class Connector { }
    }
    public class PartTree
    {
        public Part Root = new();
        public PartModules Modules = new();
        public int RecomputeCalls;
        public void RecomputeAllDerivedData() => RecomputeCalls++;
    }
    public class PartModules
    {
        public EngineController[] Engines = [new()];
        public T[] Get<T>() => (T[])(object)Engines;
    }
    public class EngineController
    {
        public bool Active;
        public int StopCalls;
        public void SetIsActive(object? context, bool active) { Active = active; if (!active) StopCalls++; }
    }

    public class VehicleEditor : IDisposable
    {
        public Vehicle? ExistingVehicle;
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }
    public static class Program
    {
        public static Vehicle? ControlledVehicle;
        public static VehicleEditor? Editor;
        public static bool EditorFlag;
        public static bool IsEditorOpen => EditorFlag || Editor != null;
    }
    public class Scheduler { public int Waits; public void Wait() => Waits++; }
    public static class JobSystems
    {
        public static Scheduler VehicleSolver = new();
        public static Scheduler ClothSolvers = new();
    }
    public static class Universe { public static object? CurrentSystem = new(); }
    public static class CrewAssignmentWindow { public static int Closes; public static void Close() => Closes++; }
    public static class VehicleSaves { public static int Closes; public static void Close() => Closes++; }
}

namespace MeowSci.KsaAbstractions
{
    public interface ISubmod { }
    public static class VehicleProvider
    {
        public static List<KSA.Vehicle> Live = new();
        public static List<KSA.Vehicle> GetAllVehicles(bool includeDebris) => Live;
    }
    public static class PhysicsFrameHook
    {
        public static readonly Queue<Action> Actions = new();
        public static void Enqueue(Action action) => Actions.Enqueue(action);
        public static void Handoff()
        {
            while (Actions.TryDequeue(out Action? action)) action();
        }
    }
}

namespace MeowSci.IronManLib
{
    public static class IronManPatches { public static bool Ready = true; }
    public static class IronManConnectors
    {
        public static void EnsureDefaults(KSA.Part root) => root.EnsureDefaultsCalls++;
    }
    public static class IronManRcsOrientationPatches
    {
        public static void InvalidateCache(KSA.KittenEva kitten) => kitten.CacheInvalidations++;
    }
}
