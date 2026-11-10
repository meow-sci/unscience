# You Didn't See Anything

Target: KSA **2026.11.10.5801**, StarMap.API **0.3.6**, Harmony **2.4.2**.

The submod keeps selected ImGui windows available while the player uses **F2** to hide KSA's UI.
It starts disabled. The controls are an enable switch, detached-platform-windows-only
selection, and exclusion of the built-in gauge windows. The feature belongs to the existing
Unscience distribution; its standalone host remains a development project.

## Source findings

The supplied CURRENT sources and DLLs are authoritative. In `KSA/Program.cs`, `OnFrame(double,
double)` gates `OnDrawUiFrame`, `OnDrawUiViewports`, and `OnDrawUiThreadSafe` on `DrawUI`
(`:2503–2511`). `OnDrawUiThreadSafe(double)` also tests `DrawUI` internally (`:3452`). F2 therefore
stops window submission as well as ordinary StarMap GUI callbacks. Merely retaining rendering
does not keep a live, interactive window. `ImGui.Render()` still runs (`:2524`), and secondary
platform-window rendering remains available in `PostRender`.

## Implementation

Guarded Harmony transpilers replace only the two UI gate getters in `Program.OnFrame` and the
single initial gate in `OnDrawUiThreadSafe`. The replacements allow native window submission and
StarMap GUI callbacks while retaining the original false `DrawUI` setting everywhere else. No
`DrawUI` setter is called: its false assignment also clears timed alerts in this build.
A dispatch marker suppresses the hidden-HUD fallback exactly once, including when a checkbox
turns the feature off during its own callback.

Native ImGui context hooks suppress input before `NewFrame` and compact each platform viewport's
rendered draw lists after window submission. They operate beyond managed `Begin` wrappers, which
can be inlined or bypassed by native child/popup creation. Parent ownership follows the Begin
hierarchy rather than the whole dock tree, so unrelated docked windows are not classified as
gauges. Non-window background/foreground overlay lists are suppressed during the hidden pass;
this also hides FPS and mod overlays drawn directly into those lists.

Before hover processing and at the end of UI submission, popup chains owned by excluded windows
are dismissed. This deliberately closes their menus/dialogs so an invisible modal cannot keep
blocking visible detached windows. A newly created modal can affect its creation frame before the
end-frame hook resolves its owner. The end-frame hook also marks an excluded platform owner hidden
when its viewport contains no allowed active window, preventing an empty OS window from lingering.
Only the feature's input/hidden flag deltas are restored on the next frame or unload.

The 15 stock `GaugeCanvas` IDs come from CURRENT `Content/Core/Gauges.xml`. Their actual windows
use runtime `###GaugeCanvas_N` identities, resolved from `GaugeCanvas.AllCanvases` and `DebugTitle`.
The static canvas IDs, not translated display names or allocation-dependent numeric suffixes,
define the denylist. Native menu-bar, world-overlay and gauge-finalization hooks keep those HUD
paths hidden. Hook delegates remain rooted, context shutdown clears pointer caches, and failure
leaves ordinary F2 behavior active with a visible unavailable status.

## State ownership

Enablement and selection flags are durable configuration stored through native-scene
sidecar capture/reset/replay. Window positions, docking, and platform geometry remain owned by
the existing ImGui/KSA layout mechanism. No live ImGui pointers, GPU objects, window handles,
per-frame suppression state, or Harmony reflection caches belong in a scene payload.

The player-owned `Program.DrawUI` state survives each frame and unloading unchanged. Vanilla
saves, old saves without this feature's record, and new scenes restore disabled defaults.

## Managed verification

Verified on macOS ARM64 with .NET SDK 10.0.403 against the explicit CURRENT `dll/` directory:

- Full solution `dotnet build ksa-mod-experiments.slnx -m:1 -p:UseSharedCompilation=false
  -nodeReuse:false -p:NuGetAudit=false`, with explicit `KSAFolder` and
  `UNSCIENCE_DIST_DIR=/private/tmp/unscience-ydsa-dist`: **0 warnings, 0 errors**. NuGet auditing
  was disabled for the offline build; warnings-as-errors remained enabled.
- `you-didnt-see-anything.tests`: production save adapter/coordinator round-trip, repeated/vanilla
  load and malformed-record retention, dispatch tracking, filter decisions and real CURRENT
  KSA.dll UI-gate counts/callback-order metadata probes: **pass**.
- `you-didnt-see-anything.patch-tests`: source-linked production Harmony installers/transpilers
  and shared fallback hook against managed game fixtures: **pass**. Covers unchanged F2,
  callback counts, mid-frame disable, unavailable filter fallback, stock/custom gauges,
  menu/world suppression, changed-IL rollback and preservation of foreign patches. Native filter
  calls are stubbed; this does not execute the live game methods or native context hooks.
- Existing `saves.tests` and `ksa-upgrade.tests`: **pass**. The upgrade suite's unrelated live
  canopy-shader check skips because Content shaders are absent beside the reference DLLs.
- `python3 scripts/check-dist.py /private/tmp/unscience-ydsa-dist`: **pass**, one Unscience
  distribution with 33 explicitly referenced feature/shared assemblies and the new feature DLL.

Logs: `/private/tmp/unscience-ydsa-build.log`, `/private/tmp/unscience-ydsa-final-build.log`.
The built distribution is `/private/tmp/unscience-ydsa-dist/unscience/`. No live install was changed.

## Native acceptance

This macOS ARM64 workspace has reference assemblies, not a running Windows KSA installation.
Compilation and managed checks cannot establish native viewport rendering or interaction.

- [ ] With the feature disabled, verify ordinary F2 behavior and continued background feature updates.
- [ ] Open the toolbox and another mod's window; enable the feature, press F2 twice, and confirm
  both remain interactive during hidden UI without duplicating frame updates.
- [ ] Enable detached-only selection. Move windows into/out of the main game window before and
  during hidden UI; only independent OS windows should remain visible.
- [ ] Exercise attached/detached dock groups, child windows, menus, combo popups, tooltips,
  modal dialogs, collapsed windows, and platform-window closure/recreation. Hidden windows must
  not intercept mouse or keyboard input.
- [ ] Enable gauge exclusion and verify stock flight, EVA, burn, time, and editor gauges remain
  hidden. Disable exclusion and verify eligible gauges follow the selected viewport policy.
- [ ] Confirm main game rendering, world labels, F2 state, and normal gameplay inputs continue to
  follow the game's hide-UI setting; the feature must not keep toggling F2 back on.
- [ ] Change feature flags with F2 active; disable the feature and restore F2. Verify normal UI
  returns with no leaked temporary window flags or missing content.
- [ ] Save enabled and disabled setups, load repeatedly and A → B → A, then load a vanilla/legacy
  save and create a new scene. Verify exact saved flags, default reset, and no stale window pointers.
- [ ] Exit while hidden and while a detached window is open. Verify targeted unpatch and safe
  restoration before ImGui shuts down, including the existing pre-renderer shutdown path.
- [ ] Repeat with the installed mod stack. Another mod may independently suppress its own UI
  or modify the same frame gates; record patch diagnostics and the complete stack if it conflicts.
