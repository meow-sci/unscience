# Blinky library

Reusable engine-pixel grid builder, scanner, manager and Unscience submod. See
[feature usage](../blinky/README.md) for layout, fuel, ignition and pattern controls.

## Scene saves

The Unscience save participant captures named grid ownership, exact cell-to-part paths, active-mask
metadata, engine mesh visibility and scrolling source pixels/speed/phase. KSA saves the created
parts and current engine states, but omits ordinary `pixel_*` names. Restore rebinds the native
parts with `PixelGrid.BuildFromPartGroups`, repairs declared fuel feeds, and registers render/cache
membership without respawning parts or changing vehicle ignition. Active scroll resumes its saved
phase; static engine state remains the native snapshot. Missing/duplicate cell parts produce a
load warning rather than a guessed association. Game ignition/throttle/fuel rules still apply.

`BlinkySubmod.Persistence.cs` owns explicit DTOs and replay; the manager's membership set supports
render suppression and excludes restored pixels from the ordinary-engine cache. Native save/load
with actual fuel wiring and Vulkan rendering still requires an in-game acceptance run.

Pending grid removals are saved as intent and restarted against restored parts; old-world deferred
callbacks are cleared during scene reset. Removal delays restart rather than running against stale
vehicle references.

## KSA 5482 compatibility

`BlinkyPatches.Apply/Remove` register and unregister the `blinky` owner with
`ksa-abstractions.lib/PartRenderFilter`. The predicate hides a full part when `RenderPixelParts` is off
and `BlinkyGridManager.IsPixelPart` matches. Raytraced IVA submissions are not filtered.
`LcdGridBuilder` calls `vehicle.Parts.EnsureDerived(DerivedData.ResourceGroups)` before
`VerifyPropellantFeeds`. KSA 5482 defers derived data to the next frame's flush, and the check needs
the resource managers, so without this call it would report every pixel as unfed.

## KSA 5438 compatibility

KSA 5438 uses FlowOrder<Tank> views for resource drainage. Feed diagnostics count actual tank entries in each selected level, respecting same-stage/reversed views; empty distance levels no longer count as reachable propellant. Fuel wiring and engine control remain native.
