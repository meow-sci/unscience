using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using KSA;
using MeowSci.TheTickLib;

internal static class Checks
{
    private static int _checks;

    private static void Main()
    {
        var hull = new Vehicle("Hull", partCount: 4);
        var rover = new Vehicle("Rover", partCount: 2);
        var authoredPart = hull.Parts.Parts[0];
        authoredPart.AuthoredTolerance = 250_000;
        Require(hull.Parts.Parts[1].CrashTolerancePascals == PartStructuralLimits.DerivedFallback
            && authoredPart.CrashTolerancePascals == 250_000, "native tolerance resolution before patching");
        ExpectCause(new VehicleUpdateState(hull), VehicleDestructionCause.ExcessiveGForce);

        var harmony = new Harmony("the-tick.tests");
        TheTickPatches.Apply(harmony);
        try
        {
            Require(TheTickPatches.IsApplied, "patches report ready");
            TheTickPatches.Apply(harmony);
            Require(TheTickPatches.IsApplied, "repeated apply is idempotent");
            Require(hull.Parts.Parts[1].CrashTolerancePascals == PartStructuralLimits.DerivedFallback,
                "unregistered vehicles keep native tolerances after patching");

            Require(TickProtection.Add(hull) && TickProtection.Add(rover), "multiple vehicles can be protected");
            Require(!TickProtection.Add(hull) && TickProtection.Snapshot().Length == 2, "duplicate add is harmless");
            foreach (var part in hull.Parts.Parts)
                Require(part.CrashTolerancePascals == TickProtection.UnbreakableTolerancePascals,
                    "every full part and sub-part of a protected vehicle reports the unbreakable tolerance");
            Require(double.IsFinite(1_000_000_000.0 / authoredPart.CrashTolerancePascals),
                "dent ratio against the unbreakable tolerance stays finite");
            CheckPartFailure(hull, protectedVehicle: true);

            var sameName = new Vehicle("Hull", partCount: 4);
            Require(sameName.Parts.Parts[0].CrashTolerancePascals == PartStructuralLimits.DerivedFallback,
                "same-name vehicle is not protected by identity");
            CheckPartFailure(sameName, protectedVehicle: false);
            var orphan = new Part();
            Require(orphan.CrashTolerancePascals == PartStructuralLimits.DerivedFallback, "parts without a tree resolve natively");
            var loose = new Part { Tree = new PartTree(null, 0) };
            Require(loose.CrashTolerancePascals == PartStructuralLimits.DerivedFallback, "trees without an owner resolve natively");
            var subOnly = new Part { PartParent = hull.Parts.Parts[0] };
            Require(subOnly.CrashTolerancePascals == TickProtection.UnbreakableTolerancePascals,
                "sub-parts resolve ownership through their full part when their own tree is unset");

            CheckStructuralDetector(hull, sameName);

            Require(TickProtection.Remove(hull), "delete removes the selected vehicle");
            Require(hull.Parts.Parts[0].CrashTolerancePascals == 250_000, "deleted vehicle returns to its authored tolerance");
            ExpectCause(new VehicleUpdateState(hull), VehicleDestructionCause.ExcessiveGForce);
            Require(TickProtection.Contains(rover), "delete leaves other vehicles protected");
            TickProtection.Add(hull);
            hull.IsDisposed = true;
            Require(!TickProtection.Contains(hull), "disposed objects stop being protected immediately");
            Require(hull.Parts.Parts[0].CrashTolerancePascals == 250_000, "disposed vehicles resolve natively");
            TickProtection.Prune(new[] { hull, rover });
            Require(TickProtection.Snapshot().Length == 1, "disposed entries are pruned");
            TickProtection.Prune(new[] { sameName });
            Require(TickProtection.Snapshot().Length == 0, "scene replacement clears old identities");
            Require(!TickProtection.Contains(sameName), "same-name replacement does not inherit protection");

            // Physics workers read while the UI mutates; the registry must not race.
            Parallel.Invoke(
                () => { for (int i = 0; i < 10000; i++) { TickProtection.Add(rover); TickProtection.Remove(rover); } },
                () => { for (int i = 0; i < 10000; i++) { TickProtection.Contains(rover); _ = rover.Parts.Parts[0].CrashTolerancePascals; } });
            TickProtection.Add(rover);
            TickProtection.Clear();
            Require(TickProtection.Snapshot().Length == 0, "session reset clears protection");
            SaveChecks.Run(harmony);
            TickProtection.Add(rover);
        }
        finally { TheTickPatches.Remove(harmony); }

        Require(!TheTickPatches.IsApplied && TickProtection.Snapshot().Length == 0, "unload clears readiness and state");
        TickProtection.Add(rover);
        Require(rover.Parts.Parts[0].CrashTolerancePascals == PartStructuralLimits.DerivedFallback, "unpatching restores native tolerance");
        ExpectCause(new VehicleUpdateState(rover), VehicleDestructionCause.ExcessiveGForce);
        TickProtection.Clear();
        Console.WriteLine($"PASS: {_checks} The Tick indestructibility checks; native collision/UI acceptance remains in-game.");
    }

