using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MeowSci.FreeFallinLib;

/// <summary>
/// Descriptor-set indices that the stock mesh techniques bind the bindless texture library and the
/// material storage buffer at. Read from the live ModelPbr.frag, never hard-coded.
/// </summary>
internal readonly record struct CanopyDescriptorSets(int Texture, int Material)
{
    public override string ToString() => $"SET_TEXTURE {Texture}, SET_MATERIAL {Material}";
}

/// <summary>
/// Game-independent text transforms for the Full Canopy projection shaders. Every anchor and layout
/// assumption is checked here, so a changed game shader throws instead of compiling into a pipeline
/// whose descriptor sets no longer match.
/// </summary>
internal static class CanopyShaderSource
{
    internal const float MaterialMarker = 31415f;
    internal const string TextureSetDefine = "SET_TEXTURE";
    internal const string MaterialSetDefine = "SET_MATERIAL";

    // The pipeline's own set (scene UBO, joint matrices) is always set 0.
    private const int OwnSet = 0;
    private const int MaxSetIndex = 31;
    private const int ProjectionLocation = 3;

    // Multiline $ stops before LF, leaving CR to match explicitly for Windows shader files.
    private static readonly Regex DefinePattern = new(
        @"^[ \t]*#[ \t]*define[ \t]+(?<name>\w+)(?<value>[^\r\n]*)\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex LineComment = new(@"//[^\r\n]*", RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the texture and material set indices that a stock mesh fragment shader declares. Throws
    /// when either define is missing, conflicting, non-numeric or impossible for a mesh technique.
    /// </summary>
    internal static CanopyDescriptorSets ParseDescriptorSets(string source, string shaderName)
    {
        string code = StripComments(source);
        int texture = ReadSetDefine(code, TextureSetDefine, shaderName);
        int material = ReadSetDefine(code, MaterialSetDefine, shaderName);
        if (texture == material)
            throw new InvalidOperationException($"{shaderName} binds textures and materials at the same set {texture}; descriptor layout changed.");
        return new CanopyDescriptorSets(texture, material);
    }

    /// <summary>
    /// The patched Model_Skinned.vert is shared by every pipeline that pairs it with these fragment
    /// shaders, so all of them must agree with the layout read from ModelPbr.frag.
    /// </summary>
    internal static void RequireSameDescriptorSets(CanopyDescriptorSets expected, string partnerSource, string partnerName)
    {
        CanopyDescriptorSets actual = ParseDescriptorSets(partnerSource, partnerName);
        if (actual != expected)
            throw new InvalidOperationException($"{partnerName} declares {actual} but ModelPbr.frag declares {expected}; the shared skinned vertex shader cannot match both layouts.");
    }

    internal static string PatchStaticVertex(string source)
    {
        RequireFreeLocation(source, "out", "Model.vert");
        source = InsertAfter(source,
            "layout (location = 2) out flat ivec4 outInstanceID;",
            "layout (location = 3) out vec2 outFreeFallinUv;");
        return InsertAfter(source,
            "outWorldPosV = vec4(worldPos.xyz, inUv.y);",
            "    outFreeFallinUv = inUv;");
    }

    internal static string PatchSkinnedVertex(string source, CanopyDescriptorSets sets)
    {
        string code = StripComments(source);
        if (code.Contains(TextureSetDefine, StringComparison.Ordinal) || code.Contains(MaterialSetDefine, StringComparison.Ordinal)
            || code.Contains("MaterialBuffer", StringComparison.Ordinal))
            throw new InvalidOperationException("Model_Skinned.vert now declares its own texture/material sets; projection patch needs review.");
        RequireFreeLocation(source, "out", "Model_Skinned.vert");

        // TextureSet.glsl #errors without SET_TEXTURE, and MaterialSet.glsl includes it itself since
        // KSA 5530; both defines therefore precede the first include.
        source = InsertAfter(source, "#version 450",
            $"\n#define {TextureSetDefine} {sets.Texture}\n#include \"../Common/TextureSet.glsl\"\n\n" +
            $"#define {MaterialSetDefine} {sets.Material}\n#include \"../Common/MaterialSet.glsl\"");
        source = InsertAfter(source,
            "layout (location = 2) out flat ivec4 outInstanceID;",
            "layout (location = 3) out vec2 outFreeFallinUv;");
        return InsertAfter(source,
            "outWorldPosV = vec4(worldPos.xyz, inUv.y);",
            "    Material ffMaterial = materialData[outInstanceID.x];\n" +
            "    vec2 ffSource = vec2(inPos.x, -inPos.z) * ffMaterial.extraData.x;\n" +
            "    outFreeFallinUv = vec2(\n" +
            "        ffMaterial.extraData.y * ffSource.x - ffMaterial.extraData.z * ffSource.y,\n" +
            "        ffMaterial.extraData.z * ffSource.x + ffMaterial.extraData.y * ffSource.y) + vec2(0.5);");
    }

    internal static string PatchFragment(string source)
    {
        RequireFreeLocation(source, "in", "ModelPbr.frag");
        string marker = MaterialMarker.ToString("0.0", CultureInfo.InvariantCulture);
        source = InsertAfter(source,
            "layout (location = 2) in flat ivec4 inInstanceID;",
            "layout (location = 3) in vec2 inFreeFallinUv;");
        // Material IDs can differ across a draw's instances, so the indices are nonuniform as in MaterialSet.glsl.
        return InsertAfter(source,
            "GetMaterialData(materialID, uv, albedo, normalMap, roughness, metallic, ao, emissive);",
            $"    if (abs(materialData[materialID].extraData.w - {marker}) < 0.25)\n" +
            "    {\n" +
            "        Material ffMaterial = materialData[materialID];\n" +
            "        albedo = ffMaterial.albedoColor * texture(\n" +
            "            sampler2D(globalTextures[nonuniformEXT(ffMaterial.albedoIndex)], samplers[nonuniformEXT(ffMaterial.samplerIndex)]),\n" +
            "            inFreeFallinUv);\n" +
            "    }");
    }

    private static int ReadSetDefine(string code, string name, string shaderName)
    {
        int? value = null;
        foreach (Match match in DefinePattern.Matches(code))
        {
            if (!match.Groups["name"].Value.Equals(name, StringComparison.Ordinal)) continue;
            string text = match.Groups["value"].Value.Trim();
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
                throw new InvalidOperationException($"{shaderName} defines {name} as '{text}', not a set index.");
            if (value.HasValue && value.Value != parsed)
                throw new InvalidOperationException($"{shaderName} defines {name} as both {value.Value} and {parsed}.");
            value = parsed;
        }
        if (!value.HasValue)
            throw new InvalidOperationException($"{shaderName} no longer defines {name}; descriptor layout cannot be derived.");
        if (value.Value <= OwnSet || value.Value > MaxSetIndex)
            throw new InvalidOperationException($"{shaderName} defines {name} {value.Value}, outside the mesh technique's external sets.");
        return value.Value;
    }

    private static void RequireFreeLocation(string source, string direction, string shaderName)
    {
        var taken = new Regex($@"layout\s*\(\s*location\s*=\s*{ProjectionLocation}\s*\)\s*(?:(?:flat|smooth|noperspective)\s+)*{direction}\b",
            RegexOptions.CultureInvariant);
        if (taken.IsMatch(StripComments(source)))
            throw new InvalidOperationException($"{shaderName} already uses {direction} location {ProjectionLocation}; projection varying needs a new slot.");
    }

    private static string StripComments(string source) => LineComment.Replace(BlockComment.Replace(source, " "), "");

    private static string InsertAfter(string source, string anchor, string insertion)
    {
        int index = source.IndexOf(anchor, StringComparison.Ordinal);
        if (index < 0) throw new InvalidOperationException($"Shader changed shape; anchor not found: {anchor}");
        if (source.IndexOf(anchor, index + anchor.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"Shader changed shape; anchor is no longer unique: {anchor}");
        index += anchor.Length;
        return source.Insert(index, "\n" + insertion);
    }
}
