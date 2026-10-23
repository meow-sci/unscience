using System;
using KSA;

namespace MeowSci.PebblesLib;

/// <summary>
/// Since KSA 5629 each <see cref="ClutterEcotypeRenderData"/> keeps per-viewport cell residency
/// (viewport cell grids, residency masks, pooled cell slots), and <see cref="GroundClutterRenderer"/>
/// releases a closing or body-changing viewport only on the arrays currently stored in
/// <c>PlanetEcotypeRenderData</c> (OnViewportUnregistered, AccumulateViewportCells). Arrays that Pebbles
/// detaches would otherwise keep slots pinned for viewports that close or move while detached, and leak
/// them once the retained originals are restored. Detaching therefore performs the renderer's own release
/// sequence for every registered viewport. The renderer still tracks the body per viewport slot, so the
/// arrays attached next see empty viewport grids and re-evaluate every cell on the following frame.
/// </summary>
internal static class ClutterResidency
{
    public static void ReleaseDetached(ClutterEcotypeRenderData[] outgoing)
    {
        ReadOnlySpan<IViewport> viewports = ViewportRegistry.Views;
        foreach (var render in outgoing)
        {
            if (render == null) continue;
            foreach (var viewport in viewports) render.ReleaseViewport(viewport);
            render.ResolvePendingReleases();
            // The renderer consumes and clears this each frame; never let a detached array keep a
            // viewport that may be unregistered before the array is attached again.
            render.GenerationViewport = null;
        }
    }
}
