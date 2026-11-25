using System;
using System.Linq;

/// <summary>
/// Real KSA.dll metadata for the seams the 5901 upgrade changed; no native game, renderer or GPU runs.
/// Each check pins the game behaviour the corresponding production fix depends on.
/// </summary>
internal static class Ksa5901Checks
{
    internal static void Run()
    {
        using var probe = KsaAssemblyProbe.TryOpen("5901 engine, connector, RCS-cache, clutter and crew-clock checks");
        if (probe == null) return;

        // Blinky diagnostics (5889): controllers hold Rockets; Blinky enumerates each rocket's cores through
        // Rocket.Cores. KSA 5936 made Rocket abstract and removed its single Core field, so pin the span
        // getter Blinky actually binds rather than the removed field.
        var controller = probe.Fields("KSA.EngineController");
        Require(controller.Any(f => f.Name == "Rockets" && f.Type == "KSA.Rocket[]") && controller.All(f => f.Name != "Cores"),
            "EngineController exposes Rockets (KSA.Rocket[]) instead of Cores");
        Require(probe.ReturnTypes("KSA.Rocket", "get_Cores").SequenceEqual(["System.ReadOnlySpan`1[KSA.RocketCore]"]),
            "Rocket.Cores exposes the rocket's cores as ReadOnlySpan<RocketCore>");

        // Iron Man anchors (5866): both load paths drop saved connector indices beyond the template
        // count before construction; IronManStaleConnectorGuard patches this exact method.
        Require(probe.CallsOf("KSA.PartInstance", "DropStaleConnectors").Count == 1, "PartInstance.DropStaleConnectors has one overload");
        Require(probe.MethodsCalling("KSA.CelestialSystem", "KSA.PartInstance::DropStaleConnectors").Contains("DeserializeSave"),
            "world load drops stale connectors in CelestialSystem.DeserializeSave");
        Require(probe.MethodsCalling("KSA.UncompressedVehicleSave", "KSA.PartInstance::DropStaleConnectors").Contains("Load"),
            "blueprint load drops stale connectors in UncompressedVehicleSave.Load");

        // Iron Man RCS cache (5807): RecomputeRocketControls now resets the thruster global state to Zero,
        // so InvalidateCache runs the pending recompute (EnsureDerived) before writing its own state.
        Require(probe.CallsOf("KSA.PartTree", "RecomputeRocketControls").Single().Contains("KSA.ThrusterControllerGlobalState::get_Zero")
                && probe.CallsOf("KSA.PartTree", "EnsureDerived").Count == 1
                && probe.CallsOf("KSA.PartTree", "FlushDirtyDerived").Single().Contains("KSA.PartTree::EnsureDerived"),
            "rocket-control recompute resets the thruster cache to Zero and runs through EnsureDerived");

        // Pebbles residency (5875): clutter residency is indexed by a compacted clutter slot that only
        // Main/Secondary viewports own; release must be guarded with HasViewportSlot. Since KSA 5919 the
        // caller resolves the slot (ReleaseViewport takes it as an int), as the renderer itself does.
        Require(probe.CallsOf("KSA.GroundClutterRenderer", "ResolveViewportSlot").Count == 1
                && probe.CallsOf("KSA.GroundClutterRenderer", "OnViewportUnregistered").Single()
                    .Contains("KSA.GroundClutterRenderer::ResolveViewportSlot"),
            "the clutter slot is resolved by the caller of ClutterEcotypeRenderData.ReleaseViewport (throws for unmapped viewports)");
        Require(probe.CallsOf("KSA.GroundClutterRenderer", "HasViewportSlot").Count == 1
                && probe.CallsOf("KSA.GroundClutterRenderer", "OnViewportUnregistered").Single()
                    .Contains("KSA.GroundClutterRenderer::HasViewportSlot"),
            "the renderer guards its own viewport release with HasViewportSlot, as Pebbles now does");

        // You Didn't See Anything (5863): the Crew Clock is drawn outside GaugeCanvas, anchored by GameTime.
        Require(probe.CallsOf("KSA.Program", "OnDrawUiFrame").Single().Contains("KSA.CrewClock::Draw"),
            "Program.OnDrawUiFrame draws the Crew Clock window");
        Require(probe.CallsOf("KSA.GaugeCanvas", "OnDrawUi").Any(calls => calls.Contains("KSA.CrewClock::SetAnchor")),
            "GaugeCanvas.OnDrawUi anchors the Crew Clock under the GameTime gauge");

        Console.WriteLine("PASS: 5901 engine rockets, stale-connector seam, RCS cache reset, clutter viewport slots and crew-clock anchor");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }
}
