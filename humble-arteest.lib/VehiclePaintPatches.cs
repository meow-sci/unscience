using System;
using System.Reflection;
using System.Runtime.CompilerServices;
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
/// 2. private <c>PartTreeRenderData.WriteState/WriteDynamicState</c> — since KSA 5482 the only
///    writers of each tree's cached per-slot <c>StateBitFlags</c> for static and dynamic part
///    models. A postfix ORs the packed paint color into the free high bits, so raster and
///    raytraced IVA submissions both carry it. Thumbnails do not use these caches and stay unpainted.
///
/// 3. <c>PartTreeRenderData.EnsureBuilt</c> — the cache is only rewritten when dirty, so this prefix
///    invalidates a tree's states whenever <see cref="VehiclePaint.RenderStateVersion"/> changed.
/// </summary>
public static class VehiclePaintPatches
{
    /// <summary>Number of seams this feature needs; anything less means paint is degraded.</summary>
    public const int RequiredPatchCount = 4;

    private static readonly ConditionalWeakTable<PartTreeRenderData, StrongBox<int>> SeenVersions = new();
    private static AccessTools.FieldRef<object, int[]>? _staticStateFlags;
    private static AccessTools.FieldRef<object, int[]>? _dynamicStateFlags;

    private static readonly PatchRecord[] Records = new PatchRecord[RequiredPatchCount];
    private static int _recordCount;

    /// <summary>How many of the required seams are currently patched.</summary>
    public static int AppliedPatchCount => _recordCount;

    // ---- Apply / remove ----

    public static void Apply(Harmony harmony)
    {
        _recordCount = 0;

        Patch(harmony, ResolveFromFile(), nameof(FromFilePrefix), "ShaderModuleUtils.FromFile");
        Patch(harmony, ResolveWriteState("Batch", "WriteState", typeof(Part), out _staticStateFlags),
            nameof(WriteStatePostfix), "PartTreeRenderData.WriteState", postfix: true);
        Patch(harmony, ResolveWriteState("DynamicBatch", "WriteDynamicState", typeof(PartModelDynamicModule),
                out _dynamicStateFlags),
            nameof(WriteDynamicStatePostfix), "PartTreeRenderData.WriteDynamicState", postfix: true);
        Patch(harmony, AccessTools.Method(typeof(PartTreeRenderData), nameof(PartTreeRenderData.EnsureBuilt),
                new[] { typeof(PartTree), typeof(ulong) }),
            nameof(EnsureBuiltPrefix), "PartTreeRenderData.EnsureBuilt");

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
        Console.WriteLine("humble-arteest: VehiclePaint patches removed");
    }

    private static void Patch(Harmony harmony, MethodBase? original, string patchName, string label,
        bool postfix = false)
    {
        try
        {
            if (original == null)
            {
                Console.WriteLine($"humble-arteest: WARNING — {label} not found; paint will be incomplete");
                return;
            }

            var patch = typeof(VehiclePaintPatches).GetMethod(patchName,
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(nameof(VehiclePaintPatches), patchName);

            if (postfix) harmony.Patch(original, postfix: new HarmonyMethod(patch));
            else harmony.Patch(original, prefix: new HarmonyMethod(patch));
            Records[_recordCount++] = new PatchRecord(original, patch, label);
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
    /// Resolves a private <c>PartTreeRenderData</c> state writer <c>(batch, int slot, owner)</c> and
    /// the <c>StateBitFlags</c> array of its private nested batch type. Null when either is missing.
    /// </summary>
    private static MethodBase? ResolveWriteState(string batchTypeName, string writerName, Type ownerType,
        out AccessTools.FieldRef<object, int[]>? stateFlags)
    {
        stateFlags = null;
        Type? batchType = AccessTools.Inner(typeof(PartTreeRenderData), batchTypeName);
        if (batchType == null || AccessTools.Field(batchType, "StateBitFlags")?.FieldType != typeof(int[])) return null;
        stateFlags = AccessTools.FieldRefAccess<int[]>(batchType, "StateBitFlags");
        return AccessTools.Method(typeof(PartTreeRenderData), writerName, new[] { batchType, typeof(int), ownerType });
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

    // ---- (2) Cached per-slot paint ----

    // The batch parameters are private nested types; Harmony binds them as object.
    private static void WriteStatePostfix(object inBatch, int inSlot, Part inPart)
    {
        if (VehiclePaint.TryGetPaintBits(inPart, out int bits))
            _staticStateFlags!(inBatch)[inSlot] |= bits;
    }

    private static void WriteDynamicStatePostfix(object inBatch, int inSlot, PartModelDynamicModule inModule)
    {
        if (VehiclePaint.TryGetPaintBits(inModule.Parent, out int bits))
            _dynamicStateFlags!(inBatch)[inSlot] |= bits;
    }

    // ---- (3) Cache invalidation ----

    /// <summary>
    /// Rewrites a tree's cached states once after any paint change. A tree seen for the first time
    /// is invalidated too, because its cache may predate the patches or the latest change.
    /// </summary>
    private static void EnsureBuiltPrefix(PartTreeRenderData __instance)
    {
        int version = VehiclePaint.RenderStateVersion;
        if (SeenVersions.TryGetValue(__instance, out var seen))
        {
            if (seen.Value == version) return;
            seen.Value = version;
        }
        else
        {
            SeenVersions.Add(__instance, new StrongBox<int>(version));
        }
        __instance.InvalidateStates();
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
