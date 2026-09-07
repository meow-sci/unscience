using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>Place active rocket-mode kittens feet-down using the native surface placement logic.</summary>
public static class IronManTeleportPatches
{
    private static bool _applied;

    public static void Apply(Harmony harmony)
    {
        if (_applied) return;
        try
        {
            harmony.Patch(Target(), transpiler: new HarmonyMethod(Own(nameof(ReplacePlacementCall))));
            _applied = true;
        }
        catch
        {
            Remove(harmony);
            throw;
        }
    }

    public static void Remove(Harmony harmony)
    {
        _applied = false;
        harmony.Unpatch(Target(), Own(nameof(ReplacePlacementCall)));
    }

    private static Vehicle.InitialKinematicState PlaceAtLocation(Celestial celestial, UniverseTime time,
        double latitude, double longitude, double3 boundsMinAsmb, double3 boundsMaxAsmb,
        double3 centerMassAsmb, byte4 orbitLineColor, Vehicle vehicle)
    {
        if (!_applied || vehicle is not KittenEva kitten || kitten.IsDisposed
            || IronManSubmod.Instance?.IsEnabled(kitten) != true)
            return Vehicle.GetInitialKinematicStateForLocation(celestial, time, latitude, longitude,
                boundsMinAsmb, boundsMaxAsmb, centerMassAsmb, orbitLineColor);

        // Native placement assumes +X is the nose and samples the minimum-X footprint.
        // Express the complete bounds in that convention first, so both terrain sampling
        // and the height of boots/accessories are correct. This exact signed permutation
        // avoids rounding the axis-aligned bounds with a general quaternion rotation.
        var min = new double3(-boundsMaxAsmb.Z, boundsMinAsmb.Y, boundsMinAsmb.X);
        var max = new double3(-boundsMinAsmb.Z, boundsMaxAsmb.Y, boundsMaxAsmb.X);
        var center = new double3(-centerMassAsmb.Z, centerMassAsmb.Y, centerMassAsmb.X);
        var state = Vehicle.GetInitialKinematicStateForLocation(celestial, time, latitude, longitude,
            min, max, center, orbitLineColor);

        // The orbit already includes correct clearance, launchpad elevation and surface
        // velocity. Convert only the proxy orientation/body-rate coordinates back to EVA.
        var rocketToBody = IronManOrientation.RocketCtrl2Body;
        state.Body2Cce = doubleQuat.Concatenate(doubleQuat.Inverse(rocketToBody), state.Body2Cce);
        state.BodyRates = double3.Transform(state.BodyRates, rocketToBody);
        return state;
    }

    internal static IEnumerable<CodeInstruction> ReplacePlacementCall(IEnumerable<CodeInstruction> instructions)
    {
        var result = new List<CodeInstruction>();
        MethodInfo placement = Placement();
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            var code = new CodeInstruction(instruction);
            if (code.Calls(placement))
            {
                // Append this after the original eight arguments. Branch labels and opening
                // exception boundaries must enter before this new receiver load; an ending
                // boundary still follows the actual call.
                code.opcode = OpCodes.Ldarg_0;
                code.operand = null;
                var call = new CodeInstruction(OpCodes.Call, Own(nameof(PlaceAtLocation)));
                foreach (var block in code.blocks)
                    if (block.blockType == ExceptionBlockType.EndExceptionBlock) call.blocks.Add(block);
                code.blocks.RemoveAll(block => block.blockType == ExceptionBlockType.EndExceptionBlock);
                result.Add(code);
                result.Add(call);
                replaced++;
            }
            else result.Add(code);
        }
        if (replaced != 1)
            throw new InvalidOperationException($"Iron Man expected one surface placement call, found {replaced}.");
        return result;
    }

    private static MethodInfo Target() => AccessTools.DeclaredMethod(typeof(Vehicle), nameof(Vehicle.TeleportToLocation),
        new[] { typeof(Celestial), typeof(double), typeof(double) })
        ?? throw new MissingMethodException(typeof(Vehicle).FullName, nameof(Vehicle.TeleportToLocation));

    private static MethodInfo Placement() => AccessTools.DeclaredMethod(typeof(Vehicle), nameof(Vehicle.GetInitialKinematicStateForLocation),
        new[] { typeof(Celestial), typeof(UniverseTime), typeof(double), typeof(double),
            typeof(double3), typeof(double3), typeof(double3), typeof(byte4) })
        ?? throw new MissingMethodException(typeof(Vehicle).FullName, nameof(Vehicle.GetInitialKinematicStateForLocation));

    private static MethodInfo Own(string name) => AccessTools.DeclaredMethod(typeof(IronManTeleportPatches), name)!;
}
