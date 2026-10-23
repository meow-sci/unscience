using System;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using MeowSci.IronManLib;

/// <summary>Helm/Tnm/Ctrl chain and the navball thrust-marker correction (KSA 5638).</summary>
internal static class HelmChecks
{
    private static double _time;

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new Exception("Helm: " + message);
    }

    private static void Near(double3 actual, double3 expected, string message) =>
        Require((actual - expected).Length() < 1e-9, $"{message} (got {actual}, expected {expected})");

    private static void SameRotation(doubleQuat actual, doubleQuat expected, string message)
    {
        Near(double3.UnitX.Transform(actual), double3.UnitX.Transform(expected), message + " X");
        Near(double3.UnitY.Transform(actual), double3.UnitY.Transform(expected), message + " Y");
    }

    public static void Run()
    {
        var harmony = new Harmony("iron-man.fixture.helm");
        var submod = new IronManSubmod();
        IronManSubmod.Instance = submod;
        var kitten = new KittenEva();
        var other = new KittenEva();
        var vessel = new Vehicle
        {
            ControlPart = new Part { Asmb2VehicleAsmb = doubleQuat.CreateFromAxisAngle(double3.UnitZ, 0.9) }
        };
        double3 nose = double3.UnitX.Transform(IronManOrientation.RocketCtrl2Body);
        // Warm the stock marker update and getter before patching, as the optimized game may have.
        Step(kitten, Worker(nose)); Step(vessel, Worker(double3.UnitX));

        for (int cycle = 0; cycle < 2; cycle++)
        {
            IronManControlFramePatches.Apply(harmony);
            IronManNavballPatches.Apply(harmony);
            IronManNavballPatches.Apply(harmony);
            Near(Step(kitten, Worker(nose)), StockMarker(kitten), "default-off kitten keeps the stock marker");

            submod.Enabled.Add(kitten);
            CheckEnabledChain(kitten, nose);
            CheckScope(kitten, other, vessel, submod);

            submod.Enabled.Remove(kitten);
            Near(Step(kitten, Worker(nose)), StockMarker(kitten), "disable restores the stock marker");
            submod.Enabled.Add(kitten);
            IronManNavballPatches.Remove(harmony);
            IronManControlFramePatches.Remove(harmony);
            Near(Step(kitten, Worker(nose)), StockMarker(kitten), "unload restores the stock marker despite membership");
            submod.Enabled.Clear();
        }
        Console.WriteLine("PASS: 5638 helm/Tnm/Ctrl chain from Iron Man's helm, navball thrust marker in the live helm frame for enabled kittens only, stock marker elsewhere, disable/unload/reapply");
    }

    private static void CheckEnabledChain(KittenEva kitten, double3 nose)
    {
        // Thrust along the rocket nose: the worker chain derives every frame from Helm2Body.
        var worker = Worker(nose);
        double3 marker = Step(kitten, worker);
        SameRotation(worker.Helm2Asmb, IronManOrientation.RocketCtrl2Body, "worker Helm2Asmb is the rocket helm");
        SameRotation(worker.Tnm2Asmb, worker.Helm2Asmb, "aligned thrust leaves Tnm on the helm");
        SameRotation(worker.Ctrl2Asmb, worker.Helm2Asmb, "helm control frame drives attitude/RCS");
        SameRotation(kitten.FlightComputer.Helm2Asmb, doubleQuat.Identity, "stock CopyFrom never copies Helm2Asmb");
        Near(marker, double3.UnitX, "thrust marker sits on the rocket nose");
        Require(Math.Abs(double3.Dot(StockMarker(kitten), double3.UnitX)) < 1e-9,
            "uncorrected stock marker would be 90 degrees off the nose");

        // Off-axis trimmed thrust selects the thrust-aligned frame (TNM) for control.
        double3 offAxis = double3.Normalize(nose + 0.3 * double3.UnitX);
        worker = Worker(offAxis, tnm: true);
        marker = Step(kitten, worker);
        Near(double3.UnitX.Transform(worker.Tnm2Asmb), offAxis, "Tnm +X follows the net thrust");
        SameRotation(worker.Ctrl2Asmb, worker.Tnm2Asmb, "TNM control frame drives attitude/TVC/RCS");
        Near(marker, offAxis.Transform(IronManOrientation.RocketCtrl2Body.Inverse()), "off-axis thrust in helm axes");

        // An explicit control part keeps its own helm; the marker follows that helm too.
        var part = new Part { Asmb2VehicleAsmb = doubleQuat.CreateFromAxisAngle(double3.UnitY, 0.4) };
        kitten.ControlPart = part;
        marker = Step(kitten, Worker(nose));
        Near(marker, nose.Transform(part.Asmb2VehicleAsmb.Inverse()), "explicit control part helm");
        kitten.ControlPart = null;

        // No control step yet: the marker stays invalid and untouched.
        var fresh = new KittenEva();
        IronManSubmod.Instance!.Enabled.Add(fresh);
        fresh.ApplyWorkerResults(new FlightComputer());
        Require(!fresh.NavBallMarkerData.ThrustValid && fresh.NavBallMarkerData.ThrustDirHelm == double3.Zero,
            "invalid marker is not written");
        IronManSubmod.Instance.Enabled.Remove(fresh);
    }

    private static void CheckScope(KittenEva kitten, KittenEva other, Vehicle vessel, IronManSubmod submod)
    {
        Near(Step(other, Worker(double3.UnitX)), StockMarker(other), "other kitten keeps the stock marker");
        // Stock is also off for rotated control parts; ordinary vessels must keep stock behaviour.
        Near(Step(vessel, Worker(double3.UnitX)), StockMarker(vessel), "ordinary vessel keeps the stock marker");
        double3 corrected = IronManNavballPatches.ThrustDirHelm(vessel.FlightComputer.Tnm2Asmb, vessel.Helm2Body);
        Require((vessel.NavBallMarkerData.ThrustDirHelm - corrected).Length() > 0.1,
            "ordinary vessel marker is not corrected");
        IronManSubmod.Instance = null;
        double3 nose = double3.UnitX.Transform(IronManOrientation.RocketCtrl2Body);
        Near(Step(kitten, Worker(nose)), StockMarker(kitten), "missing mod state keeps the stock marker");
        IronManSubmod.Instance = submod;
    }

    private static FlightComputer Worker(double3 netThrustAsmb, bool tnm = false) =>
        new() { NetThrustAsmb = netThrustAsmb, IsTnmActive = tnm };

    /// <summary>One worker control step (PhysicsBubble.cs:1372-1373) and main-thread result application.</summary>
    private static double3 Step(Vehicle vehicle, FlightComputer worker)
    {
        var nav = new FlightComputerNavigation { Helm2Body = vehicle.Helm2Body, Time = ++_time };
        worker.ComputeControl(in nav);
        vehicle.ApplyWorkerResults(worker);
        return vehicle.NavBallMarkerData.ThrustDirHelm;
    }

    /// <summary>What stock draws: the main-thread Helm2Asmb is still Identity.</summary>
    private static double3 StockMarker(Vehicle vehicle) =>
        double3.UnitX.Transform(vehicle.FlightComputer.Tnm2Asmb).NormalizeOrZero();
}
