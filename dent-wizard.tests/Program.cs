using System;
using Brutal.Numerics;
using KSA;
using MeowSci.DentWizardLib;
using MeowSci.KsaAbstractions;
using MeowSci.KitchenSinkLib;

namespace MeowSci.DentWizardTests;

internal static class Entry
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    static void Near(double3 actual, double3 expected, string message) =>
        Check((actual - expected).Length() < 1e-7, message);
    static void Reject(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        throw new Exception(message);
    }

    public static void Main()
    {
        foreach (double speed in new[] { .001, .001f, .05, 5, 100, 10000, (double)float.MaxValue })
            Check(LaunchMath.IsValidSpeed(speed), $"Valid manual speed rejected: {speed}");
        foreach (double speed in new[] { -.1, 0, .000999, double.NaN, double.PositiveInfinity })
        {
            Check(!LaunchMath.IsValidSpeed(speed), $"Invalid speed accepted: {speed}");
            Reject(() => LaunchMath.Velocity(double3.UnitX, speed, double3.Zero, double3.Zero, double3.Zero), "Invalid launch accepted");
        }
        Reject(() => LaunchMath.Velocity(double3.Zero, 5, double3.Zero, double3.Zero, double3.Zero), "Zero direction accepted");
        Reject(() => LaunchMath.Velocity(new double3(0, double.NaN, 0), 5, double3.Zero, double3.Zero, double3.Zero), "NaN direction accepted");

        // Concrete orbital intercept: prograde is +Y, firing direction is perpendicular (+X).
        var orbital = new double3(0, 7500, 0);
        var shot = LaunchMath.Velocity(new double3(100, 0, 0), 10, orbital, double3.Zero, double3.Zero);
        Near(shot, new double3(10, 7500, 0), "Orbital velocity was lost");
        Near(new double3(-100, 0, 0) + shot * 10, orbital * 10, "Perpendicular shot misses a translating target");
        var boost = new double3(31000, -29000, 1200);
        Near(LaunchMath.Velocity(double3.UnitX, 10, orbital + boost, double3.Zero, double3.Zero) - boost,
            shot, "Launch is not invariant under a shared orbital boost");
        Near(LaunchMath.Velocity(double3.UnitX, 10, orbital, new double3(0, 0, 2), new double3(3, 0, 0)),
            new double3(10, 7506, 0), "Hit-point spin velocity missing");

        var body = new Body();
        var source = new Vehicle(new Body()) { BodyRates = new double3(1, 2, 3) };
        var target = new Vehicle(body);
        VehicleProvider.Vehicles.AddRange(new[] { source, target });
        GLoadProtection.Add(source);
        var request = new LaunchRequest(source, target, body, new double3(-100, 0, 0), double3.Zero, 10);
        Check(source.Teleports == 0, "Capturing a request moved the source");
        // Target advanced between UI and physics handoff. Retain relative click geometry.
        target.Orbit = target.Orbit with { Position = new double3(7000000, 125, 0) };
        var time = new UniverseTime(42);
        request.Execute(time);
        GLoadProtection.Prune(VehicleProvider.Vehicles);
        Check(GLoadProtection.Contains(source) && !GLoadProtection.Contains(target),
            "Launching a protected source lost its registration or protected the target");
        Near(source.Orbit.Position, new double3(6999900, 125, 0), "Click drifted between UI and handoff");
        Near(source.Orbit.Velocity, shot, "Production request lost orbital speed");
        Near(source.BodyRates, new double3(1, 2, 3), "Source spin was modified");
        Check(source.Parent == body && source.Orbit.Time == time && source.Updated, "Wrong parent/time or stale per-frame data");
        int launches = source.Teleports;
        source.RejectTeleport = true;
        Reject(() => request.Execute(time), "Native teleport rejection reported success");
        Check(GLoadProtection.Contains(source), "Rejected launch changed G-load protection");
        source.RejectTeleport = false;
        target.IsDisposed = true;
        Reject(() => request.Execute(time), "Disposed target launched");
        target.IsDisposed = false;
        target.Orbit = target.Orbit with { Parent = new Body() };
        Reject(() => request.Execute(time), "Changed target parent launched");
        target.Orbit = target.Orbit with { Parent = body };
        KSA.Program.EditorFlag = true;
        Reject(() => request.Execute(time), "Editor launch allowed");
        KSA.Program.EditorFlag = false;
        VehicleProvider.Vehicles.Clear();
        VehicleProvider.Vehicles.Add(new Vehicle(body));
        Reject(() => request.Execute(time), "Old-world source reference launched after load");
        Check(source.Teleports == launches, "Failed launch changed the source");

        VehicleProvider.Vehicles.Add(source);
        body.Spin = new double3(0, 0, .001);
        body.Rotation = new doubleQuat(0, 0, Math.Sin(Math.PI / 4), Math.Cos(Math.PI / 4));
        var terrain = new LaunchRequest(source, null, body, new double3(110, 0, 0), new double3(100, 0, 0), 5);
        terrain.Execute(time);
        Near(source.Orbit.Position, new double3(0, 110, 0), "Terrain camera was not converted from CCF to CCI");
        Near(source.Orbit.Velocity, new double3(-.1, -5, 0), "Terrain surface rotation velocity was lost");

        // Drive production lifecycle logic, injecting the pending snapshot as the UI would.
        var submod = new DentWizardSubmod();
        var pending = typeof(DentWizardSubmod).GetField("_pending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        submod.Initialize();
        submod.Initialize();
        Check(PhysicsFrameHook.Subscribers == 1, "Repeated initialization registered twice");
        pending.SetValue(submod, terrain);
        launches = source.Teleports;
        int joins = PhysicsFrameHook.OrbitReaderJoins;
        PhysicsFrameHook.Dispatch(time);
        PhysicsFrameHook.Dispatch(time);
        Check(source.Teleports == launches + 1, "A queued click did not fire exactly once");
        Check(PhysicsFrameHook.OrbitReaderJoins == joins + 1, "A shot did not join the orbit readers before teleporting, or an idle frame did");
        pending.SetValue(submod, terrain);
        submod.ResetState();
        Check(submod.FormSpeed == 5f, "Scene reset did not restore default speed");
        PhysicsFrameHook.Dispatch(time);
        Check(source.Teleports == launches + 1, "Scene reset replayed a pending shot");
        Check(submod.CaptureState().ToString() == "{}", "A transient launch was persisted");
        pending.SetValue(submod, terrain);
        submod.Dispose();
        PhysicsFrameHook.Dispatch(time);
        Check(PhysicsFrameHook.Subscribers == 0 && source.Teleports == launches + 1, "Dispose retained a pending shot or callback");
        Check(GLoadProtection.Contains(source), "Dent Wizard reset/unload changed another feature's protection");
        LaunchModeChecks.Run(source, terrain, time);
        GLoadProtection.Clear();
        Console.WriteLine("Dent Wizard: speed, orbital intercept, boost, spin, handoff, parent, stale-world and terrain checks passed.");

    }
}
