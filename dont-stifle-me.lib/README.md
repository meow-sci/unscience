# dont-stifle-me.lib

Core of the [dont-stifle-me](../dont-stifle-me/README.md) mod — editor scale and configurable-value
limit patches gated by runtime toggles. Consumed by the standalone mod and by the unscience supermod.

## Files

| File | Purpose |
|---|---|
| `EditorScaleSettings.cs` | Static toggles: `Enabled` (master: clamp removal + per-axis), `Snap` (default true = game behavior). Read every frame; no re-patching on change. |
| `EditorLimitSettings.cs` | Static `JplSaidNoClamps` toggle (default off) for the extensible configurable-value limit feature. |
| `EditorScalePatches.cs` | `Apply(Harmony)` / `Remove(Harmony)`. Resolves the private targets by name and installs the four patches below. |
| `EditorValueLimitPatches.cs` | Expands parachute diameter bounds to 2–1000 m while enabled, tracks the original per-instance bounds, and restores them when disabled or unloaded. |
| `DontStifleMeMenu.cs` | `Draw()` — the "Don't Stifle Me" top-level menu body; the standalone mod calls it from a `Program.DrawProgramMenusHook` postfix. |
| `PerAxisScaleDrag.cs` | The per-axis replacement for the stock drag routine; owns the raw (un-snapped) accumulator for the current drag session. |
| `DontStifleMeSubmod.cs` | `ISubmod` ImGui surface for unscience (same controls + patch-status warning). |

## Patches

| Game target (`KSA.VehicleEditor`) | Patch | What it does |
|---|---|---|
| `private static (double Min, double Max) ScaleBoundsFor(Part)` | postfix | When clamp removal is active, returns `(1e-6, +inf)` instead of `(0.5, 2.0)`. Both the drag accumulator and `QuantizeScale` read this, so one patch lifts the clamp everywhere. |
| `private void UpdateSelectedScale(ref readonly double4x4, IViewport)` | prefix | When per-axis scaling is active, runs `PerAxisScaleDrag.Step` and skips the original (which writes `new double3(s, s, s)`). Falls through to stock on any exception. |
| `private static double QuantizeScale(Part, double rawScale)` | prefix | When `Snap` is off, returns `rawScale` clamped to `ScaleBoundsFor(part)` and skips the 0.25 m snapping. Both the stock uniform drag and the per-axis drag call this, so one prefix covers both. |
| `public void UpdateScaleGizmo(ref readonly double4x4, doubleQuat, IViewport, double)` | postfix | Per-frame hook: when `GizmoGrabbed` is false, ends the drag session so the next grab re-seeds from the part's actual scale. Avoids a `Brutal.Glfw` dependency on `OnMouseButton`. |
| `private void DrawParachuteSection(Part, ReadOnlySpan<Parachute>)` | prefix | Before the editor draws its slider, changes each selected chute's runtime `Tuning.MinDiameterM` / `MaxDiameterM` to 2 / 1000 while **jpl said no clamps** is enabled. |
| `public void Parachute.SetDiameter(float)` | prefix | Expands every chute in the receiving part before the stock setter clamps, so multi-canopy and symmetry counterparts accept the same enlarged value. |

`PerAxisScaleDrag.Step` mirrors the stock math (near-plane cursor delta → projected on the gizmo
axis → scaled by part depth), then calls the game's own private `QuantizeScale` and
`ForEachPartWithSymmetry` through `AccessTools.MethodDelegate` so snapping and symmetry behave
exactly as stock, and finishes with `Part.RefreshScaleAndReposition()` + `PartTree.RefreshStaticMass()`.

Gizmo segment index → axis: `0 = X`, `1 = Y`, `2 = Z`.

## Game-update watchlist

The five scale member names and `DrawParachuteSection` are resolved as **strings**
(`AccessTools.Method`) — a rename fails at `Apply()` (logged, patches skipped, UI shows a red notice)
rather than at compile time. `Parachute.SetDiameter(float)` is resolved with a typed signature. See
`scope/part-editor-and-robotics.md` → dont-stifle-me.

## Scene saves

Saves the master scale-limit switch, scale snapping and extended editor-value switch. Native saves retain resulting edited geometry. Transient gizmo drags are not resumed.

Scene persistence is registered by the Unscience host through `ISaveParticipantSource`; standalone development hosts retain their existing lifecycle. Use ordinary KSA Save/Load. See [save behavior](../unscience/README.md#scene-saves) and the [design plan](../plans/SAVES.md).