    private static void CheckPartFailure(Vehicle vehicle, bool protectedVehicle)
    {
        var contacts = vehicle.Parts.Parts.Select(part => (part, 5.0e9)).ToList();
        var state = new VehicleUpdateState(vehicle);
        PartFailure.Detect(state, contacts);
        Require((state.PartFailureEvent == null) == protectedVehicle, "extreme contact pressure fails only unprotected parts");
        if (!protectedVehicle)
            Require(state.PartFailureEvent!.DestroyWholeVehicle, "unprotected fragment guard still trips natively");
        var pending = new VehicleUpdateState(vehicle) { PartFailureEvent = new PartFailureEvent() };
        PartFailure.Detect(pending, contacts);
        Require(pending.PartFailureEvent!.FailedParts.Count == 0, "pending part failure events are untouched");
    }

    private static void CheckStructuralDetector(Vehicle protectedVehicle, Vehicle unprotectedVehicle)
    {
        foreach (var situation in new[] { Situation.None, Situation.Terrain, Situation.Ocean })
        {
            var state = new VehicleUpdateState(protectedVehicle) { HadVehicleContactThisFrame = true };
            state.Props.Situation = situation;
            PhysicsBubble.Detect(state);
            Require(state.DestructionEvent == null, "protected vehicle survives G-load in all contact situations");
            Require(state.PeakGLoad == 75 && state.UpdateData.NewStructuralLoad.GLoadFraction == 1.5
                && state.UpdateData.NewStructuralLoad.MaxGLoad == 50, "real measurements, limit and telemetry fraction are preserved");
        }
        foreach (bool isProtected in new[] { false, true })
        {
            var vehicle = isProtected ? protectedVehicle : unprotectedVehicle;
            foreach (double g in new[] { 49.9, 50.0, 5000.0 })
            foreach (double pressure in new[] { 199999.0, 200000.0, 300000.0 })
            {
                var state = new VehicleUpdateState(vehicle) { PeakGLoad = g, PeakDynamicPressure = pressure };
                PhysicsBubble.Detect(state);
                bool expectedFailure = !isProtected && (pressure >= 200000 || g >= 50);
                Require((state.DestructionEvent != null) == expectedFailure, "G and dynamic-pressure destruction are both suppressed only for protected vehicles");
                Require(state.UpdateData.NewStructuralLoad.DynamicPressureFraction == pressure / 200000, "pressure telemetry is preserved");
            }
        }
        ExpectCause(new VehicleUpdateState(unprotectedVehicle) { HadVehicleContactThisFrame = true }, VehicleDestructionCause.Collision);
        var kitten = new VehicleUpdateState(unprotectedVehicle) { IsKitten = true, PeakGLoad = 124 };
        PhysicsBubble.Detect(kitten);
        Require(kitten.DestructionEvent == null, "unselected kitten retains its native higher limit");
        ExpectCause(new VehicleUpdateState(unprotectedVehicle) { IsKitten = true, PeakGLoad = 125 }, VehicleDestructionCause.ExcessiveGForce);
        var debris = new VehicleUpdateState(unprotectedVehicle) { IsDebris = true };
        PhysicsBubble.Detect(debris);
        Require(debris.DestructionEvent == null, "unselected debris retains native G-load exemption");
        ExpectCause(new VehicleUpdateState(unprotectedVehicle) { PeakGLoad = 0, PeakDynamicPressure = 250000 }, VehicleDestructionCause.AerodynamicForces);

        var existing = new VehicleDestructionEvent { Cause = VehicleDestructionCause.Collision };
        var pending = new VehicleUpdateState(protectedVehicle) { DestructionEvent = existing };
        PhysicsBubble.Detect(pending);
        Require(ReferenceEquals(existing, pending.DestructionEvent), "destruction events queued before the detector are never discarded");
        object failure = new PartFailureEvent();
        var partFailure = new VehicleUpdateState(protectedVehicle) { PartFailureEvent = (PartFailureEvent)failure, HadVehicleContactThisFrame = true };
        PhysicsBubble.Detect(partFailure);
        Require(ReferenceEquals(failure, partFailure.PartFailureEvent), "pending part failure is preserved");
    }

    private static void ExpectCause(VehicleUpdateState state, VehicleDestructionCause cause)
    {
        PhysicsBubble.Detect(state);
        Require(state.DestructionEvent?.Cause == cause, $"expected native cause {cause}");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
}
