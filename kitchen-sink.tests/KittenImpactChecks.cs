using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KSA;
using MeowSci.KitchenSinkLib;

/// <summary>
/// KSA 5638 kills EVA kittens on a hard landing through <c>DetectKittenImpact</c> instead of the
/// G-load check; protected kittens must keep surviving it (at 5482 that death was a G-load kill).
/// </summary>
internal static class KittenImpactChecks
{
    private const string GLoadAnchor = "KSA.StructuralLoad::get_GLoadFraction";
    private const string KittenImpactAnchor = "KSA.PhysicsBubble::DetectKittenImpact";

    /// <summary>The production lookups resolve the fixture, and the fixture matches the shipped KSA.dll.</summary>
    public static bool VerifyGameSignatures()
    {
        var detector = GLoadProtectionPatches.ResolveDetector();
        Checks.Require(detector.GetParameters().Select(p => p.ParameterType)
            .SequenceEqual(new[] { typeof(VehicleUpdateState), typeof(SimStep) }), "production lookup uses the 5638 (VehicleUpdateState, SimStep) detector");
        bool probed = KsaAssemblyProbe.VerifyMirror(detector, Checks.Require, GLoadAnchor, KittenImpactAnchor);
        return KsaAssemblyProbe.VerifyMirror(GLoadProtectionPatches.ResolveKittenImpact(), Checks.Require) && probed;
    }

    public static void Run(Vehicle protectedKitten, Vehicle unprotectedKitten)
    {
        var native = Land(unprotectedKitten, 25, Situation.Terrain);
        Checks.Require(native.DestructionEvent?.Cause == VehicleDestructionCause.GroundImpact, "unprotected kitten dies on a 25 m/s ground impact");
        Checks.Require(native.UpdateData.NewStructuralLoad.MaxGLoad == 0, "native kitten-impact branch returns before load telemetry");
        ExpectImpact(unprotectedKitten, 25, Situation.Ocean, VehicleDestructionCause.OceanImpact);
        ExpectImpact(unprotectedKitten, 20, Situation.Terrain, VehicleDestructionCause.GroundImpact);
        ExpectImpact(unprotectedKitten, 19.9, Situation.Terrain, null);
        var ladder = Land(unprotectedKitten, 25, Situation.Terrain, onLadder: true);
        Checks.Require(ladder.DestructionEvent == null, "native ladder exemption is preserved");
        var vessel = Land(unprotectedKitten, 25, Situation.Terrain, isKitten: false);
        Checks.Require(vessel.DestructionEvent == null, "non-kitten vehicles never take the kitten-impact branch");

        foreach (var surface in new[] { Situation.Terrain, Situation.Ocean })
        foreach (double peakG in new[] { 0.0, 500.0 })
        {
            var state = Land(protectedKitten, 25, surface, peakG);
            Checks.Require(state.DestructionEvent == null, "protected kitten survives hard landings (impact and G-load)");
            Checks.Require(state.UpdateData.NewStructuralLoad.MaxGLoad == 125
                && state.UpdateData.NewStructuralLoad.PeakGLoad == peakG, "protected impact falls through to native kitten G telemetry");
            Checks.Require(state.KittenSurfaceVelocity == 0 && state.KittenSurfaceVelocityTime == Touchdown.NextTime,
                "the native kitten velocity sample keeps advancing for protected kittens");
        }
        var pressure = Land(protectedKitten, 25, Situation.Terrain, dynamicPressure: 250000);
        Checks.Require(pressure.DestructionEvent?.Cause == VehicleDestructionCause.GroundImpact
            && pressure.UpdateData.NewStructuralLoad.DynamicPressureFraction == 1.25, "fluid-pressure destruction still applies to protected kittens");
        ExpectImpact(new Vehicle(protectedKitten.Id), 25, Situation.Terrain, VehicleDestructionCause.GroundImpact);
    }

    public static void ExpectImpact(Vehicle kitten, double fallSpeed, Situation surface, VehicleDestructionCause? cause)
    {
        var state = Land(kitten, fallSpeed, surface);
        Checks.Require(state.DestructionEvent?.Cause == cause, $"kitten landing at {fallSpeed} m/s on {surface} gives {cause?.ToString() ?? "no death"}");
    }

    public static void CheckRejectedLayouts()
    {
        var getter = AccessTools.PropertyGetter(typeof(StructuralLoad), nameof(StructuralLoad.GLoadFraction));
        var impact = GLoadProtectionPatches.ResolveKittenImpact();
        CodeInstruction G() => new(OpCodes.Call, getter);
        CodeInstruction K() => new(OpCodes.Call, impact);
        foreach (var code in new[]
        {
            new[] { new CodeInstruction(OpCodes.Ret) },
            new[] { G(), G(), K() },
            new[] { G() },
            new[] { K(), K(), G() },
            new[] { K() }
        })
        {
            bool rejected = false;
            try { _ = GLoadProtectionPatches.Transpile(code).ToArray(); }
            catch (InvalidOperationException) { rejected = true; }
            Checks.Require(rejected, "missing/ambiguous G-load or kitten-impact anchors fail explicitly");
        }
        foreach (var code in new[] { new[] { K(), G() }, new[] { G(), K() } })
        {
            var patched = GLoadProtectionPatches.Transpile(code).ToArray();
            Checks.Require(patched.Length == 6 && FilteredAt(patched, getter, "FilterGLoadFraction")
                && FilteredAt(patched, impact, "FilterKittenImpact"), "each anchor is followed by ldarg.0 and its filter");
        }
    }

    private static bool FilteredAt(CodeInstruction[] code, MethodInfo anchor, string filter)
    {
        int i = Array.FindIndex(code, instruction => instruction.Calls(anchor));
        return i >= 0 && i + 2 < code.Length && code[i + 1].opcode == OpCodes.Ldarg_0
            && code[i + 2].operand is MethodInfo method && method.Name == filter;
    }

    private static readonly SimStep Touchdown = PhysicsBubble.After(PhysicsBubble.FirstFrame);

    /// <summary>Falls through the air for one frame, then touches down and stops on the next.</summary>
    private static VehicleUpdateState Land(Vehicle kitten, double fallSpeed, Situation surface, double peakG = 0,
        double dynamicPressure = 0, bool onLadder = false, bool isKitten = true)
    {
        var state = new VehicleUpdateState(kitten)
        {
            IsKitten = isKitten, OnLadder = onLadder, PeakGLoad = 0, VerticalSurfaceVelocity = -fallSpeed
        };
        PhysicsBubble.Detect(state, PhysicsBubble.FirstFrame);
        Checks.Require(state.DestructionEvent == null, "airborne frame only samples kitten velocity");
        // KSA resets per-frame peaks and load telemetry before each step (VehicleUpdateState.cs:342-347).
        state.UpdateData = default;
        state.Props.Situation = surface;
        state.VerticalSurfaceVelocity = 0;
        state.PeakGLoad = peakG;
        state.PeakDynamicPressure = dynamicPressure;
        PhysicsBubble.Detect(state, Touchdown);
        return state;
    }
}
