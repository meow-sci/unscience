using System;
using KSA;
using MeowSci.PebblesLib;

/// <summary>
/// KSA 5629 per-viewport residency: arrays Pebbles detaches from PlanetEcotypeRenderData must release
/// every registered viewport, because the renderer only releases closing viewports on attached arrays.
/// </summary>
internal static class ClutterResidencyChecks
{
    private const int Slots = 16;

    public static void Run()
    {
        var main = new TestViewport(0);
        var hidden = new TestViewport(3); // registered but not "relevant", so the renderer never releases it per frame
        var unused = new TestViewport(5);
        ViewportRegistry.Registered = [main, hidden, unused];
        try
        {
            // Without the release, a viewport that closes while the originals are detached pins its cells.
            var leaked = Populate(main, hidden);
            ViewportRegistry.Registered = [main, unused];
            leaked[0].ReleaseViewport(main); leaked[0].ResolvePendingReleases(); // what restore-time reuse would see
            Check(leaked[0].FreeSlots < Slots, "fixture reproduces the closed-viewport leak");

            ViewportRegistry.Registered = [main, hidden, unused];
            var originals = Populate(main, hidden);
            ClutterResidency.ReleaseDetached(originals);
            foreach (var render in originals)
            {
                Check(render.FreeSlots == Slots, "every pooled cell slot returns once all viewports are released");
                Check(render.LoadedCells(0) == 0 && render.LoadedCells(3) == 0, "every viewport grid is emptied, including hidden viewports");
                Check(render.Resolves == 1, "pending releases resolve once per array, after all viewports");
                Check(render.GenerationViewport == null, "detached arrays drop their generation viewport");
            }

            // The hidden viewport closes while detached; the retained originals hold nothing for it.
            ViewportRegistry.Registered = [main, unused];
            ClutterResidency.ReleaseDetached(originals);
            Check(originals[0].FreeSlots == Slots && originals[0].Resolves == 2, "repeat release on clean arrays is harmless");
            ClutterResidency.ReleaseDetached([originals[0], null!, originals[1]]);
            ClutterResidency.ReleaseDetached([]);
            Console.WriteLine("PASS: Pebbles detached clutter arrays release per-viewport residency (KSA 5629+).");
        }
        finally { ViewportRegistry.Registered = []; }
    }

    private static ClutterEcotypeRenderData[] Populate(IViewport main, IViewport hidden)
    {
        var arrays = new[] { new ClutterEcotypeRenderData(Slots), new ClutterEcotypeRenderData(Slots) };
        foreach (var render in arrays)
        {
            render.Load(main, 1); render.Load(main, 2);
            render.Load(hidden, 2); render.Load(hidden, 3); // cell 2 is shared by both viewports
        }
        return arrays;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Clutter residency check failed: " + message);
    }
}
