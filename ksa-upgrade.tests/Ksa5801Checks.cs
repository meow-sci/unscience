using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MeowSci.HumbleArteestLib;

/// <summary>Real DLL metadata and production shader transforms; no native game or GPU execution.</summary>
internal static class Ksa5801Checks
{
    internal static void Run()
    {
        using var probe = KsaAssemblyProbe.TryOpen("5801 reflection, paint and layout checks");
        if (probe == null) return;
        var fields = new Dictionary<string, string[]>
        {
            ["Camera"] = ["_fovRadians"],
            ["KittenEva"] = ["_renderable"],
            ["KittenRenderable"] = ["_characterAvatar", "_groundIdleAnim", "_groundWalkAnim", "_groundRunAnim",
                "_ladderAnim", "_jumpIntroAnim", "_flailAnim", "_jumpLandAnim", "_moonWalkAnim", "_moonRunAnim",
                "_swimAnim", "_swimIdleAnim", "_seatedIdleAnim", "_seatedIdleActionAnims", "_walkPairSampler",
                "_runPairSampler", "_swimPairSampler", "_blendSampler", "_catPersonalityExpressionAnim",
                "_catExpressionAnim", "_catEyeAnim", "_catEarAnim"],
            ["CharacterAvatar"] = ["Core", "Fur", "Attachments"],
            ["CharacterAvatar+CharacterCore"] = ["Scale", "CharacterModel"],
            ["CatExpressionAnim"] = ["_expressionPose"],
            ["ChuteRenderable"] = ["_renderable"],
            ["AnimatedRenderable"] = ["MaterialIndices", "RuntimeAnim"],
            ["BoneAnimRuntime"] = ["TimeSinceTransition", "CurrentAnimation"],
            ["StaticMeshRenderable"] = ["MaterialIndices"],
            ["CatFurRenderable"] = ["MaterialIndices"],
            ["LightModule+TemplateData"] = ["Intensity", "ColorRgb"],
            ["ModLibrary"] = ["AllParts", "AllCharacters", "AllMeshes", "AllFiles", "AllGltfs", "AllMaterials", "AllExhaustTemplates"],
            ["ShaderReference"] = ["<Shader>k__BackingField"],
            ["TextureReference"] = ["<TextureAsset>k__BackingField"],
            ["MeshReference"] = ["<HostPrimitives>k__BackingField", "<PrimitiveMaterialIds>k__BackingField"],
            ["PartTreeRenderData"] = ["_batches", "_dynamicBatches", "_glassBatches"],
            ["PartTreeRenderData+Batch"] = ["Model", "Parts", "Count", "StateBitFlags"],
            ["PartTreeRenderData+DynamicBatch"] = ["Model", "Parts", "Count", "StateBitFlags"],
            ["PartTreeRenderData+GlassBatch"] = ["Model", "Parts", "Count"],
            ["DistantSphereRenderer"] = ["_material"],
            ["PlanetTransparenciesRenderer"] = ["_anyRings", "_ringRendererCreated", "_ringsRenderer"],
            ["Program"] = ["_planetTransparenciesRenderer"],
            ["NarrowPhaseCallbacks"] = ["Sim"],
            ["GpuObjectSystem`1"] = ["<BigBuffer>k__BackingField", "DeviceCtx", "BigBufferAllocator"],
            ["AssetManager`1"] = ["AssetMap"],
            ["DataReference`1"] = ["Value"],
        };
        foreach (var (type, members) in fields)
        {
            var names = probe.Fields("KSA." + type).Select(f => f.Name).ToHashSet();
            foreach (string name in members) Require(names.Contains(name), $"KSA.{type}.{name} resolves");
        }
        var kittenRenderCalls = probe.CallsOf("KSA.KittenRenderable", "UpdateRenderData").Single();
        int locomotion = kittenRenderCalls.IndexOf("KSA.KittenRenderable::UpdateLocomotionAnimationState");
        int condition = kittenRenderCalls.IndexOf("KSA.KittenRenderable::UpdateConditionPose");
        int pose = kittenRenderCalls.IndexOf("KSA.AnimatedRenderable::UpdateAnimation");
        Require(locomotion >= 0 && condition > locomotion && pose > condition,
            "kitten pose prefix runs after native locomotion and condition selection for all rendered kittens");
        var nativeBits = probe.Fields("KSA.PartTreeRenderData+StateBit");
        Require(nativeBits.Count > 0 && nativeBits.All(f => f.Constant is { } bit && (bit & ~0x1fff) == 0),
            "all native state flags fit below the paint encoding");
        Require(nativeBits.Any(f => f.Name == "HIGHLIGHT_YELLOW" && f.Constant == 2048) &&
                nativeBits.Any(f => f.Name == "HIGHLIGHT_PURPLE" && f.Constant == 4096), "5801 habitat highlight flags exist");
        Require(probe.Fields("KSA.MaterialData").Select(f => f.Type).SequenceEqual(new[]
            { "System.Int32", "System.Int32", "System.Int32", "System.Int32", "Brutal.Numerics.float4",
              "Brutal.Numerics.float4", "Brutal.Numerics.float4", "System.Int32", "System.Int32", "System.Int32", "System.Int32" }),
            "GPU MaterialData retains its 80-byte sequential field shape with albedo at byte 16");
        Require(probe.CallsOf("KSA.GameSaves", "get_UniverseSerializer").Count == 1 &&
                probe.CallsOf("KSA.VehicleSaves", "get_VehicleSerializer").Count == 1, "native serializer getters exist");
        NativeKillOrdering(probe);
        LiveShaders();
        Console.WriteLine("PASS: 5801 reflection watchlist, native paint flags, material layout and live paint shader transforms");
    }

