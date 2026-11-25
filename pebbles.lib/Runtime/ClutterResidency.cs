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
/// Since KSA 5875 residency is indexed by a clutter slot that only Main/Secondary viewports own
/// (<see cref="GroundClutterRenderer.ResolveViewportSlot"/> throws for thumbnail/portrait viewports), so
/// the release skips unmapped viewports exactly like the renderer's own OnViewportUnregistered. Since KSA
/// 5919 <c>ReleaseViewport</c> takes that resolved slot.
/// <para>
/// Since KSA 5919/5920 the large per-planet buffers (instance positions, cell counts, LOD fades, curvature
/// and view selections) are allocated lazily by <c>AccumulateViewportCells</c> and freed by
/// <c>DisposeUnusedPlanetResources</c>, but only for arrays stored in the dictionary. A detached array
/// would keep them for the whole override, so detaching also frees them, like that native pass. Callers
/// have already waited for the device to go idle. <c>DisposePlanetResources</c> is guarded by
/// <c>HasPlanetResources</c>, so later native or Pebbles disposal never frees them twice. Once the array is
/// attached again, the renderer reallocates and rewrites its descriptors before it is used.
/// </para>
/// </summary>
internal static class ClutterResidency
{
    public static void ReleaseDetached(ClutterEcotypeRenderData[] outgoing)
    {
        ReadOnlySpan<IViewport> viewports = ViewportRegistry.Views;
        foreach (var render in outgoing)
        {
            if (render == null) continue;
            foreach (var viewport in viewports)
                if (GroundClutterRenderer.HasViewportSlot(viewport))
                    render.ReleaseViewport(GroundClutterRenderer.ResolveViewportSlot(viewport));
            render.ResolvePendingReleases();
            // After every cell is released, so the native "still holds cells" diagnostic stays quiet.
            render.DisposePlanetResources();
            // The renderer consumes and clears this each frame; never let a detached array keep a
            // viewport that may be unregistered before the array is attached again.
            render.GenerationViewport = null;
        }
    }
}
