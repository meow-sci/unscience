using System;
using System.Linq;

/// <summary>
/// Real KSA.dll metadata for the seams the 5949 upgrade changed; no native game, renderer or GPU runs.
/// Each check pins the game behaviour the corresponding production fix depends on. Running with
/// <c>KSA_DLL_DIR</c> pointed at the 5901 DLL fails the first check (negative control).
/// </summary>
internal static class Ksa5949Checks
{
    internal static void Run()
    {
        using var probe = KsaAssemblyProbe.TryOpen("5949 serializer, rocket-feed and clutter checks");
        if (probe == null) return;

        // Iron Man anchors (5939): the internal recursive serializer gained an optional
        // EditorSnapshot.PreviousTransforms; IronManConnectorPatches resolves exactly this overload.
        string[] serialize = ["System.UInt32&", "KSA.PartInstance", "System.Boolean", "KSA.EditorSnapshot+PreviousTransforms"];
        var overloads = probe.ParameterTypes("KSA.Part", "GetReferenceWithChildren");
        Require(overloads.Count == 2 && overloads.Any(p => p.SequenceEqual(serialize)) && overloads.Any(p => p.SequenceEqual(["System.UInt32&"])),
            "Part.GetReferenceWithChildren has the public (ref uint) and internal (ref uint, PartInstance, bool, PreviousTransforms) overloads");
        Require(probe.CallsOf("KSA.Part", "GetReferenceWithChildren").Count(calls => calls.Contains("KSA.Part::GetReferenceWithChildren")) == 2,
            "both overloads reach the internal serializer (public wrapper and per-child recursion), so the postfix runs once per node");
        Require(probe.MethodsCalling("KSA.PartTree", "KSA.Part::GetReferenceWithChildren").Contains("Serialize"),
            "PartTree.Serialize (saves, copies, editor snapshots) serializes through the patched overload");

        // Blinky feed diagnostics (5925/5936): a gas-generator rocket fires only when both cores have
        // propellant, which RocketFeedCheck mirrors. Every other rocket type checks its single core.
        Require(probe.CallsOf("KSA.GasGeneratorRocket", "UpdatePropellantAvailability").Single()
                .Count(call => call == "KSA.RocketCore::ComputePropellantAvailable") == 2,
            "GasGeneratorRocket requires propellant on both its combustor and gas-generator cores");
        Require(probe.ReturnTypes("KSA.GasGeneratorRocket", "get_Cores").SequenceEqual(["System.ReadOnlySpan`1[KSA.RocketCore]"]),
            "GasGeneratorRocket exposes both cores through Rocket.Cores");

        // Pebbles residency (5919): the renderer resolves the clutter slot, then releases by slot.
        Require(probe.ParameterTypes("KSA.ClutterEcotypeRenderData", "ReleaseViewport").Single().SequenceEqual(["System.Int32"]),
            "ClutterEcotypeRenderData.ReleaseViewport takes the resolved clutter slot");
        Require(probe.CallsOf("KSA.GroundClutterRenderer", "OnViewportUnregistered").Single()
                .SkipWhile(call => call != "KSA.GroundClutterRenderer::HasViewportSlot")
                .SkipWhile(call => call != "KSA.GroundClutterRenderer::ResolveViewportSlot")
                .Contains("KSA.ClutterEcotypeRenderData::ReleaseViewport"),
            "native viewport release guards with HasViewportSlot, resolves the slot, then releases by slot, as Pebbles does");

        // Pebbles retirement (5919): per-ecotype GPU buffers and maps moved to GroundClutterGpuResources,
        // which ClutterRetirement now walks; the curvature buffer left TerrainData.Dispose.
        Require(probe.Fields("KSA.ClutterEcotypeRenderData").Any(f => f.Name == "Resources" && f.Type == "KSA.GroundClutterGpuResources"),
            "ClutterEcotypeRenderData owns its GPU buffers through GroundClutterGpuResources");
        Require(probe.Fields("KSA.GroundClutterGpuResources").Count(f => f.Type.StartsWith("Brutal.VulkanApi.Abstractions.MappedMemory")) >= 6
                && probe.Fields("KSA.ClutterCubeCellGrid").All(f => !f.Type.Contains("MappedMemory") && !f.Type.Contains("BufferEx")),
            "mapped memory and buffers live on GroundClutterGpuResources, not ClutterCubeCellGrid");
        Require(probe.CallsOf("KSA.GroundClutterTerrainData", "DisposePlanetResources").Single().Contains("Brutal.VulkanApi.Abstractions.BufferEx::Dispose")
                && probe.CallsOf("KSA.GroundClutterTerrainData", "Dispose").Single().SequenceEqual(["KSA.GroundClutterTerrainData::DestroyFrameResources"])
                && !probe.CallsOf("KSA.GroundClutterTerrainData", "DestroyFrameResources").Single().Contains("Brutal.VulkanApi.Abstractions.BufferEx::Dispose"),
            "the curvature buffer is freed by DisposePlanetResources, not by Dispose/DestroyFrameResources");

        // Scene saves across star-system switches (5916): Load hands the world to SaveSystemSwap, whose
        // queued switch runs Program.SwapSystem (reset prefix) and then ApplyLoaded (DeserializeSave before
        // OnGameLoaded). NativeSaveHooks binds RequestLoad's `apply`/`savedSystemId` by name.
        var load = probe.CallsOf("KSA.UncompressedSave", "Load").Single();
        Require(load.Contains("KSA.SaveSystemSwap::RequestLoad") && !load.Contains("KSA.Universe::DeserializeSave"),
            "UncompressedSave.Load defers the world load to SaveSystemSwap.RequestLoad");
        Require(probe.CallsOf("KSA.UncompressedSave", "ApplyLoaded").Single()
                .SkipWhile(call => call != "KSA.Universe::DeserializeSave").Contains("KSA.Program::OnGameLoaded"),
            "ApplyLoaded deserializes the save before Program.OnGameLoaded");
        Require(probe.ParameterTypes("KSA.SaveSystemSwap", "RequestLoad").Single().SequenceEqual(["System.String", "System.String", "System.Action"])
                && probe.ParameterNames("KSA.SaveSystemSwap", "RequestLoad").Single().SequenceEqual(["saveName", "savedSystemId", "apply"]),
            "SaveSystemSwap.RequestLoad(string saveName, string savedSystemId, Action apply) keeps the names Harmony binds");
        Require(probe.ReturnTypes("KSA.SaveSystemSwap", "PlanLoad").Contains("KSA.SaveLoadPlan"),
            "SaveSystemSwap.PlanLoad(string) decides the load plan the preflight mirrors");
        Require(probe.MethodsCalling("KSA.SaveSystemSwap", "KSA.Program::SwapSystem").SequenceEqual(["OnFrame"])
                && probe.MethodsCalling("KSA.Program", "KSA.Program::SwapSystem").Count == 0,
            "Program.SwapSystem is reached only from SaveSystemSwap.OnFrame");
        Require(probe.CallsOf("KSA.Program", "SwapSystem").Single()
                .SkipWhile(call => call != "KSA.JobSystems::WaitAll")
                .SkipWhile(call => call != "KSA.CelestialSystem::DestroyAllVehicles")
                .Contains("KSA.Universe::CreateSystem"),
            "SwapSystem joins workers, destroys the old vehicles, then creates the new system (reset prefix runs first)");

        Console.WriteLine("PASS: 5949 serializer overload, gas-generator feed rule, clutter slot release, GPU resource ownership and system-switch save seams");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }
}
