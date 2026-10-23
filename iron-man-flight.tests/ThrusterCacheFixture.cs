using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Brutal.Numerics;

namespace KSA;

// Mirrors of the KSA 2026.10.19.5638 RCS authority cache; line numbers are under decomp/KSA/.
// Engines, rocket cores, moles and gimbals are omitted: they stay idle in these fixtures, so only
// the thruster terms of the native update gate remain.

[Flags]
public enum ThrusterMapFlags { None = 0, PitchUp = 1, RollRight = 2, TranslateForward = 4 }
public struct EmptyStruct { }
public struct RocketCoreState { }

/// <summary>ThrusterAuthority.cs:6-32, reduced to the torque total the flight computer reads.</summary>
public struct ThrusterAuthority
{
    public required float3 TotalTorque;
    public static ThrusterAuthority Zero => new() { TotalTorque = float3.Zero };
}

/// <summary>ThrusterControllerState.cs: map, pulse command and activity bookkeeping.</summary>
public struct ThrusterControllerState
{
    public ThrusterMapFlags ControlMap;
    public double CommandPulseTime;
    public bool WasActive;
    public readonly bool IsNonzero() => CommandPulseTime > 0.0;
}

/// <summary>ThrusterControllerGlobalState.cs:6-60; centre-of-mass and pressure checks omitted.</summary>
public struct ThrusterControllerGlobalState
{
    public required float CachedMass;
    public required floatQuat CachedCtrl2Body;
    public required ThrusterAuthority Authority;

    // :20-28: the named Zero caches an Identity control frame.
    public static ThrusterControllerGlobalState Zero => new()
    {
        CachedMass = 0f,
        CachedCtrl2Body = floatQuat.Identity,
        Authority = ThrusterAuthority.Zero
    };

    public void ResetCache(float mass, floatQuat ctrl2Body)
    {
        this = Zero;
        CachedMass = mass;
        CachedCtrl2Body = ctrl2Body;
    }

    // :49-52, verbatim.
    public readonly bool IsCtrl2BodyCurrent(floatQuat ctrl2Body) =>
        MathF.Abs(floatQuat.Dot(ctrl2Body, CachedCtrl2Body)) >= MathF.Cos(0.0005f);

    public readonly bool IsCacheValid(float mass, floatQuat ctrl2Body) =>
        IsCtrl2BodyCurrent(ctrl2Body) && Math.Abs(mass - CachedMass) <= 0.001 * Math.Max(mass, CachedMass);
}

public class ThrusterController(Part root)
{
    public class PartParent(Part fullPart) { public Part FullPart = fullPart; }
    public PartParent Parent = new(root);
    public ThrusterMapFlags? ManualControlMap;
    public bool IsActive = true;
    /// <summary>Fixture: this module's slot in the vehicle's ThrusterControllerState list.</summary>
    public ThrusterControllerState State;
    public ThrusterMapFlags GeometricMap;
    public int GeometricMapCalls, RecomputeCalls;

    /// <summary>Signature of ThrusterController.cs:84; the nullable-map seam (:124-133) is verbatim.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void RecomputeDynamicData(ref ThrusterControllerState state, ref ThrusterAuthority negativeAuthority,
        ref ThrusterAuthority positiveAuthority, ReadOnlySpan<RocketCoreState> coreStates, float3 centerMassAsmb,
        float ambientPressure, floatQuat ctrl2Body)
    {
        RecomputeCalls++;
        ThrusterMapFlags? manualControlMap = ManualControlMap;
        if (manualControlMap.HasValue)
        {
            ThrusterMapFlags valueOrDefault = manualControlMap.GetValueOrDefault();
            state.ControlMap = valueOrDefault;
        }
        else
        {
            state.ControlMap = ComputeControlMap();
        }
        // ComputeIntendedAction (:195+) adds a jet's torque only along its mapped axes.
        if (state.ControlMap != ThrusterMapFlags.None)
            positiveAuthority.TotalTorque += new float3(1f, 1f, 1f);
    }

    /// <summary>Fixture convenience: resolve the map outside a worker step.</summary>
    public ThrusterMapFlags Resolve()
    {
        var negative = ThrusterAuthority.Zero;
        var positive = ThrusterAuthority.Zero;
        RecomputeDynamicData(ref State, ref negative, ref positive, ReadOnlySpan<RocketCoreState>.Empty,
            float3.Zero, 0f, floatQuat.Identity);
        return State.ControlMap;
    }

    private ThrusterMapFlags ComputeControlMap() { GeometricMapCalls++; return GeometricMap; }
}

public static class ModuleStateful<TModule, TState, TGlobal, TFx> where TGlobal : struct
{
    /// <summary>ModuleStateful.cs:264,270,472: a list's global state starts as new TGlobalState().</summary>
    public class StateList
    {
        private TGlobal _globalState = new TGlobal();
        public ref readonly TGlobal GlobalState => ref _globalState;
        public ref TGlobal GetMutableGlobalStateForInitialization() => ref _globalState;
    }
    public static StateList InitializeHotPathList(StateList states) => states;
}

/// <summary>Thruster path of Rocket.UpdateRockets (:83-106), UpdateThrusterCache (:195-213) and RocketsNeedUpdate (:256+).</summary>
public static class Rocket
{
    public static void UpdateRockets(PartTree parts, IReadOnlyList<ThrusterController> thrusters, float mass,
        doubleQuat ctrl2Body)
    {
        // The worker updater copies the list's published global state (ModuleStateful.cs:784)
        // and publishes it back after the step (UpdateBulkStates, :385).
        ThrusterControllerGlobalState global = parts.States.GlobalState;
        floatQuat ctrl = floatQuat.Pack(in ctrl2Body);
        if (RocketsNeedUpdate(in global, thrusters, ctrl))
        {
            UpdateThrusterCache(ref global, thrusters, mass, ctrl);
            foreach (var thruster in thrusters) thruster.State.WasActive = thruster.IsActive;
        }
        parts.States.GetMutableGlobalStateForInitialization() = global;
    }

    private static void UpdateThrusterCache(ref ThrusterControllerGlobalState global,
        IReadOnlyList<ThrusterController> thrusters, float mass, floatQuat ctrl2Body)
    {
        if (global.IsCacheValid(mass, ctrl2Body)) return;
        global.ResetCache(mass, ctrl2Body);
        var negative = ThrusterAuthority.Zero;
        var positive = ThrusterAuthority.Zero;
        foreach (var thruster in thrusters)
            thruster.RecomputeDynamicData(ref thruster.State, ref negative, ref positive,
                ReadOnlySpan<RocketCoreState>.Empty, float3.Zero, 0f, ctrl2Body);
        // Stands in for negativeAuthority.ConservativeWith(positiveAuthority) (:213).
        global.Authority = positive;
    }

    private static bool RocketsNeedUpdate(in ThrusterControllerGlobalState global,
        IReadOnlyList<ThrusterController> thrusters, floatQuat ctrl2Body)
    {
        if (!global.IsCtrl2BodyCurrent(ctrl2Body)) return true;
        foreach (var thruster in thrusters)
            if (thruster.State.IsNonzero() || thruster.State.WasActive != thruster.IsActive) return true;
        return false;
    }
}
