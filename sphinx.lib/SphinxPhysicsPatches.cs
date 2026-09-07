using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepuPhysics;
using BepuPhysics.Collidables;
using HarmonyLib;
using KSA;

namespace MeowSci.SphinxLib;

internal static class SphinxPhysicsPatches
{
    public static void Apply(Harmony harmony)
    {
        Patch(typeof(ConstraintSim), nameof(ConstraintSim.BeginStaticObjectPass), nameof(Sync));
        Patch(typeof(ConstraintSim), nameof(ConstraintSim.UpdateSimForSnappedOrigin), nameof(Sync));
        Patch(typeof(ConstraintSim), nameof(ConstraintSim.IsGroundSurfaceFor), nameof(Ground));
        Patch(typeof(ConstraintSim), nameof(ConstraintSim.TryResetForPool), nameof(Clear), prefix: true);
        Patch(typeof(ConstraintSim), nameof(ConstraintSim.Dispose), nameof(Clear), prefix: true);
        var callback = typeof(ConstraintSim).Assembly.GetType("KSA.NarrowPhaseCallbacks", throwOnError: true)!;
        var filter = AccessTools.Method(callback, "AllowContactGeneration",
            new[] { typeof(int), typeof(CollidableReference), typeof(CollidableReference), typeof(float).MakeByRefType() })
            ?? throw new MissingMethodException(callback.FullName, "AllowContactGeneration");
        harmony.Patch(filter, transpiler: new HarmonyMethod(typeof(SphinxPhysicsPatches), nameof(ContactFilter)));

        void Patch(Type type, string name, string hook, bool prefix = false)
        {
            var target = AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
            var method = new HarmonyMethod(typeof(SphinxPhysicsPatches), hook);
            harmony.Patch(target, prefix: prefix ? method : null, postfix: prefix ? null : method);
        }
    }
    public static void Remove(Harmony harmony)
    {
        foreach (var original in Harmony.GetAllPatchedMethods().ToArray())
        {
            var patches = Harmony.GetPatchInfo(original);
            if (patches == null) continue;
            foreach (var patch in patches.Prefixes.Concat(patches.Postfixes).Concat(patches.Transpilers)
                .Where(p => p.owner == harmony.Id && p.PatchMethod.DeclaringType == typeof(SphinxPhysicsPatches)))
                harmony.Unpatch(original, patch.PatchMethod);
        }
    }
    private static void Sync(ConstraintSim __instance) => SphinxSubmod.Instance?.SyncCollision(__instance);
    private static void Clear(ConstraintSim __instance) => SphinxPhysics.Clear(__instance);
    private static void Ground(ConstraintSim __instance, StaticHandle handle, ref bool __result)
        => __result |= SphinxPhysics.Owns(__instance, handle);

    private static IEnumerable<CodeInstruction> ContactFilter(IEnumerable<CodeInstruction> input, MethodBase __originalMethod)
    {
        var original = AccessTools.Method(typeof(BepuHandles), nameof(BepuHandles.IsGroundSurface));
        var replacement = AccessTools.Method(typeof(SphinxPhysicsPatches), nameof(IsSurface));
        var sim = AccessTools.Field(__originalMethod.DeclaringType, "Sim")
            ?? throw new MissingFieldException("KSA.NarrowPhaseCallbacks", "Sim");
        int replaced = 0;
        foreach (var instruction in input)
        {
            if (!instruction.Calls(original)) { yield return instruction; continue; }
            var load = new CodeInstruction(OpCodes.Ldarg_0);
            load.MoveLabelsFrom(instruction); load.MoveBlocksFrom(instruction);
            yield return load;
            yield return new CodeInstruction(OpCodes.Ldfld, sim);
            yield return new CodeInstruction(OpCodes.Call, replacement);
            replaced++;
        }
        if (replaced != 1) throw new InvalidOperationException("Sphinx expected one stock ground filter call; collision hooks need a game-update review.");
    }
    private static bool IsSurface(ref BepuHandles handles, StaticHandle handle, ConstraintSim sim)
        => handles.IsGroundSurface(handle) || SphinxPhysics.Owns(sim, handle);
}
