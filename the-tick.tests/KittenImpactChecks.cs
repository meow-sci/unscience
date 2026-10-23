using System.Linq;
using HarmonyLib;
using KSA;
using MeowSci.TheTickLib;

/// <summary>
/// KSA 5638 kills EVA kittens on a hard landing through <c>DetectKittenImpact</c>, inside the same
/// detector The Tick already postfixes. Registered kittens must survive it like any other force kill.
/// </summary>
internal static class KittenImpactChecks
{
    /// <summary>The production lookups resolve the fixture, and the fixture matches the shipped KSA.dll.</summary>
    public static bool VerifyGameSignatures()
    {
        var detector = TheTickPatches.ResolveDetector();
        Checks.Require(detector.GetParameters().Select(p => p.ParameterType)
            .SequenceEqual(new[] { typeof(VehicleUpdateState), typeof(SimStep) }), "production lookup uses the 5638 (VehicleUpdateState, SimStep) detector");
        Checks.Require(detector.GetParameters()[0].Name == "vehicleState", "prefix/postfix bind the detector's vehicleState parameter by name");
        bool probed = KsaAssemblyProbe.VerifyMirror(detector, Checks.Require, "KSA.PhysicsBubble::DetectKittenImpact");
        var getter = AccessTools.PropertyGetter(typeof(Part), nameof(Part.CrashTolerancePascals));
        return KsaAssemblyProbe.VerifyMirror(getter, Checks.Require) && probed;
    }

    public static void Run(Vehicle protectedKitten)
    {
        foreach (var (surface, cause) in new[]
        {
            (Situation.Terrain, VehicleDestructionCause.GroundImpact),
            (Situation.Ocean, VehicleDestructionCause.OceanImpact)
        })
        {
            ExpectImpact(new Vehicle(protectedKitten.Id), surface, cause);
            var state = Land(protectedKitten, surface);
            Checks.Require(state.DestructionEvent == null, $"registered kitten survives a 25 m/s {surface} impact");
            Checks.Require(state.KittenSurfaceVelocityTime == Touchdown.NextTime, "native kitten velocity sampling continues");
            // Deliberate: the native impact branch returns before load telemetry, so that one frame reads empty.
            Checks.Require(state.UpdateData.NewStructuralLoad.MaxGLoad == 0, "kitten-impact frame keeps native (unwritten) telemetry");
        }
        var pending = new VehicleDestructionEvent { Cause = VehicleDestructionCause.Collision };
        var queued = Land(protectedKitten, Situation.Terrain, existing: pending);
        Checks.Require(ReferenceEquals(pending, queued.DestructionEvent), "events queued before a kitten landing are never discarded");
    }

    public static void ExpectImpact(Vehicle kitten, Situation surface, VehicleDestructionCause cause) =>
        Checks.Require(Land(kitten, surface).DestructionEvent?.Cause == cause, $"unregistered kitten dies natively by {cause}");

    private static readonly SimStep Touchdown = PhysicsBubble.After(PhysicsBubble.FirstFrame);

    /// <summary>Falls at 25 m/s for one airborne frame, then stops on the surface the next frame.</summary>
    private static VehicleUpdateState Land(Vehicle kitten, Situation surface, VehicleDestructionEvent? existing = null)
    {
        var state = new VehicleUpdateState(kitten) { IsKitten = true, PeakGLoad = 0, VerticalSurfaceVelocity = -25 };
        PhysicsBubble.Detect(state, PhysicsBubble.FirstFrame);
        // KSA resets per-frame load telemetry before each step (VehicleUpdateState.cs:342-347).
        state.UpdateData = default;
        state.Props.Situation = surface;
        state.VerticalSurfaceVelocity = 0;
        state.DestructionEvent = existing;
        PhysicsBubble.Detect(state, Touchdown);
        return state;
    }
}
