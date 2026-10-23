using System;
using System.Threading.Tasks;
using HarmonyLib;
using KSA;
using MeowSci.KitchenSinkLib;

internal static class Checks
{
    private static int _checks;

    private static void Main()
    {
        var axle = new Vehicle("Axle");
        var cradle = new Vehicle("Cradle");
        var evaKitten = new Vehicle("Kitten");
        ExpectCause(new VehicleUpdateState(axle), VehicleDestructionCause.ExcessiveGForce);
        KittenImpactChecks.ExpectImpact(evaKitten, 25, Situation.Terrain, VehicleDestructionCause.GroundImpact);
        bool probed = KittenImpactChecks.VerifyGameSignatures();
        var harmony = new Harmony("kitchen-sink.tests");
        GLoadProtectionPatches.Apply(harmony);
        try
        {
            Require(GLoadProtectionPatches.IsApplied, "patch reports ready");
            GLoadProtectionPatches.Apply(harmony);
            Require(GLoadProtectionPatches.IsApplied, "repeated apply is idempotent");
            Require(GLoadProtection.Add(evaKitten), "EVA kittens can be protected");
            KittenImpactChecks.Run(evaKitten, new Vehicle("Stray"));
            Require(GLoadProtection.Remove(evaKitten), "kitten protection can be removed");
            KittenImpactChecks.ExpectImpact(evaKitten, 25, Situation.Terrain, VehicleDestructionCause.GroundImpact);
            Require(GLoadProtection.Add(axle) && GLoadProtection.Add(cradle), "multiple vehicles can be protected");
            Require(!GLoadProtection.Add(axle) && GLoadProtection.Snapshot().Length == 2, "duplicate add is harmless");
            foreach (var vehicle in new[] { axle, cradle })
            foreach (var situation in new[] { Situation.None, Situation.Terrain, Situation.Ocean })
            {
                var state = new VehicleUpdateState(vehicle) { HadVehicleContactThisFrame = true };
                state.Props.Situation = situation;
                PhysicsBubble.Detect(state);
                Require(state.DestructionEvent == null, "protected vehicle survives G-load in all contact situations");
                Require(state.PeakGLoad == 75 && state.UpdateData.NewStructuralLoad.GLoadFraction == 1.5,
                    "real measurements, limit and telemetry fraction are preserved");
            }

            var sameName = new Vehicle("Axle");
            ExpectCause(new VehicleUpdateState(sameName) { HadVehicleContactThisFrame = true }, VehicleDestructionCause.Collision);
            var kitten = new VehicleUpdateState(sameName) { IsKitten = true, PeakGLoad = 124 };
            PhysicsBubble.Detect(kitten);
            Require(kitten.DestructionEvent == null, "unselected kitten retains its native higher limit");
            ExpectCause(new VehicleUpdateState(sameName) { IsKitten = true, PeakGLoad = 125 }, VehicleDestructionCause.ExcessiveGForce);
            var debris = new VehicleUpdateState(sameName) { IsDebris = true };
            PhysicsBubble.Detect(debris);
            Require(debris.DestructionEvent == null, "unselected debris retains native G-load exemption");
            foreach (bool protectedVehicle in new[] { false, true })
            {
                var vehicle = protectedVehicle ? axle : sameName;
                foreach (double g in new[] { 49.9, 50.0, 5000.0 })
                foreach (double pressure in new[] { 199999.0, 200000.0, 300000.0 })
                {
                    var state = new VehicleUpdateState(vehicle) { PeakGLoad = g, PeakDynamicPressure = pressure };
                    PhysicsBubble.Detect(state);
                    bool expectedFailure = pressure >= 200000 || (!protectedVehicle && g >= 50);
                    Require((state.DestructionEvent != null) == expectedFailure, "independent G and pressure threshold matrix");
                }
            }
            var ocean = new VehicleUpdateState(axle) { PeakDynamicPressure = 200000 };
            ocean.Props.Situation = Situation.Ocean;
            ExpectCause(ocean, VehicleDestructionCause.HydrodynamicForces);
            var terrain = new VehicleUpdateState(axle) { PeakDynamicPressure = 200000 };
            terrain.Props.Situation = Situation.Terrain;
            ExpectCause(terrain, VehicleDestructionCause.GroundImpact);

            object failure = new();
            var partFailure = new VehicleUpdateState(axle) { PartFailureEvent = failure, HadVehicleContactThisFrame = true };
            PhysicsBubble.Detect(partFailure);
            Require(ReferenceEquals(failure, partFailure.PartFailureEvent), "pending part failure is preserved");
            var existing = new VehicleDestructionEvent { Cause = VehicleDestructionCause.Collision };
            var pending = new VehicleUpdateState(axle) { DestructionEvent = existing };
            PhysicsBubble.Detect(pending);
            Require(ReferenceEquals(existing, pending.DestructionEvent), "existing destruction events are untouched");

            Require(GLoadProtection.Remove(axle), "delete removes the selected vehicle");
            ExpectCause(new VehicleUpdateState(axle), VehicleDestructionCause.ExcessiveGForce);
            Require(GLoadProtection.Contains(cradle), "delete leaves other vehicles protected");
            GLoadProtection.Add(axle);
            axle.IsDisposed = true;
            Require(!GLoadProtection.Contains(axle), "disposed objects stop being protected immediately");
            GLoadProtection.Prune(new[] { axle, cradle });
            Require(GLoadProtection.Snapshot().Length == 1, "disposed entries are pruned");
            GLoadProtection.Prune(new[] { sameName });
            Require(GLoadProtection.Snapshot().Length == 0, "scene replacement clears old identities");
            Require(!GLoadProtection.Contains(sameName), "same-name replacement does not inherit protection");

            // Worker reads and UI mutations may overlap without mutable-collection races.
            Parallel.Invoke(
                () => { for (int i = 0; i < 10000; i++) { GLoadProtection.Add(cradle); GLoadProtection.Remove(cradle); } },
                () => { for (int i = 0; i < 10000; i++) { GLoadProtection.Contains(cradle); GLoadProtection.Snapshot(); } });
            GLoadProtection.Add(cradle);
            GLoadProtection.Clear();
            Require(GLoadProtection.Snapshot().Length == 0, "session reset clears protection");
            KittenImpactChecks.CheckRejectedLayouts();
            SaveChecks.Run(harmony);
            CapsuleGlassSaveChecks.Run(harmony);
            IvaCameraChecks.Run(harmony);
            GLoadProtection.Add(cradle);
            GLoadProtection.Add(evaKitten);
        }
        finally { GLoadProtectionPatches.Remove(harmony); }

        Require(!GLoadProtectionPatches.IsApplied && GLoadProtection.Snapshot().Length == 0, "unload clears readiness and state");
        // Re-add registry state after unpatch to prove native behavior was restored.
        GLoadProtection.Add(cradle);
        GLoadProtection.Add(evaKitten);
        ExpectCause(new VehicleUpdateState(cradle), VehicleDestructionCause.ExcessiveGForce);
        KittenImpactChecks.ExpectImpact(evaKitten, 25, Situation.Terrain, VehicleDestructionCause.GroundImpact);
        GLoadProtection.Clear();
        string probe = probed ? "real KSA.dll signatures/anchors verified" : "real KSA.dll probe SKIPPED";
        Console.WriteLine($"PASS: {_checks} G-load protection checks ({probe}); native collision/UI acceptance remains in-game.");
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
