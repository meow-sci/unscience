using System;
using System.Reflection;
using System.Text;
using Brutal.ShaderCApi;
using Brutal.VulkanApi;
using HarmonyLib;
using KSA;
using RenderCore;

namespace MeowSci.HumbleArteestLib;

/// <summary>
/// Harmony patches behind the vehicle paint feature. Three seams, all inert until
/// <see cref="VehiclePaintShaders.Installed"/> is true:
///
/// 1. <c>ShaderModuleUtils.FromFile</c> — compiles the paint-patched GLSL instead of the file on
///    disk for the part fragment shaders. This is the only interception point that works on
///    KSA 4693+, where part pipelines recompile per feature variant straight from disk and never
///    consult <c>ShaderReference.Shader</c>.
///
/// 2. <c>PartModelModule/PartModelDynamicModule.UpdateRenderData</c> — records which
///    <c>Part</c> is about to submit an instance. The normal render-data paths consume this
///    hand-off immediately; thumbnail calls have no pending part and remain unpainted.
///
/// 3. <c>PartModel/PartModelDynamic.AddInstance</c> — ORs the packed paint color into the free
///    high bits of <c>StateBitFlag</c> on its way to the GPU.
/// </summary>
public static class VehiclePaintPatches
{
    /// <summary>Part whose instance data is about to be submitted; set by (2), consumed by (3).</summary>
    [ThreadStatic] private static Part? _pendingPart;

    /// <summary>Number of seams this feature needs; anything less means paint is degraded.</summary>
    public const int RequiredPatchCount = 5;

    private static readonly PatchRecord[] Records = new PatchRecord[RequiredPatchCount];
    private static int _recordCount;

    /// <summary>How many of the required seams are currently patched.</summary>
    public static int AppliedPatchCount => _recordCount;

    // ---- Apply / remove ----

    public static void Apply(Harmony harmony)
    {
        _recordCount = 0;

        Patch(harmony, ResolveFromFile(), nameof(FromFilePrefix), "ShaderModuleUtils.FromFile");
        Patch(harmony, AccessTools.Method(typeof(PartModelModule), nameof(PartModelModule.UpdateRenderData)),
            nameof(PartModelModulePrefix), "PartModelModule.UpdateRenderData");
        Patch(harmony, AccessTools.Method(typeof(PartModelDynamicModule), nameof(PartModelDynamicModule.UpdateRenderData)),
            nameof(PartModelDynamicModulePrefix), "PartModelDynamicModule.UpdateRenderData");
        Patch(harmony, ResolveAddInstance(typeof(PartModel), typeof(PartModel.PerInstanceData)),
            nameof(AddInstancePrefix), "PartModel.AddInstance");
        Patch(harmony, ResolveAddInstance(typeof(PartModelDynamic), typeof(PartModelDynamic.PerInstanceData)),
            nameof(AddInstanceDynamicPrefix), "PartModelDynamic.AddInstance");

        Console.WriteLine($"humble-arteest: VehiclePaint patches applied ({_recordCount}/{RequiredPatchCount})");
    }

    public static void Remove(Harmony harmony)
    {
        for (int i = 0; i < _recordCount; i++)
        {
            try { harmony.Unpatch(Records[i].Original, Records[i].Patch); }
            catch (Exception ex) { Console.WriteLine($"humble-arteest: unpatch failed for {Records[i].Label}: {ex.Message}"); }
        }
        _recordCount = 0;
        _pendingPart = null;
        Console.WriteLine("humble-arteest: VehiclePaint patches removed");
    }

