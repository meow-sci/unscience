using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Brutal.ShaderCApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using HarmonyLib;
using KSA;
using RenderCore;
using RenderCore.Shaders;
using RenderCore.Mesh;

namespace MeowSci.PebblesLib;

internal static class ClutterHooks
{
    private static ClutterController? _controller;
    private static readonly Dictionary<ClutterEcotypeRenderData, ClutterResources> Owned = [];
    [ThreadStatic] private static ClutterResources? _context;
    public static IDisposable Enter(ClutterResources resources)
    { var old = _context; _context = resources; return new Scope(() => _context = old); }
    private sealed class Scope(Action leave) : IDisposable { public void Dispose() => leave(); }
    public static void Track(ClutterResources resources) { foreach (var render in resources.Render) Owned.Add(render, resources); }
    public static void Forget(ClutterResources resources) { foreach (var render in resources.Render) Owned.Remove(render); }

    public static void Apply(Harmony harmony, ClutterController controller)
    {
        if (_controller != null && !ReferenceEquals(_controller, controller)) throw new InvalidOperationException("Pebbles already owns a runtime bridge.");
        _controller = controller;
        foreach (var type in new[] { typeof(GroundClutterPlacementData), typeof(ClutterEcotypeRenderData), typeof(ClutterEcotypePhysicalData), typeof(ClutterCubeCellGrid), typeof(ClutterViewResources), typeof(SimpleVkMeshAtlas) })
            foreach (var constructor in type.GetConstructors()) harmony.Patch(constructor, prefix: Method(nameof(BeforeConstruct)));
        // Patch callers rather than tiny getters, which the JIT may inline.
        foreach (var name in new[] { "SortMaterialIds", "CreateColorRenderer", "CreateDepthPrePassRenderer", "CreateShadowDepthRenderer" })
            harmony.Patch(Required(typeof(ClutterEcotypeRenderData), name), transpiler: Method(nameof(MaterialCalls)));
        harmony.Patch(Required(typeof(Universe), nameof(Universe.ExecuteNextClothSolvers)), prefix: Method(nameof(BeforeCloth)));
        harmony.Patch(Required(typeof(GroundClutterRenderer), nameof(GroundClutterRenderer.RebuildFrameResources)), postfix: Method(nameof(RebuildOriginals)));
        harmony.Patch(Required(typeof(GroundClutterRenderer), nameof(GroundClutterRenderer.Dispose)), prefix: Method(nameof(BeforeDispose)));
        var serializeSave = AccessTools.Method(typeof(GroundClutterRenderer), nameof(GroundClutterRenderer.SerializeSave), [typeof(List<ClutterEcotypeSaveData>)])
            ?? throw new MissingMethodException(typeof(GroundClutterRenderer).FullName, nameof(GroundClutterRenderer.SerializeSave));
        harmony.Patch(serializeSave, postfix: Method(nameof(AfterSerializeSave)));
        harmony.Patch(Required(typeof(ClutterEcotypeRenderData), nameof(ClutterEcotypeRenderData.RebuildFrameResources)), prefix: Method(nameof(BeforeRebuild)), finalizer: Method(nameof(AfterRebuild)));
        harmony.Patch(Required(typeof(ShaderReference), nameof(ShaderReference.CompileVariantWithCustomOptions)), prefix: Method(nameof(CompileColor)));
    }
    public static void Remove(Harmony harmony, ClutterController controller)
    {
        if (ReferenceEquals(_controller, controller)) { controller.Dispose(); _controller = null; }
        // Main shares one Harmony owner across all submods. Remove only our methods.
        foreach (var original in Harmony.GetAllPatchedMethods().ToArray())
        {
            var patches = Harmony.GetPatchInfo(original);
            if (patches == null) continue;
            foreach (var patch in patches.Prefixes.Concat(patches.Postfixes)
                .Concat(patches.Transpilers).Concat(patches.Finalizers)
                .Where(p => p.owner == harmony.Id && p.PatchMethod.DeclaringType == typeof(ClutterHooks)))
                harmony.Unpatch(original, patch.PatchMethod);
        }
    }
    private static MethodInfo Required(Type type, string name) => AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
    private static HarmonyMethod Method(string name) => new(Required(typeof(ClutterHooks), name)) { priority = Priority.First };

