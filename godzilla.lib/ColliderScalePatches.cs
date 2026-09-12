using System;
using System.Collections.Generic;
using System.Reflection;
using BepuPhysics.Collidables;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace MeowSci.GodzillaLib;

/// <summary>Independent collider dimensions/centers, with the physical bounds kept in their own channel.</summary>
public static class ColliderScalePatches
{
    private sealed record Bounds(Box Box, float3 Center, float Radius);
    private static readonly Dictionary<ColliderModule, ColliderScaleState> Modules = new();
    private static readonly Dictionary<Vehicle, ColliderScaleState> Vehicles = new();
    private static readonly List<(MethodInfo Target, MethodInfo Patch)> Installed = new();
    private static Action<Vehicle>? _rebuild;
    [ThreadStatic] private static bool _preserveBounds;
    public static bool IsApplied { get; private set; }

    public static void Apply(Harmony harmony)
    {
        if (IsApplied) return;
        try
        {
            var rebuild = AccessTools.Method(typeof(Vehicle), "UpdateCollisionGeometry")
                ?? throw new MissingMethodException(typeof(Vehicle).FullName, "UpdateCollisionGeometry");
            Patch(harmony, AccessTools.Method(typeof(ColliderModule), nameof(ColliderModule.SetScale)), nameof(ScalePrefix));
            Patch(harmony, AccessTools.Method(typeof(ColliderModule), nameof(ColliderModule.SetScale)), nameof(ScaleFinalizer), finalizer: true);
            Patch(harmony, AccessTools.PropertyGetter(typeof(ColliderModule), nameof(ColliderModule.PositionVehicleAsmb)),
                nameof(PositionPostfix), postfix: true);
            Patch(harmony, rebuild, nameof(BoundsPrefix));
            Patch(harmony, rebuild, nameof(BoundsFinalizer), finalizer: true);
            // Create after patching so this open delegate enters the wrapped method.
            _rebuild = AccessTools.MethodDelegate<Action<Vehicle>>(rebuild);
            IsApplied = true;
        }
        catch { Remove(harmony); throw; }
    }

    public static void Remove(Harmony harmony)
    {
        foreach (var vehicle in new List<Vehicle>(Vehicles.Keys)) Clear(vehicle);
        IsApplied = false;
        foreach (var (target, patch) in Installed) harmony.Unpatch(target, patch);
        Installed.Clear();
        _rebuild = null;
    }

    internal static void Set(ColliderScaleState state)
    {
        if (!IsApplied) throw new InvalidOperationException("Independent collider scaling patches are unavailable.");
        Vehicles.Add(state.Vehicle, state);
        foreach (var module in state.Modules) Modules.Add(module, state);
        state.RefreshShapes();
        Rebuild(state.Vehicle);
    }

    internal static void Clear(Vehicle vehicle)
    {
        if (!Vehicles.TryGetValue(vehicle, out var state)) return;
        foreach (var module in state.Modules) Modules.Remove(module);
        if (vehicle.IsDisposed) { Vehicles.Remove(vehicle); return; }
        // On staging, only touch modules that still belong to this live tree. Detached parts are
        // left at their last shape size until their new owner refreshes or disposes them.
        var current = new HashSet<ColliderModule>(vehicle.Parts.Modules.Get<ColliderModule>().ToArray());
        foreach (var module in state.Modules)
        {
            if (!current.Contains(module)) continue;
            var scale = new ScaleFactors(module.Parent.ScaleTotal);
            module.SetScale(in scale);
            module.NeedsColliderUpdate = true;
        }
        Rebuild(vehicle);
        Vehicles.Remove(vehicle); // Keep restoration state available if a native rebuild throws.
    }

    internal static bool TopologyMatches(Vehicle vehicle)
    {
        if (!Vehicles.TryGetValue(vehicle, out var state)) return true;
        var current = vehicle.Parts.Modules.Get<ColliderModule>();
        return current.Length == state.Modules.Length &&
            new HashSet<ColliderModule>(current.ToArray()).SetEquals(state.Modules);
    }

    internal static void Forget(Vehicle vehicle)
    {
        if (!Vehicles.Remove(vehicle, out var state)) return;
        foreach (var module in state.Modules) Modules.Remove(module);
    }

    private static void Rebuild(Vehicle vehicle)
    {
        bool previous = _preserveBounds;
        try { _preserveBounds = true; _rebuild!(vehicle); }
        finally { _preserveBounds = previous; }
        // Rebuilding clears these flags. Republish them so the worker's UpdateShape returns true
        // and BodyReference.UpdateBounds refreshes the broad phase even for a stationary body.
        foreach (var collider in vehicle.Parts.Modules.Get<ColliderModule>()) collider.NeedsColliderUpdate = true;
    }

    private static void Patch(Harmony harmony, MethodInfo? target, string name, bool postfix = false, bool finalizer = false)
    {
        if (target == null) throw new MissingMethodException($"Godzilla collider patch target for {name} is missing.");
        var method = AccessTools.Method(typeof(ColliderScalePatches), name);
        Installed.Add((target, method));
        var patch = new HarmonyMethod(method);
        harmony.Patch(target, prefix: !postfix && !finalizer ? patch : null,
            postfix: postfix ? patch : null, finalizer: finalizer ? patch : null);
    }

    private static void ScalePrefix(ColliderModule __instance, ref ScaleFactors scale, out ScaleFactors? __state)
    {
        __state = null;
        if (!Modules.TryGetValue(__instance, out var state)) return;
        __state = scale;
        scale = state.GetScale(__instance);
    }

    private static void ScaleFinalizer(ref ScaleFactors scale, ScaleFactors? __state)
    {
        // Part.RefreshScale passes one readonly local to every IRescale module. Restoring it is
        // essential: leaking our collider override would rescale the following mass/fuel modules.
        if (__state.HasValue) scale = __state.Value;
    }

    private static void PositionPostfix(ColliderModule __instance, ref double3 __result)
    {
        if (Modules.TryGetValue(__instance, out var state)) __result = state.GetPosition(__instance);
    }

    private static void BoundsPrefix(Vehicle __instance, out Bounds? __state)
    {
        __state = null;
        if (!_preserveBounds && !Vehicles.ContainsKey(__instance)) return;
        var props = __instance.GetPhysicsStatesMutable().Props;
        __state = new(props.BoundingBoxAsmb, props.GeometricCenterAsmb, props.BoundingSphereRadiusBody);
    }

    private static void BoundsFinalizer(Vehicle __instance, Bounds? __state)
    {
        if (__state == null) return;
        ref var props = ref __instance.GetPhysicsStatesMutable().Props;
        props.BoundingBoxAsmb = __state.Box;
        props.GeometricCenterAsmb = __state.Center;
        props.BoundingSphereRadiusBody = __state.Radius;
    }
}
