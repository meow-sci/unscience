using System;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using MeowSci.IronManLib;

/// <summary>Iron Man's joined-handoff cache invalidation against the 5638 RCS frame-change gate.</summary>
internal static class RcsCacheChecks
{
    private const float Mass = 80f;
    private static double _time;

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new Exception("RCS cache: " + message);
    }

    public static void Run()
    {
        var harmony = new Harmony("iron-man.fixture.rcs-cache");
        var submod = new IronManSubmod();
        IronManSubmod.Instance = submod;
        var kitten = new KittenEva();
        kitten.Parts.Root.Template.Id = IronManConnectors.BackpackTemplateId;
        var jets = new ThrusterController(kitten.Parts.Root)
        {
            ManualControlMap = ThrusterMapFlags.PitchUp,
            GeometricMap = ThrusterMapFlags.RollRight | ThrusterMapFlags.TranslateForward
        };
        var worker = new FlightComputer();
        IronManControlFramePatches.Apply(harmony);
        IronManRcsOrientationPatches.Apply(harmony);
        try
        {
            Step(kitten, worker, jets);
            RequireMap(kitten, jets, ThrusterMapFlags.PitchUp, "fresh all-zero list computes the EVA map");
            int calls = jets.RecomputeCalls;
            Step(kitten, worker, jets);
            Require(jets.RecomputeCalls == calls, "unchanged frame and mass keep the native cache");

            submod.Enabled.Add(kitten);
            IronManRcsOrientationPatches.InvalidateCache(kitten);
            Step(kitten, worker, jets);
            RequireMap(kitten, jets, jets.GeometricMap, "enable remaps jets in the rocket frame");

            // Disable returns to the Identity EVA frame: the 5638 gate regression.
            submod.Enabled.Remove(kitten);
            IronManRcsOrientationPatches.InvalidateCache(kitten);
            Step(kitten, worker, jets);
            RequireMap(kitten, jets, ThrusterMapFlags.PitchUp, "disable to the Identity frame restores EVA authority");

            // Regression guard: ThrusterControllerGlobalState.Zero caches Identity, so the
            // frame-change gate skips the recompute and leaves zero authority.
            kitten.Parts.States.GetMutableGlobalStateForInitialization() = ThrusterControllerGlobalState.Zero;
            calls = jets.RecomputeCalls;
            Step(kitten, worker, jets);
            Require(jets.RecomputeCalls == calls && !HasAuthority(kitten),
                "stock Zero reset leaves RCS authority dead under the 5638 gate");
            IronManRcsOrientationPatches.InvalidateCache(kitten);
            Step(kitten, worker, jets);
            RequireMap(kitten, jets, ThrusterMapFlags.PitchUp, "invalidation recovers from a Zero cache");

            // An explicit control part aligned with the body: enabling changes no frame at all.
            kitten.ControlPart = new Part();
            submod.Enabled.Add(kitten);
            IronManRcsOrientationPatches.InvalidateCache(kitten);
            Step(kitten, worker, jets);
            RequireMap(kitten, jets, jets.GeometricMap, "enable with an unchanged explicit frame remaps jets");
            submod.Enabled.Remove(kitten);
            IronManRcsOrientationPatches.InvalidateCache(kitten);
            Step(kitten, worker, jets);
            RequireMap(kitten, jets, ThrusterMapFlags.PitchUp, "disable with an unchanged explicit frame restores EVA map");
            kitten.ControlPart = null;
            Require(jets.ManualControlMap == ThrusterMapFlags.PitchUp, "authored map field never mutated");

            IronManRcsOrientationPatches.InvalidateCache(kitten);
            var invalidated = kitten.Parts.States.GlobalState;
            Require(!invalidated.IsCtrl2BodyCurrent(floatQuat.Identity)
                && !invalidated.IsCtrl2BodyCurrent(floatQuat.Pack(in IronManOrientation.RocketCtrl2Body)),
                "invalidated cache matches no control frame");
        }
        finally
        {
            IronManControlFramePatches.Remove(harmony);
            IronManRcsOrientationPatches.Remove(harmony);
            submod.Enabled.Clear();
        }
        Console.WriteLine("PASS: 5638 RCS frame-change gate: invalidation forces recompute for rocket, Identity EVA and unchanged explicit frames; stock Zero reset regression guarded; steady-state cache retained");
    }

    private static void RequireMap(KittenEva kitten, ThrusterController jets, ThrusterMapFlags expected, string message)
    {
        Require(jets.State.ControlMap == expected, message + " (map)");
        Require(HasAuthority(kitten), message + " (authority)");
        Require(kitten.Parts.States.GlobalState.IsCtrl2BodyCurrent(floatQuat.Pack(kitten.Helm2Body)),
            message + " (cached frame)");
    }

    private static bool HasAuthority(KittenEva kitten) =>
        kitten.Parts.States.GlobalState.Authority.TotalTorque.X > 0f;

    /// <summary>Worker control step (PhysicsBubble.cs:1372-1373), then module update with Ctrl2Body = FC.Ctrl2Asmb (VehicleUpdateState.cs:393).</summary>
    private static void Step(KittenEva kitten, FlightComputer worker, ThrusterController jets)
    {
        var nav = new FlightComputerNavigation { Helm2Body = kitten.Helm2Body, Time = ++_time };
        worker.ComputeControl(in nav);
        Rocket.UpdateRockets(kitten.Parts, [jets], Mass, worker.Ctrl2Asmb);
    }
}
