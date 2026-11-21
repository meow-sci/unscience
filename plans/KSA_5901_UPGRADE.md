# KSA 5901 upgrade — impact, remediation and acceptance

## Verdict and inputs

Upgraded the suite to the supplied **2026.11.16.5901** build. One compile break and four silent
runtime regressions are fixed; one test harness assertion followed a native signature change. Wing
Walker mounts gained a native Control module (user decision) so rider-only craft stay controllable
under 5823; Iron Man Auto modes on an incapacitated kitten remain a recorded decision.
Verification is **static and managed**: no 5901 game installation was used on this macOS ARM64 host.

| Input | Value |
|---|---|
| CURRENT / NEW | `../ksa-game-assemblies/current`, build **2026.11.16.5901**, metadata date **2026-11-17**, `fromRevision=5801`, `toRevision=5901` |
| PREVIOUS / OLD | `../ksa-game-assemblies_prev/current`, build **2026.11.10.5801**; matches the prior recorded scope baseline |
| Changelog | 98 entries, every revision **5802–5899**; **5900–5901 are unlogged**. No earlier baseline gap in this direct hop |
| Sources | Supplied CURRENT/PREVIOUS `decomp/` and `Content/` trees. In-repo `decomp/ksa` is historical and was not used as authoritative |
| Source delta | After normalizing line endings: 363 C# files changed, 500 added, 11 removed. Most additions are the new `KSA.Kfs*` namespaces (not hooked into normal play) and life-support/mission types |
| Content delta | 90 text files changed, 11 added, 3 removed (mostly shaders: cloud shadows/ambient, part highlight, terrain modifiers) |
| Build DLLs | Plain `dotnet build ksa-mod-experiments.slnx`; `Directory.Build.props` resolves `KSAFolder` to the adjacent `../ksa-game-assemblies/current/dll/` with no override |
| Dependencies | StarMap.API **0.3.6**, Lib.Harmony **2.4.2**, Tomlyn **0.19.0** unchanged. Brutal.Vulkan abstractions changed with the game (5867); Brutal.ImGui DLLs are byte-identical |

## Changelog priorities

| Revisions | Coupling reviewed |
|---|---|
| 5889 | Rocket state/commands moved from RocketCore to Rocket; `EngineController.Cores` → `Rockets`; new SolidRocket |
| 5866 | Older-save part changes: new `PartInstance.DropStaleConnectors()` on world and blueprint load |
| 5807, 5838 | Thruster cache after splits (`RecomputeRocketControls` now resets to Zero); gimbal rate limits/TVC |
| 5875, 5885, 5892 | Ground clutter viewport compaction (8 → 5 slots, Main/Secondary only); host-memory BVH buffers; clutter-off crash |
| 5863, 5865, 5870–5872 | Crew Clock window under the GameTime gauge, Ctrl+F6 binding, View menu |
| 5804, 5813–5823 | EVA incapacitation removes kitten control; probe-core power/avionics; loss of control drops Auto attitude/burn; per-zone crew control |
| 5810 | Non-looping jump clips: `PlayAnimation/SetAnimation(..., bool loop = true)` |
| 5816, 5829–5830, 5867, 5876, 5879–5880, 5894 | Dynamic rendering everywhere, shader reloading, Vulkan abstractions, cloud ambient/shadow passes, colour target formats |
| 5825, 5886 | Part highlight tint + rim; native part picker |
| 5835, 5845, 5891, 5898 | Lazily allocated module lists, editor copy/detach performance |
| 5802, 5811, 5820, 5824, 5832, 5846, 5859, 5864, 5883, 5895 | Re-imported art, removed interstage decouplers, habitat modules with inverted interiors, connector changes |
| 5847–5857, 5873–5874 | Terrain modifiers, colouration and CPU/GPU terrain precision |
| 5812, 5826–5828, 5833–5834, 5837, 5840–5844, 5860–5862, 5868–5869, 5893 | Life Support window, missions, UniverseTime save fields |
| 5842–5843, 5858, 5887–5888, 5896, 5899 | kFS VM/editor; PhysicsBubble flight-computer dispatch (inactive unless enabled) |

## Confirmed findings and fixes

All findings are **new in NEW relative to OLD**. Evidence paths are relative to each tree's
`current/decomp/KSA/` unless marked otherwise.

