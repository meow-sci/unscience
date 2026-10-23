using System;
using System.IO;
using MeowSci.FreeFallinLib;

/// <summary>
/// Full Canopy descriptor-set derivation (KSA 5638). Rev 5530 renumbered the mesh-technique sets, so
/// the skinned vertex patch must take SET_TEXTURE/SET_MATERIAL from the game's ModelPbr.frag and fail
/// closed when they cannot be read.
/// </summary>
internal static class FreeFallinShaderChecks
{
    // Verbatim header of KSA 2026.10.19.5638 Content/Core/Shaders/Mesh/ModelPbr.frag (lines 1-42).
    private const string ModelPbr5638Header = """
        // Version directive for GLSL shaders
        #version 450

        #define SET_TEXTURE 5
        #define FRAGMENT_SHADER

        #include "../Common/SharedFrag.glsl"

        #define SET_GLOBAL 4
        #include "../Common/Global.glsl"

        #define SET_MATERIAL 1
        #include "../Common/MaterialSet.glsl"

        // TODO: Why is this not shared?
        #define SET_CSM 2
        #include "../Lighting/CascadedShadows.glsl"
        #include "../Common/CelestialShadows.glsl"

        #define SET_LIGHTING 3
        #include "../Common/Lighting.glsl"
        #include "../Common/SpecularAA.glsl"
        #include "../Lighting/PunctualLighting.glsl"

        //#include "Shared/Tonemapping.glsl"

        #define SET_AO 6
        layout (set = SET_AO, binding = 0) uniform sampler2D aoTexture;

        #ifdef RAYTRACED_REFLECTIONS
        // Receive-only raytraced output. Bindings must match RaytraceRasterizer.RaytracedOutputDescriptorSetLayout
        #define SET_RAYTRACED_OUTPUT 7
        layout (set = SET_RAYTRACED_OUTPUT, binding = 0) uniform sampler2D samplerRaytracedIndirectLighting;
        layout (set = SET_RAYTRACED_OUTPUT, binding = 1) uniform sampler2D samplerRaytracedReflections;
        layout (set = SET_RAYTRACED_OUTPUT, binding = 2) uniform sampler2D samplerRaytracedDirectLighting;
        #endif

        layout (location = 0) in vec4 inNormalU;   // xyz = normal, w = uv.x
        layout (location = 1) in vec4 inWorldPosV; // xyz = worldPos, w = uv.y
        layout (location = 2) in flat ivec4 inInstanceID;

        layout (location = 0) out vec4 outFragColor;
        """;

    // KSA 2026.9.22.5482 ordering of the same defines (texture 1, material 2).
    private const string ModelPbr5482Defines = """
        #version 450
        #define SET_GLOBAL 5
        #define SET_TEXTURE 1
        #include "../Common/TextureSet.glsl"
        #define SET_MATERIAL 2
        #include "../Common/MaterialSet.glsl"
        """;

    // Minimal skinned vertex shape carrying exactly the anchors the patch uses.
    private const string SkinnedVertex = """
        #version 450
        layout (location = 0) in vec3 inPos;
        layout (location = 2) in vec2 inUv;
        layout (location = 3) in ivec4 inJointIndices;
        layout (location = 2) out flat ivec4 outInstanceID;
        void main()
        {
            vec4 worldPos = vec4(inPos, 1.0);
            outWorldPosV = vec4(worldPos.xyz, inUv.y);
        }
        """;

    private static int _checks;

