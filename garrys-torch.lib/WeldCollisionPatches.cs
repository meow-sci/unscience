using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using BepuPhysics;
using BepuPhysics.Collidables;
using HarmonyLib;
using KSA;

namespace MeowSci.GarrysTorchLib;

/// <summary>Keep welded sources simulated, but omit their shapes from rigid-body collision passes.</summary>
internal static class WeldCollisionPatches
{
    // Published only at the result/snapshot handoff. Workers never read mutable welds or UI lists.
    private static HashSet<Vehicle> _sources = new(ReferenceEqualityComparer.Instance);
    private static readonly MethodInfo Prefix = AccessTools.Method(typeof(WeldCollisionPatches), nameof(BeforePass));
    private static readonly MethodInfo Finalizer = AccessTools.Method(typeof(WeldCollisionPatches), nameof(AfterPass));
    private static readonly MethodInfo[] Targets =
    {
        AccessTools.Method(typeof(ConstraintSim), nameof(ConstraintSim.DetectCollisions), new[] { typeof(double) }),
        AccessTools.Method(typeof(ConstraintSim), nameof(ConstraintSim.Simulate),
            new[] { typeof(double), typeof(SimStep).MakeByRefType() }),
    };

    public static void Apply(Harmony harmony)
    {
        try
        {
            foreach (var target in Targets)
            {
                if (target == null)
                    throw new MissingMethodException("KSA ConstraintSim collision pass signature changed.");
                harmony.Patch(target, prefix: new HarmonyMethod(Prefix), finalizer: new HarmonyMethod(Finalizer));
            }
        }
        catch
        {
            Remove(harmony);
            throw;
        }
    }

    public static void Remove(Harmony harmony)
    {
        Clear();
        foreach (var target in Targets)
        {
            if (target == null) continue;
            harmony.Unpatch(target, Prefix);
            harmony.Unpatch(target, Finalizer);
        }
    }

    public static void Publish(IReadOnlyList<WeldEntry> welds)
    {
        var sources = new HashSet<Vehicle>(ReferenceEqualityComparer.Instance);
        foreach (var weld in welds)
            if (weld.WeldEnabled && !weld.Collisions && !weld.Source.IsDisposed && !weld.Target.IsDisposed
                && weld.Source.Parent == weld.Target.Parent)
                sources.Add(weld.Source);
        Volatile.Write(ref _sources, sources);
    }

    public static void Clear() =>
        Volatile.Write(ref _sources, new HashSet<Vehicle>(ReferenceEqualityComparer.Instance));

    private static void BeforePass(ConstraintSim __instance, out List<(BodyHandle Body, TypedIndex Shape)>? __state)
    {
        __state = null;
        var sources = Volatile.Read(ref _sources);
        if (sources.Count == 0) return;

        // These entry points run on the bubble's owning worker before Bepu dispatches its workers.
        // SetShape updates the broad phase; changing Collidable.Shape directly would leave ghosts.
        foreach (var (handle, state) in __instance.HandleToState)
        {
            if (!sources.Contains(state.ReadOnlyVehicle)) continue;
            var body = __instance.Simulation.Bodies[handle];
            var shape = body.Collidable.Shape;
            if (!shape.Exists) continue;
            (__state ??= new()).Add((handle, shape));
            body.SetShape(default);
        }
    }

    private static void AfterPass(ConstraintSim __instance, List<(BodyHandle Body, TypedIndex Shape)>? __state)
    {
        if (__state == null) return;
        // Harmony finalizers also run on exceptions, including partially completed prefixes.
        // Preserve the actual shape, including animated/scaled compounds; never dispose its data.
        foreach (var (handle, shape) in __state)
        {
            try
            {
                var body = __instance.Simulation.Bodies[handle];
                body.SetShape(shape);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"garrys-torch: failed to restore collision shape: {ex}");
            }
        }
    }
}