| Consumer / production code | Class and evidence in OLD → NEW | Resolution |
|---|---|---|
| Blinky diagnostics: `blinky.lib/BlinkySubmod.cs` (`ReachesPropellant`, `DiagnoseController`) | **Compile break / retyped**: OLD `EngineController.cs:36` `RocketCore[] Cores`; NEW `:36` `Rocket[] Rockets`, with `Rocket.Core`/`Rocket.Cores` (`Rocket.cs:11,21`) | New `ControllerCores` flattens every rocket's cores. Feed and ignition paths already use `SubtreeModules.Get<RocketCore>()`, which is unchanged |
| Pebbles: `pebbles.lib/Runtime/ClutterResidency.cs` | **Semantic drift / throws**: OLD `ClutterEcotypeRenderData.ReleaseViewport` indexes by `ShaderSlot` (8 slots). NEW `:1058` calls `GroundClutterRenderer.ResolveViewportSlot` (`GroundClutterRenderer.cs:401`), which throws for every viewport except Main/Secondary (`:436-446`). `ViewportRegistry.Views` always holds the part-thumbnail and portrait viewports, so **every recipe Apply and save-load replay failed** | Release only viewports with `GroundClutterRenderer.HasViewportSlot`, exactly like native `OnViewportUnregistered` (`:463-467`). Fixture now models the compacted slot and throws for unmapped viewports; the negative control reproduces the native exception |
| Iron Man saves: new `iron-man.lib/IronManStaleConnectorGuard.cs` (wired from `IronManConnectorPatches`) | **Native save semantics (new Harmony seam)**: NEW `PartInstance.DropStaleConnectors()` (`PartInstance.cs:113-139`, absent in OLD) runs before construction on world load (`CelestialSystem.cs:764`) and blueprint load (`UncompressedVehicleSave.cs:121`). It deletes saved connections whose `Index` ≥ template connector count — exactly Iron Man's owned anchor suffix. Connected anchors came back free with a yellow alert, and the loss became permanent on the next save | Prefix hides only a marked backpack's validated owned range `[StockConnectorCount, +Anchors.Count)` from the native check; postfix restores them in saved order. Genuinely stale indices, unmarked parts and native child recursion are unchanged; returned count excludes kept anchors (no false alert). Never throws (the world-load call site is outside KSA's per-vehicle guard); damaged records still fail closed in the constructor prefix |
| Iron Man RCS cache: `iron-man.lib/IronManRcsOrientationPatches.cs` (`InvalidateCache`) | **Body change under an invariant**: NEW `PartTree.RecomputeRocketControls` (`PartTree.cs:1352-1355`) resets the thruster global state to `ThrusterControllerGlobalState.Zero` (OLD `:1301-1325` did not). Enable/Disable mark derived data dirty first, and `PartTree.FlushDirtyDerived` (`Program.cs:2468`, before vehicle solvers) then replaced the invalidation with Zero, reviving the 5638 zero-RCS-authority bug on Identity frames | Run `kitten.Parts.EnsureDerived(DerivedData.RocketControls)` before writing the invalidated state, so the pending recompute (and its Zero reset) happens first and the later flush has nothing to redo. Fixture models the dirty flush; negative control fails exactly on the new check |
| You Didn't See Anything: `UiGatePolicy.cs`, `NativeWindowFilter.cs` | **New native window**: NEW `CrewClock.Draw` (`CrewClock.cs:118-160`) is a separate `##crewClock` window drawn from `Program.OnDrawUiFrame` (`Program.cs:3419`), anchored only by the GameTime gauge (`GaugeCanvas.cs:867-870`). With Hide Gauges on, the gauge is skipped, the clock falls back to a floating top-centre window and was retained while every stock gauge was hidden | `UiGatePolicy.GaugeAttachedWindows` maps `##crewClock` → GameTime; the window filter treats it as part of that stock gauge. It is shown whenever GameTime is shown |
| Wing Walker mounts: `wing-walker/WingWalker.xml` (`Unscience_WingWalker_Seated`, inherited by Standing) | **Native rule change (5823)**: OLD control required any able kitten aboard (`Vehicle.cs:679-693`). NEW requires an able kitten in a Control part's own habitat zone (`Vehicle.cs:704-718,3813-3830`); `OpenSeat` joins no other zone, so rider-only craft lost control | **User decision:** add native `<Control />`. `ScanControls` resolves it to the seat's own zone (empty/incapacitated seats give no control, never the probe-core fallback). Side effects: "Control from here", split/breakup naming, zone labels, SRBs on command. Native harness asserts one Control per final part |
| Wing Walker native harness: `wing-walker.native-tests/NativeXmlAssertions.cs` (test only) | **Signature (5810)**: `AnimatedRenderable.PlayAnimation/SetAnimation(IAnimation, float)` became `(IAnimation, float, bool loop = true)` (OLD `AnimatedRenderable.cs:128,133` vs NEW same lines). Production calls are typed and bind `loop: true`, preserving looping idles | Assertion requires the three-argument public surface and a `true` default |

Sphinx is unaffected by the static pipeline change (5879: 10 → 9 sets, cloud shadows removed, planet
uniforms set 9 → 8) because it binds only sets 2/3; its stale comment and `scope/statics.md` were corrected.

## Decisions recorded, not changed

| Interaction | Evidence | Options |
|---|---|---|
| Iron Man rocket-mode kitten becomes incapacitated with Auto attitude/burn engaged (Life Support rule on) | NEW `KittenEva.IsControllable => !_incapacitated` (`KittenEva.cs:69`; OLD always true). `Vehicle.UpdateControl` (`Vehicle.cs:3412`) samples it before `UpdateIncapacitated` (`:3424`), so native `ReleaseFlightComputer` (`:3775`) never fires for kittens. Stock kittens never use Auto; Iron Man kittens can, and player input is now locked out | Keep as is (autopilot keeps flying, as in 5801, but the player can no longer cancel it), or add an Iron Man handoff release mirroring `ReleaseFlightComputer` |

## Surfaces checked without another confirmed code break

- **Reflection and strings:** no string literal or `___field` injector in production code names an
  identifier that vanished anywhere in NEW. Type-scoped re-checks of the §4 watchlist (Camera FOV,
  avatar chain, light templates, registries, render batches, ring/texture/mesh private fields,
  Pebbles' `_renderPassInfo`/`_planetClutterMaxBoundingRadius`/`BuildMaterialIndirection(string)`,
  StagingPool, material-buffer allocator) all resolve with unchanged types. All 61 production
  literals that match Content IDs/keys still exist.
- **Rendering:** no mod builds a pipeline against a game render pass/framebuffer (5816); main and
  secondary offscreen formats are unchanged (5880 changed HDR, bloom, gizmo, cloud targets no mod
  uses). MaterialData, all PerInstanceData, GpuMaterialSystem, GlobalShaderBindings, RenderTarget and
  StaticObjectModel are byte-identical. `PartTreeRenderData` highlight bits remain 0/10/11/12 and paint
  bits 13–30 are clear (real-DLL check). Paint/canopy/clutter shader anchors match once in CURRENT
  GLSL. Graffiti's live Global/TextureSet headers grew compatibly; native compilation is untested.
- **Vehicle/physics/editor:** eternal-flame, garrys-torch, the-tick, kitchen-sink, godzilla,
  dent-wizard, zippo, its-so-shiny, pyro, i-feel-seen, hot-pursuit, camera mods and free-fallin keep
  their targets; changes there are renames or equivalent refactors. Iron Man transpiler anchors and
  the 11-argument Part constructor are unchanged; `PartCloner.Construct`'s new module append runs
  after the copy postfix. RocketCore remains a stateful module, so Blinky's core enumeration holds.
- **Clutter/terrain/celestial:** 5885 moved planet BVH buffers only; the 8 → 5 viewport arrays are
  walked generically by Pebbles retirement; ring renderer changes are renames; `Eccentricity < 1`
  became `IsBound()` without changing orbit-type selection.
- **UI/input/saves:** the only new native binding is Ctrl+F6 (no mod uses F6 or Ctrl). StarMap entry
  points, `GameSettings.OnKeyAll`, `DrawProgramMenusHook`, `Program.Dispose`, the DrawUI gate counts and
  the native save/load entry points are unchanged. `XmlHelper` still scans types, so Iron Man's
  isolated serializers pick up new Mission/gimbal/power save data.
- **Assets/schema:** Content IDs+keys 8,443 → 8,555 (25 removed, 137 added); none of the removed keys
  is referenced by the repo. Mod XML (Beverage Cooler, Wing Walker, BYO Music) uses no changed
  schema element; every external reference resolves.

## Save compatibility

Sidecar IDs, versions and payloads are unchanged. Iron Man writes the same v1 anchor envelope; the new
guard only prevents native load from deleting its connection references. Saves made by 5901 **before**
this fix that already lost anchor connections cannot be repaired automatically (the native save no
longer contains them). Pebbles recipe records are unchanged; Apply/replay work again. 5862's
UniverseTime fields are native-owned.

## Known-broken baseline reconciliation

Nothing from earlier baselines is relabelled as a 5901 regression. The old skill's
`Controller.___Transform`/Zippo Color/nullability/IvaForceRender items remain retired. Humble Arteest's
unresolved immediate-white report is unchanged by this hop (paint bits and anchors intact). Prior
pending native acceptance remains pending.

## Validation and native acceptance

Commands (repository root, no environment overrides for compilation):

```sh
dotnet build ksa-mod-experiments.slnx --no-incremental
# Execute every *.tests/bin/Debug/net10.0/*.tests.dll and you-didnt-see-anything.patch-tests.
dotnet wing-walker.native-tests/bin/Debug/net10.0/MeowSci.WingWalker.NativeTests.dll --allow-il-header-retarget
python3 tools/check_runtime_bootstrap.py --entry-assembly unscience/bin/Debug/net10.0/MeowSci.Unscience.dll
UNSCIENCE_DIST_DIR=<scratch>/dist/ dotnet build ksa-mod-experiments.slnx   # CI-style output location only
python3 scripts/check-dist.py <scratch>/dist
```

Final results on 2026-11-20:

| Check | Result |
|---|---|
| Clean `--no-incremental` plain whole-solution build (adjacent `../ksa-game-assemblies/current/dll/`) | **PASS — 0 warnings, 0 errors** |
| Managed executables | **23/23 PASS**: byo-music, camera-saves, dent-wizard, doh, garrys-torch, godzilla, iron-man, iron-man-flight, iron-man-mode, kitchen-sink, kitten-animations, ksa-upgrade, pebbles, pyro, save-yourself, saves, sphinx, the-tick, war-paint, wing-walker, world-saves, you-didnt-see-anything, you-didnt-see-anything.patch-tests; no SKIP/WARN lines |
| PREVIOUS-DLL negative control | **PASS**: `KSA_DLL_DIR=<PREVIOUS>/dll` passes the 5801 checks and rejects the first 5901 check |
| Production-code negative controls | **PASS**: removing the Pebbles guard reproduces `Viewport 1 not registered for clutter rendering`; removing the stale-connector guard or the `EnsureDerived` call fails exactly the new checks |
| Wing Walker native XML harness | **NOT RUN to completion**: it loads the real KSA.dll, but `GameContentTags.Build` needs game-shipped third-party assemblies (Tomlet 6.2.0, MemoryPack.Core 1.21.4, Microsoft.Extensions.Logging.Console 10.0.0) that `current/dll/` does not contain. Its stale 5801 animation-signature assertion is updated, and it now asserts one Control per Wing Walker mount; the new `<Control />` is therefore not natively deserialized here (it matches the stock pods' declaration) |
| Runtime bootstrap / actual built entry module | **19/19 PASS**, initializer present |
| CI-style distribution build + validator | **PASS**: one Unscience mod, 34 explicit feature assemblies, cooler/chair asset budgets |
| `git diff --check` | **PASS** |
| Native game/editor/Vulkan acceptance | **NOT RUN**: no 5901 installation |

`ksa-upgrade.tests`' free-fallin live-shader check previously found Content only through a run-time
`KSAFolder` variable and silently skipped on a plain run; it now uses the probe's build-time fallback.

New regressions: `Ksa5901Checks` (real DLL: Rockets/Core, `DropStaleConnectors` and both load
callers, RecomputeRocketControls Zero reset via EnsureDerived, clutter slot resolution/guard, Crew
Clock draw/anchor; the 5801 DLL fails it as a negative control), `StaleConnectorChecks` (production
guard through real Harmony), the 5807 derived-flush case in `RcsCacheChecks`, the compacted-slot
residency fixture, and the Crew Clock gate policy.

Native acceptance still required:

- Load a 5801 world and blueprint with connected Iron Man anchors in 5901: no yellow dropped-connector
  alert, equipment still on the anchors; save/reload, copy, undo/redo.
- Iron Man Enable/Disable on the Identity EVA frame and with an explicit control part: RCS authority
  present immediately after each toggle.
- Pebbles Apply, Restore and save-load replay on Earth/Mars with secondary viewports open and closed,
  part thumbnails and crew portraits visible.
- F2 with You Didn't See Anything, Hide Gauges on/off and Detached-only, with Life Support hazards
  so the Crew Clock appears.
- Vehicle Paint under the new tint/rim highlight in every blend mode; Sphinx statics with SSAO and
  screen-space cloud shadows; Graffiti native shader compile and decals under overcast skies.
- Kitchen Sink IVA force-render on HM-2/HM-3 inverted interiors; Kitten Animations forcing a jump clip.
- Wing Walker rider-only craft: controllable with an able rider, not with an empty/incapacitated seat;
  "Control from here", decoupling/breakup vessel names and a pod-plus-rider craft's control priority.
- Blinky diagnose output on liquid and solid engines.
- All earlier pending native acceptance and Purr2D2 coexistence checks remain open.
