# dont-stifle-me

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

Removes restrictions from the KSA vehicle editor:

1. **Scale clamp** — top-level parts can no longer be scaled outside **0.5x–2.0x**.
2. **Uniform scaling** — dragging any scale-gizmo arrow now scales all three axes together; per-axis
   (non-uniform) scaling is gone.

With this mod enabled, both go away: any positive scale is accepted and each gizmo arrow (X / Y / Z)
scales only its own axis, like the pre-5348 editor. Scale **snapping** (0.25 m diameter increments)
can also be switched off for free, continuous scaling. A separate **"jpl said no clamps"** option
expands configurable editor ranges; initially, it changes parachute diameter from the part-authored
limits (currently 20–50 m) to **2–1000 m**.

## Controls

Standalone: a **"Don't Stifle Me"** top-level menu in the game's main menu bar. Inside the
**unscience** supermod the same controls appear as the **"Don't Stifle Me - Editor Limits"**
section (no menu is added there).

| Control | Default | Effect |
|---|---|---|
| **Enabled** | on | Lifts the 0.5x–2x clamp to `(1e-6, +inf)` and makes gizmo drags per-axis. Off = stock editor. Flip at any time; no restart needed. |
| **Snap scaling** | on (game default) | Keep the game's 0.25 m diameter snapping. Off = raw continuous drag values. Only matters while Enabled. |
| **jpl said no clamps** | off | Expands selected configurable editor ranges. Currently sets parachute diameter bounds to 2–1000 m. Disabling it or unloading the mod restores each live chute's authored bounds. |

## Notes / limitations

- Non-uniform scale is a *visual/mesh* scale. The game now derives connector positions, mass and
  inertia from a single `ScaleFactors` value = the **largest axis**, so connectors on a part stretched
  along one axis may not sit on the mesh surface. This is a game limitation, not something the mod
  can fix without replacing `Part.RefreshScale`.
- Sub-parts already had unbounded scale in the stock editor; the mod does not change them.

## How it works

Core logic lives in [`dont-stifle-me.lib`](../dont-stifle-me.lib/README.md). This project is the
standalone StarMap entry: `Patcher.cs` applies `HotkeyGuard`, `EditorScalePatches`, `EditorValueLimitPatches` and
`MenuBarPatch` (a postfix on `Program.DrawProgramMenusHook` that draws `DontStifleMeMenu`). There is
no floating window.
