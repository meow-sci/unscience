using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Brutal.GlfwApi;
using HarmonyLib;
using KSA;
using RenderCore.Input;

namespace MeowSci.IronManLib;

/// <summary>Opted-in EVA instances use the existing vessel solver and input paths.</summary>
public static class IronManFlightPatches
{
    private static readonly HashSet<VehicleUpdateState> OwnedStates = new();
    private static bool _applied;

    public static void Apply(Harmony harmony)
    {
        if (_applied) return;
        try
        {
            // Reverse patches preserve the nonvirtual Vehicle implementation. Casting an EVA
            // to Vehicle and calling OnKey/ProcessInput would dispatch back into these prefixes.
            harmony.CreateReversePatcher(
                RequiredMethod(typeof(Vehicle), nameof(Vehicle.OnKey), typeof(GlfwKeyEvent)),
                new HarmonyMethod(RequiredMethod(typeof(IronManFlightPatches), nameof(VehicleOnKey))))
                .Patch();
            harmony.CreateReversePatcher(
                RequiredMethod(typeof(Vehicle), nameof(Vehicle.ProcessInput), typeof(InputAction), typeof(GlfwKeyAction), typeof(GlfwModifier)),
                new HarmonyMethod(RequiredMethod(typeof(IronManFlightPatches), nameof(VehicleProcessInput))))
                .Patch();

            harmony.Patch(RequiredMethod(typeof(VehicleUpdateState), nameof(VehicleUpdateState.PrepareFromVehicle)),
                postfix: new HarmonyMethod(typeof(IronManFlightPatches), nameof(PreparePostfix)));
            harmony.Patch(RequiredMethod(typeof(KittenEva), nameof(KittenEva.OnKey), typeof(GlfwKeyEvent)),
                prefix: new HarmonyMethod(typeof(IronManFlightPatches), nameof(OnKeyPrefix)));
            harmony.Patch(RequiredMethod(typeof(KittenEva), nameof(KittenEva.ProcessInput), typeof(InputAction), typeof(GlfwKeyAction), typeof(GlfwModifier)),
                prefix: new HarmonyMethod(typeof(IronManFlightPatches), nameof(ProcessInputPrefix)));
            IronManRenderPatches.Apply(harmony);
            _applied = true;
            Console.WriteLine("iron-man: flight patches applied (inactive until an EVA is enabled)");
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
        IronManRenderPatches.Remove(harmony);
        // PrepareFromVehicle snapshots are read by physics jobs. Never restore them in flight.
        if (OwnedStates.Count > 0)
        {
            JobSystems.VehicleSolver?.Wait();
            foreach (var state in OwnedStates) state.IsKitten = true;
            OwnedStates.Clear();
        }
        Unpatch(harmony, typeof(VehicleUpdateState), nameof(VehicleUpdateState.PrepareFromVehicle), nameof(PreparePostfix));
        Unpatch(harmony, typeof(KittenEva), nameof(KittenEva.OnKey), nameof(OnKeyPrefix));
        Unpatch(harmony, typeof(KittenEva), nameof(KittenEva.ProcessInput), nameof(ProcessInputPrefix));
    }

    private static bool IsEnabled(KittenEva kitten) =>
        _applied && IronManSubmod.Instance?.IsEnabled(kitten) == true;

    private static void PreparePostfix(VehicleUpdateState __instance)
    {
        if (__instance.ReadOnlyVehicle is not KittenEva kitten) return;
        try
        {
            if (IsEnabled(kitten))
            {
                // All modules, mass, propellant consumption and thrust already use Vehicle's
                // part tree. This flag alone selects away from the character servos, locomotion
                // and the ground-mode RCS input filter, retaining ordinary vessel physics.
                __instance.IsKitten = false;
                OwnedStates.Add(__instance);
            }
            else
            {
                // The original just restored IsKitten=true. Stop retaining the worker state.
                OwnedStates.Remove(__instance);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"iron-man: flight preparation failed: {ex.Message}");
        }
    }

    private static bool OnKeyPrefix(KittenEva __instance, GlfwKeyEvent keyEvent, ref bool __result)
    {
        if (!IsEnabled(__instance)) return true;
        try
        {
            __result = VehicleOnKey(__instance, keyEvent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"iron-man: vessel key handling failed: {ex.Message}");
            // Do not run a second handler after a partially queued input action.
            __result = true;
        }
        return false;
    }

    private static bool ProcessInputPrefix(KittenEva __instance, InputAction action, GlfwKeyAction keyAction, GlfwModifier modifiers)
    {
        if (!IsEnabled(__instance)) return true;
        try
        {
            VehicleProcessInput(__instance, action, keyAction, modifiers);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"iron-man: vessel input processing failed: {ex.Message}");
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool VehicleOnKey(Vehicle vehicle, GlfwKeyEvent keyEvent) =>
        throw new InvalidOperationException("iron-man: Vehicle.OnKey reverse patch was not installed");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VehicleProcessInput(Vehicle vehicle, InputAction action, GlfwKeyAction keyAction, GlfwModifier modifiers) =>
        throw new InvalidOperationException("iron-man: Vehicle.ProcessInput reverse patch was not installed");

    private static MethodInfo RequiredMethod(Type type, string name, params Type[] argumentTypes) =>
        (argumentTypes.Length == 0 ? AccessTools.Method(type, name) : AccessTools.Method(type, name, argumentTypes))
        ?? throw new MissingMethodException(type.FullName, name);

    private static void Unpatch(Harmony harmony, Type type, string name, string patchName)
    {
        var target = AccessTools.Method(type, name);
        var patch = AccessTools.Method(typeof(IronManFlightPatches), patchName);
        if (target != null && patch != null) harmony.Unpatch(target, patch);
    }
}
