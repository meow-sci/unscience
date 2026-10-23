using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Brutal.ShaderCApi;
using Brutal.VulkanApi;
using HarmonyLib;
using KSA;
using KSA.Rendering;
using RenderCore;
using RenderCore.Pipelines;

namespace MeowSci.FreeFallinLib;

/// <summary>
/// Adds one optional bind-pose projection varying to KSA's model PBR shaders. The path is selected
/// by a marker in MaterialData.ExtraData, so every non-Free-Fallin material remains stock. Text
/// transforms live in <see cref="CanopyShaderSource"/>; this class owns the game seams.
/// </summary>
internal static class CanopyProjectionShaders
{
    internal const float MaterialMarker = CanopyShaderSource.MaterialMarker;

    private const string StaticVertex = "Model.vert";
    private const string SkinnedVertex = "Model_Skinned.vert";
    private const string PbrFragment = "ModelPbr.frag";
    private const string LayoutSourceId = "ModelPbrFrag";

    private static readonly string[] ShaderIds = { "ModelVert", "ModelSkinnedVert", LayoutSourceId };
    // Fragment shaders that KSA also pairs with ModelSkinnedVert (skinned pre-pass, kitten eyes). The
    // patched vertex shader joins their pipelines, so their declared sets must agree with ModelPbr.frag.
    private static readonly string[] PartnerFragmentIds = { "ModelNormalFrag", "ModelTranslucentFrag" };
    private static readonly Dictionary<string, byte[]> SourceCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly MethodInfo FromFile = AccessTools.Method(typeof(ShaderModuleUtils), nameof(ShaderModuleUtils.FromFile),
        new[] { typeof(Device), typeof(string), typeof(VkShaderStageFlags).MakeByRefType(), typeof(CompileOptions?) })
        ?? throw new MissingMethodException(typeof(ShaderModuleUtils).FullName, nameof(ShaderModuleUtils.FromFile));
    private static readonly MethodInfo SetShaderFromMod = AccessTools.Method(typeof(Utils), nameof(Utils.SetShaderFromMod),
        new[] { typeof(SimpleShaderStages), typeof(Device), typeof(string), typeof(bool) })
        ?? throw new MissingMethodException(typeof(Utils).FullName, nameof(Utils.SetShaderFromMod));
    private static readonly MethodInfo FromFilePatch = AccessTools.Method(typeof(CanopyProjectionShaders), nameof(FromFilePrefix))!;
    private static readonly MethodInfo SetShaderFromModPatch = AccessTools.Method(typeof(CanopyProjectionShaders), nameof(SetShaderFromModPrefix))!;

    private static bool _skinnedVertexCompiled;
    private static bool _pbrFragmentCompiled;
    private static CanopyDescriptorSets _descriptorSets;

    internal static bool Available { get; private set; }
    internal static string? LastError { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        SourceCache.Clear();
        LastError = null;
        _skinnedVertexCompiled = false;
        _pbrFragmentCompiled = false;
        Available = ValidateSources();
        if (!Available)
        {
            Console.WriteLine($"free-fallin: full-canopy projection unavailable (stock shaders untouched): {LastError}");
            return;
        }

        harmony.Patch(FromFile, prefix: new HarmonyMethod(FromFilePatch));
        harmony.Patch(SetShaderFromMod, prefix: new HarmonyMethod(SetShaderFromModPatch));
        Program.RendererRebuildNeeded = true;
        Console.WriteLine("free-fallin: full-canopy projection shaders armed; renderer rebuild requested");
    }

    internal static void Remove(Harmony harmony)
    {
        if (!Available) return;
        Available = false;
        SourceCache.Clear();
        _skinnedVertexCompiled = false;
        _pbrFragmentCompiled = false;
        harmony.Unpatch(FromFile, FromFilePatch);
        harmony.Unpatch(SetShaderFromMod, SetShaderFromModPatch);
        Program.RendererRebuildNeeded = true;
        Console.WriteLine("free-fallin: full-canopy projection shaders removed; renderer rebuild requested");
    }

    internal static void RequireAvailable()
    {
        if (!Available)
            throw new InvalidOperationException(LastError ?? "Full-canopy projection shaders are unavailable on this KSA build.");
        if (!_skinnedVertexCompiled || !_pbrFragmentCompiled)
            throw new InvalidOperationException("Full-canopy projection shaders have not reached the active flight renderer. Check the console for shader rebuild errors.");
    }

