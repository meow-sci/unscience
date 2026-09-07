using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>Use the native vessel gauges and button policy for explicitly enabled EVA kittens.</summary>
public static class IronManFlightComputerPatches
{
    private static bool _applied;
    private static Func<Vehicle, Enum, bool>? _basePolicy;

    public static void Apply(Harmony harmony)
    {
        if (_applied) return;
        try
        {
            // Calling through a Vehicle reference still dispatches to KittenEva's override,
            // which disables every ordinary action. Call the native base nonvirtually.
            // Generic Harmony entry-point patches are not guaranteed to affect this call.
            // Patching the generic override itself would depend on JIT generic-code sharing.
            var method = new DynamicMethod("IronManVesselFlightComputerPolicy", typeof(bool),
                new[] { typeof(Vehicle), typeof(Enum) }, typeof(IronManFlightComputerPatches), true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, BasePolicyMethod());
            il.Emit(OpCodes.Ret);
            _basePolicy = method.CreateDelegate<Func<Vehicle, Enum, bool>>();

            Patch(harmony, typeof(GaugeCanvas), nameof(GaugeCanvas.IsContextVisible), nameof(ReplaceEvaContext));
            Patch(harmony, typeof(GaugeButtonFlightComputer), nameof(GaugeButtonFlightComputer.IsDisabled), nameof(ReplaceButtonPolicy));
            Patch(harmony, typeof(GaugeButtonFlightComputer), nameof(GaugeButtonFlightComputer.PackData), nameof(ReplaceButtonPolicy));
            _applied = true;
            Console.WriteLine("iron-man: native flight-computer gauges enabled for activated kittens");
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
        _basePolicy = null;
        Unpatch(harmony, typeof(GaugeCanvas), nameof(GaugeCanvas.IsContextVisible), nameof(ReplaceEvaContext));
        Unpatch(harmony, typeof(GaugeButtonFlightComputer), nameof(GaugeButtonFlightComputer.IsDisabled), nameof(ReplaceButtonPolicy));
        Unpatch(harmony, typeof(GaugeButtonFlightComputer), nameof(GaugeButtonFlightComputer.PackData), nameof(ReplaceButtonPolicy));
    }

    private static bool UsesVesselControls(Vehicle? vehicle) => _applied
        && vehicle is KittenEva kitten && IronManSubmod.Instance?.IsEnabled(kitten) == true;

    // These two substitutions change only the EVA/Vehicle predicates, leaving the canvas's
    // AND semantics, saved visibility, engine/target/burn/atmosphere/IVA conditions untouched.
    private static KittenEva? AsOrdinaryEva(Vehicle? vehicle) =>
        UsesVesselControls(vehicle) ? null : vehicle as KittenEva;

    private static bool IsButtonDisabled(Vehicle vehicle, Enum value) => UsesVesselControls(vehicle)
        ? _basePolicy!(vehicle, value)
        : vehicle.IsFlightComputerDisabled(value);

    internal static IEnumerable<CodeInstruction> ReplaceEvaContext(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        int replaced = 0;
        foreach (var code in codes)
        {
            if (code.opcode != OpCodes.Isinst || !Equals(code.operand, typeof(KittenEva))) continue;
            code.opcode = OpCodes.Call;
            code.operand = OwnMethod(nameof(AsOrdinaryEva));
            replaced++;
        }
        if (replaced != 2)
            throw new InvalidOperationException($"Iron Man expected two EVA/Vehicle gauge context checks, found {replaced}.");
        return codes;
    }

    internal static IEnumerable<CodeInstruction> ReplaceButtonPolicy(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        MethodInfo original = BasePolicyMethod();
        int replaced = 0;
        foreach (var code in codes)
        {
            if (!code.Calls(original)) continue;
            // The static adapter consumes the same receiver + Enum pair as callvirt.
            // Both click eligibility and packed GPU button state must use the same policy.
            code.opcode = OpCodes.Call;
            code.operand = OwnMethod(nameof(IsButtonDisabled));
            replaced++;
        }
        if (replaced != 1)
            throw new InvalidOperationException($"Iron Man expected one flight-computer button policy call, found {replaced}.");
        return codes;
    }

    private static MethodInfo BasePolicyMethod()
    {
        var method = AccessTools.DeclaredMethod(typeof(Vehicle), nameof(Vehicle.IsFlightComputerDisabled))
            ?? throw new MissingMethodException(typeof(Vehicle).FullName, nameof(Vehicle.IsFlightComputerDisabled));
        return method.MakeGenericMethod(typeof(Enum));
    }

    private static MethodInfo OwnMethod(string name) =>
        AccessTools.DeclaredMethod(typeof(IronManFlightComputerPatches), name)!;

    private static void Patch(Harmony harmony, Type type, string name, string transpiler)
    {
        var target = AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);
        harmony.Patch(target, transpiler: new HarmonyMethod(OwnMethod(transpiler)));
    }

    private static void Unpatch(Harmony harmony, Type type, string name, string transpiler)
    {
        var target = AccessTools.DeclaredMethod(type, name);
        if (target != null) harmony.Unpatch(target, OwnMethod(transpiler));
    }
}