    private static void Patch(Harmony harmony, MethodBase? original, string prefixName, string label)
    {
        try
        {
            if (original == null)
            {
                Console.WriteLine($"humble-arteest: WARNING — {label} not found; paint will be incomplete");
                return;
            }

            var prefix = typeof(VehiclePaintPatches).GetMethod(prefixName,
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(nameof(VehiclePaintPatches), prefixName);

            harmony.Patch(original, prefix: new HarmonyMethod(prefix));
            Records[_recordCount++] = new PatchRecord(original, prefix, label);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"humble-arteest: WARNING — could not patch {label}: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves the four-argument <c>FromFile</c> overload (the one that takes compile options);
    /// the two-argument overload delegates to it.
    /// </summary>
    private static MethodBase? ResolveFromFile() =>
        AccessTools.Method(typeof(ShaderModuleUtils), nameof(ShaderModuleUtils.FromFile), new[]
        {
            typeof(Device),
            typeof(string),
            typeof(VkShaderStageFlags).MakeByRefType(),
            typeof(CompileOptions?),
        });

    /// <summary>
    /// Resolves the method that actually appends an instance to the per-viewport lists. Since KSA
    /// 5438 added a dent-aware public wrapper, resolving by name would be ambiguous and could patch
    /// only one wrapper. Both wrappers call this private four-argument method, so one prefix sees
    /// each submission exactly once and the native <c>PerInstanceDent</c> remains untouched.
    /// </summary>
    private static MethodBase? ResolveAddInstance(Type modelType, Type instanceDataType)
    {
        MethodBase? submission = AccessTools.Method(modelType, nameof(PartModel.AddInstance), new[]
        {
            instanceDataType, typeof(KSA.Deformation.PerInstanceDent), typeof(IViewport), typeof(int)
        });
        return submission?.IsPrivate == true ? submission : null;
    }

    // ---- (1) Shader compilation ----

    /// <summary>
    /// Compiles the paint-patched source for part fragment shaders. The original file path is
    /// handed to the compiler as the input file name so relative <c>#include</c>s resolve exactly
    /// as they do stock, and the caller's <c>CompileOptions</c> (which carry the
    /// <c>ENABLE_EMISSIVE</c>/<c>ENABLE_TEMPERATURE</c>/... variant defines) pass straight through.
    /// Any failure falls back to compiling the untouched file.
    /// </summary>
    private static bool FromFilePrefix(Device device, string filePath, ref VkShaderStageFlags shaderStage,
        CompileOptions? options, ref VkShaderModule __result)
    {
        byte[]? source;
        try
        {
            source = VehiclePaintShaders.TryGetPatchedSource(filePath);
        }
        catch (Exception ex)
        {
            VehiclePaintShaders.NoteCompileFailed(filePath, ex);
            return true;
        }

        if (source == null) return true;

        try
        {
            var stage = ShaderModuleUtils.ShaderStageFromFileExtension(filePath);
            __result = ShaderModuleUtils.FromString(device, source, stage, options, NullTerminated(filePath));
            shaderStage = stage;
            VehiclePaintShaders.NoteCompiled();
            return false;
        }
        catch (Exception ex)
        {
            VehiclePaintShaders.NoteCompileFailed(filePath, ex);
            return true;
        }
    }

    /// <summary>The compiler takes the input file name as a C string, so terminate it explicitly.</summary>
    private static byte[] NullTerminated(string value)
    {
        var utf8 = new UTF8Encoding(false);
        var bytes = new byte[utf8.GetByteCount(value) + 1];
        utf8.GetBytes(value, 0, value.Length, bytes, 0);
        return bytes;
    }

    // ---- (2) Part hand-off ----

    private static void PartModelModulePrefix(PartModelModule __instance)
    {
        if (!VehiclePaintShaders.Installed) return;
        _pendingPart = __instance.Parent;
    }

    private static void PartModelDynamicModulePrefix(PartModelDynamicModule __instance)
    {
        if (!VehiclePaintShaders.Installed) return;
        _pendingPart = __instance.Parent;
    }

    // ---- (3) Per-instance paint ----

    private static void AddInstancePrefix(ref PartModel.PerInstanceData instanceData)
    {
        if (!TryTakePaintBits(out int bits)) return;
        instanceData.StateBitFlag |= bits;
    }

    private static void AddInstanceDynamicPrefix(ref PartModelDynamic.PerInstanceData inInstanceData)
    {
        if (!TryTakePaintBits(out int bits)) return;
        inInstanceData.StateBitFlag |= bits;
    }

    /// <summary>
    /// Consumes the pending part and resolves its paint. Clearing the slot here keeps a part from
    /// leaking into an unrelated submission if the game ever gains another AddInstance caller.
    /// </summary>
    private static bool TryTakePaintBits(out int bits)
    {
        var part = _pendingPart;
        _pendingPart = null;

        if (part == null)
        {
            bits = 0;
            return false;
        }

        return VehiclePaint.TryGetPaintBits(part, out bits);
    }

    private readonly struct PatchRecord
    {
        public readonly MethodBase Original;
        public readonly MethodInfo Patch;
        public readonly string Label;

        public PatchRecord(MethodBase original, MethodInfo patch, string label)
        {
            Original = original;
            Patch = patch;
            Label = label;
        }
    }
}
