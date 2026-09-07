using System;
using System.Reflection;
using Brutal.VulkanApi;
using HarmonyLib;
using KSA;

namespace MeowSci.SphinxLib;

/// <summary>Submit private meshes while KSA's native static-object pipeline/state is bound.</summary>
public static class SphinxPatches
{
    public static bool Ready { get; private set; }
    private static MethodInfo Method(string name, params Type[] parameters) =>
        AccessTools.Method(typeof(StaticObjectRenderer), name, parameters)
        ?? throw new MissingMethodException(typeof(StaticObjectRenderer).FullName, name);
    private static MethodInfo Color() => Method("WriteCommandsColor", typeof(CommandBuffer), typeof(IViewport), typeof(int), typeof(VkPipeline), typeof(StaticObjectModel.DrawBucket));
    private static MethodInfo Prepare() => Method(nameof(StaticObjectRenderer.UpdateRenderData), typeof(IViewport), typeof(int));
    private static MethodInfo Prepass() => Method(nameof(StaticObjectRenderer.WriteCommandsPrePass), typeof(CommandBuffer), typeof(IViewport), typeof(int));
    public static void Apply(Harmony harmony)
    {
        var prepare = Prepare(); var color = Color(); var prepass = Prepass();
        try
        {
            harmony.Patch(prepare, postfix: new HarmonyMethod(typeof(SphinxPatches), nameof(AfterPrepare)));
            harmony.Patch(color, postfix: new HarmonyMethod(typeof(SphinxPatches), nameof(AfterColor)));
            harmony.Patch(prepass, postfix: new HarmonyMethod(typeof(SphinxPatches), nameof(AfterPrepass)));
            Ready = true;
        }
        catch { Remove(harmony); throw; }
    }
    public static void Remove(Harmony harmony)
    {
        Ready = false;
        harmony.Unpatch(Prepare(), AccessTools.Method(typeof(SphinxPatches), nameof(AfterPrepare)));
        harmony.Unpatch(Color(), AccessTools.Method(typeof(SphinxPatches), nameof(AfterColor)));
        harmony.Unpatch(Prepass(), AccessTools.Method(typeof(SphinxPatches), nameof(AfterPrepass)));
    }
    private static void AfterPrepare(IViewport viewport, int frameIndex) => SphinxSubmod.Instance?.Prepare(viewport, frameIndex);
    private static void AfterColor(CommandBuffer commandBuffer, IViewport viewport, int frameIndex, StaticObjectModel.DrawBucket bucket)
    {
        // Opaque and blended calls always bind complete game lighting state when a nearby body
        // exists. The terrain variant has a different shader and must never submit these meshes.
        if (viewport.GetCamera().NearbyCelestial == null || bucket == StaticObjectModel.DrawBucket.OpaqueTerrain) return;
        SphinxSubmod.Instance?.Record(commandBuffer, viewport, frameIndex, false, bucket == StaticObjectModel.DrawBucket.Blended);
    }
    private static void AfterPrepass(CommandBuffer commandBuffer, IViewport viewport, int frameIndex) =>
        SphinxSubmod.Instance?.Record(commandBuffer, viewport, frameIndex, true, false);
}