    /// <summary>
    /// Stock model pipelines normally reuse cached ShaderReference modules when rebuilt. Force only
    /// our three targets through CompileVariantWithCustomOptions so FromFilePrefix can transform them.
    /// </summary>
    private static void SetShaderFromModPrefix(string modId, ref bool useCustomOptions)
    {
        if (!Available) return;
        foreach (string shaderId in ShaderIds)
        {
            if (!shaderId.Equals(modId, StringComparison.OrdinalIgnoreCase)) continue;
            useCustomOptions = true;
            return;
        }
    }

    private static bool FromFilePrefix(Device device, string filePath, ref VkShaderStageFlags shaderStage,
        CompileOptions? options, ref VkShaderModule __result)
    {
        if (!Available || !IsTarget(filePath)) return true;
        try
        {
            byte[] source = GetPatchedSource(filePath);
            VkShaderStageFlags stage = ShaderModuleUtils.ShaderStageFromFileExtension(filePath);
            __result = ShaderModuleUtils.FromString(device, source, stage, options, NullTerminated(filePath));
            shaderStage = stage;
            string name = Path.GetFileName(filePath);
            if (name.Equals(SkinnedVertex, StringComparison.OrdinalIgnoreCase)) _skinnedVertexCompiled = true;
            if (name.Equals(PbrFragment, StringComparison.OrdinalIgnoreCase)) _pbrFragmentCompiled = true;
            Console.WriteLine($"free-fallin: compiled full-canopy shader {name}");
            return false;
        }
        catch (Exception ex)
        {
            LastError = $"Patched {Path.GetFileName(filePath)} failed to compile: {ex.Message}";
            Console.WriteLine($"free-fallin: {LastError}; using the stock shader");
            return true;
        }
    }

    /// <summary>
    /// Derives the texture/material sets from the live ModelPbr.frag and pre-patches all three targets.
    /// Any failure leaves Available false, so no prefix is installed and every stock shader is untouched.
    /// </summary>
    private static bool ValidateSources()
    {
        try
        {
            _descriptorSets = ResolveDescriptorSets();
            foreach (string shaderId in ShaderIds)
            {
                string path = ResolveShaderPath(shaderId);
                SourceCache[path] = PatchSource(path, File.ReadAllText(path));
            }
            Console.WriteLine($"free-fallin: full-canopy projection uses {_descriptorSets} from {PbrFragment}");
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            SourceCache.Clear();
            return false;
        }
    }

    private static CanopyDescriptorSets ResolveDescriptorSets()
    {
        CanopyDescriptorSets sets = CanopyShaderSource.ParseDescriptorSets(
            File.ReadAllText(ResolveShaderPath(LayoutSourceId)), PbrFragment);
        foreach (string partnerId in PartnerFragmentIds)
        {
            string path = ResolveShaderPath(partnerId);
            CanopyShaderSource.RequireSameDescriptorSets(sets, File.ReadAllText(path), Path.GetFileName(path));
        }
        return sets;
    }

    private static string ResolveShaderPath(string shaderId)
    {
        string path = ModLibrary.Get<ShaderReference>(shaderId).ModPath;
        if (!File.Exists(path)) throw new FileNotFoundException($"Could not locate shader '{shaderId}'.", path);
        return path;
    }

    private static bool IsTarget(string path)
    {
        string name = Path.GetFileName(path);
        return name.Equals(StaticVertex, StringComparison.OrdinalIgnoreCase)
               || name.Equals(SkinnedVertex, StringComparison.OrdinalIgnoreCase)
               || name.Equals(PbrFragment, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] GetPatchedSource(string path)
    {
        if (SourceCache.TryGetValue(path, out byte[]? source)) return source;
        source = PatchSource(path, File.ReadAllText(path));
        SourceCache[path] = source;
        return source;
    }

    private static byte[] PatchSource(string path, string source)
    {
        string name = Path.GetFileName(path);
        string patched = name switch
        {
            StaticVertex => CanopyShaderSource.PatchStaticVertex(source),
            SkinnedVertex => CanopyShaderSource.PatchSkinnedVertex(source, _descriptorSets),
            PbrFragment => CanopyShaderSource.PatchFragment(source),
            _ => throw new InvalidOperationException($"Unsupported shader target '{name}'.")
        };
        return new UTF8Encoding(false).GetBytes(patched);
    }

    private static byte[] NullTerminated(string value)
    {
        byte[] result = new byte[Encoding.UTF8.GetByteCount(value) + 1];
        Encoding.UTF8.GetBytes(value, 0, value.Length, result, 0);
        return result;
    }
}