    internal static void Run()
    {
        _checks = 0;
        Require(Parse(ModelPbr5638Header) == new CanopyDescriptorSets(5, 1), "5638 ModelPbr.frag yields texture set 5, material set 1");
        Require(Parse(ModelPbr5482Defines) == new CanopyDescriptorSets(1, 2), "5482 ordering yields texture set 1, material set 2");

        RequireThrows(() => Parse(ModelPbr5638Header.Replace("#define SET_TEXTURE 5", "")), "missing SET_TEXTURE fails closed");
        RequireThrows(() => Parse(ModelPbr5638Header.Replace("#define SET_MATERIAL 1", "")), "missing SET_MATERIAL fails closed");
        RequireThrows(() => Parse(ModelPbr5638Header.Replace("#define SET_MATERIAL 1", "// #define SET_MATERIAL 1")), "commented-out define is ignored");
        RequireThrows(() => Parse(ModelPbr5638Header.Replace("#define SET_TEXTURE 5", "/* #define SET_TEXTURE 5 */")), "block-commented define is ignored");
        RequireThrows(() => Parse(ModelPbr5638Header + "\n#define SET_MATERIAL 2\n"), "conflicting definitions fail closed");
        RequireThrows(() => Parse(ModelPbr5638Header.Replace("#define SET_TEXTURE 5", "#define SET_TEXTURE (SET_GLOBAL + 1)")), "non-numeric set fails closed");
        RequireThrows(() => Parse(ModelPbr5638Header.Replace("#define SET_TEXTURE 5", "#define SET_TEXTURE 1")), "shared texture/material set fails closed");
        RequireThrows(() => Parse(ModelPbr5638Header.Replace("#define SET_MATERIAL 1", "#define SET_MATERIAL 0")), "pipeline-own set 0 fails closed");
        Require(Parse(ModelPbr5638Header + "\n#define SET_MATERIAL 1\n") == new CanopyDescriptorSets(5, 1), "repeated identical define is accepted");

        var sets = new CanopyDescriptorSets(5, 1);
        CanopyShaderSource.RequireSameDescriptorSets(sets, ModelPbr5638Header, "ModelNormal.frag");
        RequireThrows(() => CanopyShaderSource.RequireSameDescriptorSets(sets, ModelPbr5482Defines, "ModelNormal.frag"), "partner fragment with another layout fails closed");

        string patched = CanopyShaderSource.PatchSkinnedVertex(SkinnedVertex, sets);
        Require(patched.Contains("#define SET_TEXTURE 5\n#include \"../Common/TextureSet.glsl\"", StringComparison.Ordinal)
            && patched.Contains("#define SET_MATERIAL 1\n#include \"../Common/MaterialSet.glsl\"", StringComparison.Ordinal)
            && !patched.Contains("SET_MATERIAL 2", StringComparison.Ordinal),
            "skinned vertex receives the derived defines");
        Require(patched.IndexOf("#define SET_TEXTURE", StringComparison.Ordinal) < patched.IndexOf("#include", StringComparison.Ordinal),
            "SET_TEXTURE precedes the first include");
        RequireThrows(() => CanopyShaderSource.PatchSkinnedVertex(SkinnedVertex.Replace("#version 450", "#version 450\n#define SET_MATERIAL 1"), sets),
            "skinned vertex that already declares sets fails closed");
        RequireThrows(() => CanopyShaderSource.PatchSkinnedVertex(SkinnedVertex + "\nlayout (location = 3) out vec4 outOther;", sets),
            "occupied varying location 3 fails closed");
        RequireThrows(() => CanopyShaderSource.PatchSkinnedVertex(SkinnedVertex.Replace("outWorldPosV = vec4(worldPos.xyz, inUv.y);", ""), sets),
            "missing assignment anchor fails closed");
        RequireThrows(() => CanopyShaderSource.PatchFragment(ModelPbr5638Header), "fragment without the GetMaterialData anchor fails closed");

        RunLiveShaders();
        Console.WriteLine($"PASS: free-fallin shader layout ({_checks} checks)");
    }

    /// <summary>Patches the real game shaders when a KSA Content tree is reachable from KSAFolder.</summary>
    private static void RunLiveShaders()
    {
        string? mesh = FindMeshShaders();
        if (mesh == null)
        {
            Console.WriteLine("SKIP: free-fallin live shader check (no KSA Content/Core/Shaders/Mesh beside KSAFolder)");
            return;
        }

        string Read(string name) => File.ReadAllText(Path.Combine(mesh, name));
        CanopyDescriptorSets live = CanopyShaderSource.ParseDescriptorSets(Read("ModelPbr.frag"), "ModelPbr.frag");
        CanopyShaderSource.RequireSameDescriptorSets(live, Read("ModelNormal.frag"), "ModelNormal.frag");
        CanopyShaderSource.RequireSameDescriptorSets(live, Read("ModelTranslucent.frag"), "ModelTranslucent.frag");
        CanopyShaderSource.PatchStaticVertex(Read("Model.vert"));
        string skinned = CanopyShaderSource.PatchSkinnedVertex(Read("Model_Skinned.vert"), live);
        CanopyShaderSource.PatchFragment(Read("ModelPbr.frag"));
        Require(skinned.Contains($"#define SET_MATERIAL {live.Material}", StringComparison.Ordinal), "live skinned vertex uses the live material set");
        Console.WriteLine($"free-fallin: live {mesh} -> {live}");
    }

    private static string? FindMeshShaders()
    {
        string? root = Environment.GetEnvironmentVariable("KSAFolder");
        if (string.IsNullOrWhiteSpace(root)) return null;
        foreach (string content in new[] { Path.Combine(root, "Content"), Path.Combine(root, "..", "Content") })
        {
            string mesh = Path.GetFullPath(Path.Combine(content, "Core", "Shaders", "Mesh"));
            if (File.Exists(Path.Combine(mesh, "ModelPbr.frag"))) return mesh;
        }
        return null;
    }

    private static CanopyDescriptorSets Parse(string source) => CanopyShaderSource.ParseDescriptorSets(source, "ModelPbr.frag");

    private static void RequireThrows(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { _checks++; return; }
        throw new Exception("expected failure: " + message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
}
