using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using MeowSci.IronManLib;

internal static class TeleportChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Teleport: " + message);
    }

    private static void Near(double3 actual, double3 expected, string message) =>
        Require((actual - expected).Length() < 1e-10, message);

    private static void SameRotation(doubleQuat actual, doubleQuat expected, string message)
    {
        Near(double3.UnitX.Transform(actual), double3.UnitX.Transform(expected), message + " X");
        Near(double3.UnitY.Transform(actual), double3.UnitY.Transform(expected), message + " Y");
        Near(double3.UnitZ.Transform(actual), double3.UnitZ.Transform(expected), message + " Z");
    }

    public static void Run()
    {
        var harmony = new Harmony("iron-man.fixture.teleport");
        var submod = new IronManSubmod();
        IronManSubmod.Instance = submod;
        var kitten = new KittenEva();
        var configuredEva = new KittenEva();
        var vessel = new Vehicle();
        var celestial = new Celestial();
        kitten.TeleportToLocation(celestial, 23.5, -78.25);
        ClearCalls();
        for (int cycle = 0; cycle < 2; cycle++)
        {
            IronManTeleportPatches.Apply(harmony);
            IronManTeleportPatches.Apply(harmony);
            CheckNativeBypass(kitten, celestial, "default EVA");
            submod.Configured.Add(configuredEva);
            CheckNativeBypass(configuredEva, celestial, "configured EVA");
            CheckNativeBypass(vessel, celestial, "ordinary vessel");
            submod.Enabled.Add(kitten);
            CheckRocketPlacement(kitten, celestial, submod, double3.Zero);
            CheckRocketPlacement(kitten, celestial, submod, new double3(1.25, -0.75, 2.5));
            kitten.ControlPart = new Part();
            kitten.ControlConnector = new Part.Connector();
            CheckRocketPlacement(kitten, celestial, submod, double3.Zero);
            kitten.ControlPart = null;
            kitten.ControlConnector = null;
            CheckOtherCallers(kitten, celestial);
            kitten.IsDisposed = true;
            CheckNativeBypass(kitten, celestial, "disposed kitten");
            kitten.IsDisposed = false;
            IronManSubmod.Instance = null;
            CheckNativeBypass(kitten, celestial, "missing mod state");
            IronManSubmod.Instance = submod;
            submod.Enabled.Remove(kitten);
            CheckNativeBypass(kitten, celestial, "returned-to-EVA kitten");
            submod.Enabled.Add(kitten);
            IronManTeleportPatches.Remove(harmony);
            CheckNativeBypass(kitten, celestial, "unloaded patch with retained activation");
            IronManTeleportPatches.Remove(harmony);
            submod.Enabled.Clear();
        }
        CheckIlGuards();
        ClearCalls();
        Console.WriteLine("PASS: native surface-placement call adaptation, exact asymmetric clearance/corners, frame/rate conversion, unchanged arguments/orbit/deferred queue, scoped mode/lifetime gates, other teleport paths, unload/reapply and IL metadata guards");
    }

    private static void ClearCalls()
    {
        Vehicle.PlacementCalls.Clear();
        InputEvents.TeleportInputBuffer.Clear();
    }

    private static (Vehicle.PlacementCall Native, InputEvents.TeleportInputData Queued) Request(Vehicle vehicle, Celestial celestial)
    {
        ClearCalls();
        vehicle.TeleportToLocation(celestial, 23.5, -78.25);
        Require(Vehicle.PlacementCalls.Count == 1 && InputEvents.TeleportInputBuffer.Count == 1,
            "one native helper invocation and one native queued teleport");
        var native = Vehicle.PlacementCalls[0];
        var queued = InputEvents.TeleportInputBuffer[0];
        Require(ReferenceEquals(native.Celestial, celestial) && native.Time == vehicle.TeleportTime
            && native.Latitude == 23.5 && native.Longitude == -78.25 && native.Color.Equals(vehicle.TeleportColor),
            "celestial, next simulation time, location and orbit color passed through unchanged");
        Require(ReferenceEquals(queued.Vehicle, vehicle) && ReferenceEquals(queued.Orbit, native.Result.Orbit),
            "native orbit instance and target vehicle survive queue construction unchanged");
        return (native, queued);
    }

    private static void CheckNativeBypass(Vehicle vehicle, Celestial celestial, string context)
    {
        var (native, queued) = Request(vehicle, celestial);
        Near(native.Min, vehicle.TeleportBoundsMin, context + " retains native min bounds");
        Near(native.Max, vehicle.TeleportBoundsMax, context + " retains native max bounds");
        Near(native.Center, vehicle.TeleportCenter, context + " retains native center");
        SameRotation(queued.Body2Cce, native.Result.Body2Cce, context + " retains native orientation");
        Near(queued.BodyRates, native.Result.BodyRates, context + " retains native body rates");
    }

    private static void CheckRocketPlacement(KittenEva kitten, Celestial celestial, IronManSubmod submod, double3 center)
    {
        kitten.TeleportCenter = center;
        double3 min = kitten.TeleportBoundsMin;
        double3 max = kitten.TeleportBoundsMax;
        doubleQuat previousRotation = kitten.CurrentTeleportRotation;
        Orbit? previousOrbit = kitten.CurrentTeleportOrbit;
        var (native, queued) = Request(kitten, celestial);
        Near(native.Min, new double3(-max.Z, min.Y, min.X), "proxy min uses exact signed axis permutation");
        Near(native.Max, new double3(-min.Z, max.Y, max.X), "proxy max uses exact signed axis permutation");
        Near(native.Center, new double3(-center.Z, center.Y, center.X), "proxy center follows same signed permutation");
        Require(native.Result.Orbit.SurfaceClearance == max.Z - center.Z, "native placement receives exact feet/accessory clearance");
        Near(kitten.TeleportBoundsMin, min, "original min bounds not mutated");
        Near(kitten.TeleportBoundsMax, max, "original max bounds not mutated");
        Near(kitten.TeleportCenter, center, "original center not mutated");
        double3 up = double3.UnitX.Transform(native.Result.Body2Cce);
        Near((-double3.UnitZ).Transform(queued.Body2Cce), up, "kitten head points in native surface-up direction");
        Near(double3.UnitX.Transform(queued.Body2Cce), double3.UnitZ.Transform(native.Result.Body2Cce), "kitten face retains native yaw convention");
        Near(queued.BodyRates.Transform(queued.Body2Cce), celestial.WorldAngularVelocity,
            "body-rate conversion preserves the physical world angular velocity");
        double lowest = double.PositiveInfinity;
        foreach (double3 corner in Corners(min, max))
        {
            var proxyCorner = new double3(-corner.Z, corner.Y, corner.X);
            Require(proxyCorner.X >= native.Min.X && proxyCorner.X <= native.Max.X
                && proxyCorner.Y >= native.Min.Y && proxyCorner.Y <= native.Max.Y
                && proxyCorner.Z >= native.Min.Z && proxyCorner.Z <= native.Max.Z,
                "every asymmetric assembly corner lies in exact native proxy bounds");
            double3 displacement = (corner - center).Transform(queued.Body2Cce);
            double height = double3.Dot(displacement, up) + native.Result.Orbit.SurfaceClearance;
            Require(height >= -1e-10, "every body/accessory corner clears native tangent surface");
            lowest = Math.Min(lowest, height);
        }
        Require(Math.Abs(lowest) < 1e-10, "lowest corner rests at exact native surface clearance");
        foreach (double3 nativeFoot in Corners(native.Min, native.Max).Where(value => value.X == native.Min.X))
        {
            double3 actualFoot = new(nativeFoot.Z, nativeFoot.Y, -nativeFoot.X);
            Require(actualFoot.Z == max.Z, "all four native terrain probes correspond to actual lowest feet/accessory plane");
        }
        SameRotation(kitten.CurrentTeleportRotation, previousRotation, "teleport remains deferred until native queue application");
        Require(ReferenceEquals(kitten.CurrentTeleportOrbit, previousOrbit), "request does not mutate current orbit early");
        submod.Enabled.Remove(kitten);
        queued.Apply();
        SameRotation(kitten.CurrentTeleportRotation, queued.Body2Cce, "later mode change does not rewrite already queued pose");
        Near(kitten.CurrentTeleportRates, queued.BodyRates, "native queue applies converted rates");
        Require(ReferenceEquals(kitten.CurrentTeleportOrbit, native.Result.Orbit), "native queue applies original computed orbit");
        submod.Enabled.Add(kitten);
    }

    private static IEnumerable<double3> Corners(double3 min, double3 max)
    {
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
            yield return new(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z);
    }

    private static void CheckOtherCallers(KittenEva kitten, Celestial celestial)
    {
        ClearCalls();
        var direct = Vehicle.GetInitialKinematicStateForLocation(celestial, kitten.TeleportTime, 9, 10,
            kitten.TeleportBoundsMin, kitten.TeleportBoundsMax, kitten.TeleportCenter, kitten.TeleportColor);
        Near(Vehicle.PlacementCalls.Single().Min, kitten.TeleportBoundsMin, "shared helper direct callers remain unmodified");
        SameRotation(direct.Body2Cce, celestial.PlacementRotation, "shared helper keeps native direct result");
        Require(InputEvents.TeleportInputBuffer.Count == 0, "shared helper does not enqueue extra teleport");
        ClearCalls();
        var otherOrbit = new Orbit();
        var otherRotation = doubleQuat.CreateFromAxisAngle(double3.UnitX, 1.3);
        var otherRates = new double3(0.4, 0.5, 0.6);
        kitten.OtherTeleport(otherOrbit, otherRotation, otherRates);
        var other = InputEvents.TeleportInputBuffer.Single();
        Require(Vehicle.PlacementCalls.Count == 0 && ReferenceEquals(other.Orbit, otherOrbit), "unrelated teleport bypasses surface adaptation");
        SameRotation(other.Body2Cce, otherRotation, "unrelated teleport rotation untouched");
        Near(other.BodyRates, otherRates, "unrelated teleport rates untouched");
    }

    private static void CheckIlGuards()
    {
        var originalMethod = AccessTools.DeclaredMethod(typeof(Vehicle), nameof(Vehicle.GetInitialKinematicStateForLocation));
        var label = new DynamicMethod("teleport_labels", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
        var call = new CodeInstruction(OpCodes.Call, originalMethod);
        call.labels.Add(label);
        call.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        call.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
        var result = IronManTeleportPatches.ReplacePlacementCall([call]).ToArray();
        Require(result.Length == 2 && result[0].opcode == OpCodes.Ldarg_0 && result[1].opcode == OpCodes.Call,
            "transpiler appends receiver before placement adapter call");
        Require(result[0].labels.Contains(label) && result[1].labels.Count == 0,
            "branches execute appended receiver load before adapter");
        Require(result[0].blocks.Single().blockType == ExceptionBlockType.BeginExceptionBlock
            && result[1].blocks.Single().blockType == ExceptionBlockType.EndExceptionBlock,
            "opening exception boundary precedes receiver and ending boundary follows adapter");
        Require(call.opcode == OpCodes.Call && call.labels.Count == 1 && call.blocks.Count == 2,
            "original instruction metadata is not mutated");
        foreach (var invalid in new[] { Array.Empty<CodeInstruction>(), new[] { call, new CodeInstruction(call) } })
        {
            bool rejected = false;
            try { IronManTeleportPatches.ReplacePlacementCall(invalid).ToArray(); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "missing/duplicate placement calls fail closed");
        }
    }
}
