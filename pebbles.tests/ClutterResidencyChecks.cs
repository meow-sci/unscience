using System;
using KSA;
using MeowSci.PebblesLib;

/// <summary>
/// KSA 5629 per-viewport residency: arrays Pebbles detaches from PlanetEcotypeRenderData must release
/// every registered viewport, because the renderer only releases closing viewports on attached arrays.
/// KSA 5875: only viewports with a clutter slot (Main/Secondary) are released; others would throw.
/// KSA 5919: ReleaseViewport takes the resolved clutter slot, so the guard precedes slot resolution.
/// KSA 5919/5920: the renderer frees per-planet buffers only for attached arrays, so detaching frees them too,
/// exactly once and only after every cell is released.
/// </summary>
internal static class ClutterResidencyChecks
{
    private const int Slots = 16;

    public static void Run()
    {
        var main = new TestViewport(0);
        var thumbnail = new TestViewport(1); // registered, but KSA 5875 gives it no clutter slot
        var hidden = new TestViewport(3); // registered but not "relevant", so the renderer never releases it per frame
        var unused = new TestViewport(5);
        var portrait = new TestViewport(6); // character portraits likewise own no clutter slot
        GroundClutterRenderer.MapClutterViewports(main, hidden, unused);
        ViewportRegistry.Registered = [main, thumbnail, hidden, unused, portrait];
        try
        {
            // Without the release, a viewport that closes while the originals are detached pins its cells.
            var leaked = Populate(main, hidden);
            ViewportRegistry.Registered = [main, thumbnail, unused, portrait];
            leaked[0].ReleaseViewport(GroundClutterRenderer.ResolveViewportSlot(main)); leaked[0].ResolvePendingReleases(); // what restore-time reuse would see
            Check(leaked[0].FreeSlots < Slots, "fixture reproduces the closed-viewport leak");
            Check(leaked[0].HasPlanetResources, "fixture reproduces the detached per-planet VRAM retention");
            leaked[1].DisposePlanetResources();
            Check(leaked[1].FreesWhileHoldingCells == 1, "fixture detects freeing per-planet buffers before cells are released");

            ViewportRegistry.Registered = [main, thumbnail, hidden, unused, portrait];
            var originals = Populate(main, hidden);
            Check(Throws(() => GroundClutterRenderer.ResolveViewportSlot(thumbnail)), "fixture rejects viewports without a clutter slot like KSA 5875");
            ClutterResidency.ReleaseDetached(originals);
            foreach (var render in originals)
            {
                Check(render.FreeSlots == Slots, "every pooled cell slot returns once all viewports are released");
                Check(render.LoadedCells(main) == 0 && render.LoadedCells(hidden) == 0, "every viewport grid is emptied, including hidden viewports");
                Check(render.Resolves == 1, "pending releases resolve once per array, after all viewports");
                Check(render.GenerationViewport == null, "detached arrays drop their generation viewport");
                Check(!render.HasPlanetResources && render.PlanetFrees == 1, "detached arrays free their per-planet buffers once");
                Check(render.FreesWhileHoldingCells == 0, "per-planet buffers are freed only after every cell is released");
            }

            // The hidden viewport closes while detached; the retained originals hold nothing for it.
            ViewportRegistry.Registered = [main, thumbnail, unused, portrait];
            ClutterResidency.ReleaseDetached(originals);
            Check(originals[0].FreeSlots == Slots && originals[0].Resolves == 2, "repeat release on clean arrays is harmless");
            Check(originals[0].PlanetFrees == 1, "a repeat detach never frees per-planet buffers twice");

            // Restore re-attaches; the renderer reallocates lazily, and a later detach frees that allocation once.
            originals[0].AllocatePlanetResources();
            ClutterResidency.ReleaseDetached([originals[0]]);
            Check(originals[0].PlanetFrees == 2 && !originals[0].HasPlanetResources, "re-attached arrays free their new allocation on the next detach");
            var unallocated = new ClutterEcotypeRenderData(Slots);
            ClutterResidency.ReleaseDetached([unallocated]);
            Check(unallocated.PlanetFrees == 0, "arrays the renderer never allocated free nothing");
            ClutterResidency.ReleaseDetached([originals[0], null!, originals[1]]);
            ClutterResidency.ReleaseDetached([]);
            Console.WriteLine("PASS: Pebbles detached clutter arrays release per-viewport residency, skip non-clutter viewports and free per-planet VRAM once (KSA 5629+/5875/5919).");
        }
        finally { ViewportRegistry.Registered = []; GroundClutterRenderer.MapClutterViewports(); }
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (Exception) { return true; }
    }

    private static ClutterEcotypeRenderData[] Populate(IViewport main, IViewport hidden)
    {
        var arrays = new[] { new ClutterEcotypeRenderData(Slots), new ClutterEcotypeRenderData(Slots) };
        foreach (var render in arrays)
        {
            render.Load(main, 1); render.Load(main, 2);
            render.Load(hidden, 2); render.Load(hidden, 3); // cell 2 is shared by both viewports
            render.AllocatePlanetResources(); // the viewport is near this body, so the renderer allocated
        }
        return arrays;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Clutter residency check failed: " + message);
    }
}