    private static IEnumerable<CodeInstruction> MaterialCalls(IEnumerable<CodeInstruction> input, MethodBase __originalMethod)
    {
        var buffer = AccessTools.PropertyGetter(typeof(GroundClutterRenderer), nameof(GroundClutterRenderer.MaterialBuffer));
        var index = Required(typeof(GroundClutterRenderer), nameof(GroundClutterRenderer.GetMaterialIndex));
        var replaced = 0;
        foreach (var instruction in input)
        {
            if (instruction.Calls(buffer)) { instruction.opcode = OpCodes.Call; instruction.operand = Required(typeof(ClutterHooks), nameof(Buffer)); replaced++; }
            else if (instruction.Calls(index)) { instruction.opcode = OpCodes.Call; instruction.operand = Required(typeof(ClutterHooks), nameof(Index)); replaced++; }
            yield return instruction;
        }
        if (replaced != 1) throw new InvalidOperationException($"Pebbles expected one material binding in {__originalMethod.Name}, found {replaced}. Game build is incompatible.");
    }
    private static BufferEx Buffer(GroundClutterRenderer renderer)
        => ReferenceEquals(_context?.Owner, renderer) ? _context.MaterialBuffer!.Value : renderer.MaterialBuffer;
    private static uint Index(GroundClutterRenderer renderer, GroundClutterMaterialReference material)
        => ReferenceEquals(_context?.Owner, renderer) ? _context.MaterialIndices[material.Hash] : renderer.GetMaterialIndex(material);
    private static void BeforeConstruct(object __instance) => _context?.Constructed.Add(__instance);
    private static void BeforeCloth()
    { try { _controller?.Process(); } catch (Exception ex) { _controller?.Report(ex); } }
    private static void RebuildOriginals(GroundClutterRenderer __instance, DeviceEx __0) => _controller?.RebuildOriginals(__instance, __0);
    private static void BeforeDispose(GroundClutterRenderer __instance) => _controller?.RendererDisposing(__instance);
    private static void AfterSerializeSave(GroundClutterRenderer __instance, List<ClutterEcotypeSaveData> __0)
    { try { _controller?.WriteNativeSave(__instance, __0); } catch (Exception ex) { _controller?.Report(ex); } }
    private static void BeforeRebuild(ClutterEcotypeRenderData __instance, out IDisposable? __state)
        => __state = Owned.TryGetValue(__instance, out var resources) ? Enter(resources) : null;
    private static Exception? AfterRebuild(Exception? __exception, IDisposable? __state) { __state?.Dispose(); return __exception; }

    private static bool CompileColor(ShaderReference __instance, CompileOptions __0, ref VkShaderModule? __result)
    {
        if (_context == null || _context.Graph.SourceColorMaterials.Count == 0 || __instance.Id != "ClutterSolidFrag") return true;
        var source = File.ReadAllText(__instance.ModPath);
        const string marker = "diffuseSample.rgb *= (groundColor / averageLuminosity);";
        if (source.Split(marker).Length != 2) throw new InvalidOperationException("Clutter color shader changed; source-color adaptation needs review.");
        source = source.Replace(marker, marker + "\n    if ((materialData.flags & 0x80000000u) != 0u) diffuseSample.rgb = pow(texture(sampler2D(globalTextures[materialData.diffuseTextureId], textureSampler), inUv).rgb, vec3((materialData.flags & 0x40000000u) != 0u ? 1.0 : 2.2));", StringComparison.Ordinal);
        ShaderCompilerResolve.SetIncludeCallback(__0);
        __result = ShaderModuleUtils.FromString(Program.GetRenderer().Device, Encoding.UTF8.GetBytes(source), VkShaderStageFlags.FragmentBit, __0, Encoding.UTF8.GetBytes(__instance.ModPath + "\0"));
        return false;
    }
}