    private static void NativeKillOrdering(KsaAssemblyProbe probe)
    {
        var calls = probe.CallsOf("KSA.Program", "PrepareFrame").Single();
        int apply = calls.IndexOf("KSA.Universe::ApplyVehicleSolvers");
        int input = calls.IndexOf("KSA.InputEvents::ApplyInputEvents");
        int refresh = calls.IndexOf("KSA.Program::RefreshVehiclesInFrame");
        int handoff = calls.IndexOf("KSA.Universe::GetJobSimStep");
        Require(apply >= 0 && input > apply && refresh > input && handoff > refresh,
            "native destroy input commits after physics results and before render-list refresh; the old DOH handoff is too late");
        Require(probe.Fields("KSA.InputEvents+VehicleDestroyData").Select(f => (f.Name, f.Type))
            .SequenceEqual(new[] { ("Vehicle", "KSA.Vehicle"), ("Recovered", "System.Boolean") }),
            "native destroy input retains exact vehicle and recovery disposition fields");
        Require(probe.CallsOf("KSA.InputEvents+VehicleDestroyData", "Apply").Single()
                .Contains("KSA.Universe::DestroyVehicle")
            && probe.CallsOf("KSA.Vehicle", "KillDyingCrew").Single().Contains("KSA.Universe::DestroyVehicle"),
            "the native input action and normal EVA crew deaths share the game's teardown");
    }

    private static void LiveShaders()
    {
        string content = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(KsaAssemblyProbe.KsaDllPath())!, "..", "Content"));
        foreach (string name in new[] { "MeshIndirect.frag", "MeshIndirectRaytraced.frag" })
        {
            string source = File.ReadAllText(Path.Combine(content, "Core", "Shaders", "Mesh", name));
            Require(VehiclePaintShaderSource.Prepare(source, true, out var stockError) == source && stockError == null,
                "stock diagnostic keeps shader text byte-for-byte unchanged");
            foreach (PaintBlendMode mode in Enum.GetValues<PaintBlendMode>())
            {
                VehiclePaint.BlendMode = mode;
                foreach (string input in new[] { source, source.Replace("\r\n", "\n").Replace("\n", "\r\n") })
                {
                    string? patched = VehiclePaintShaderSource.Inject(input, out var error);
                    Require(patched != null, $"{name}/{mode}: {error}");
                    Require(patched!.Contains("inStateFlags >> 13u") && patched.Contains("0x3Fu"), "shader decodes the CPU paint format");
                }
            }
        }
        Require(VehiclePaintShaderSource.Inject("void main() {}", out _) == null, "missing shader anchors fail closed");
        VehiclePaint.BlendMode = PaintBlendMode.Multiply;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }
}
