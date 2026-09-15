# KSA 5438 — reconcile local Kitchen Sink work with upstream

## Result

Local `main` was fast-forwarded from `d251bd8` to upstream `4685c7f` after fetching origin.
The pre-existing tracked and untracked edits were captured in a retained Git safety stash,
then reapplied. Local work remains uncommitted. The reconciliation preserves the G-load
feature, both save records, test project, Flexo removal, maintenance rules and investigations.
Safety snapshot: stash commit `faafaa43b3a22f5f6191df09b8ad1399e5544a7f`
(`Safety snapshot before reconciling local Kitchen Sink work with KSA 5438`).

The three textual conflicts were documentation-only:

- Root README: retain upstream's 5438 compatibility summary and local feature/save guidance.
- FULL_SCOPE: retain the concise 5438 baseline and upgrade findings, then include Kitchen Sink.
- Master index: retain Blinky's new typed FlowOrder status and remove Kitchen Sink's retired
  RecomputeStaticMass reflection row. Add the new detector to the actual reflection watchlist.

Both `ksa-upgrade.tests` and `kitchen-sink.tests` remain in the solution. Upstream's production
upgrade fixes are retained, including shared IVA dent-aware submission and the native hash assembly
reference required by saves. No local production-code change was needed for the new game build.

## Verified inputs and scope

| Input | Value |
|---|---|
| CURRENT | `../ksa-game-assemblies/current`, **2026.9.10.5438**, metadata 2026-09-15 |
| PREVIOUS | `../ksa-game-assemblies_prev/current`, **2026.9.7.5402** |
| Local pre-reconciliation baseline | 5402, equal to PREVIOUS |
| Upstream baseline | 5438; full upgrade audit in [KSA_5438_UPGRADE](KSA_5438_UPGRADE.md) |
| Windows install | `C:/Program Files/Kitten Space Agency/KSA.dll` file/product build **2026.9.10.5438** |
| Actual build references | Explicit `KSAFolder=C:/Users/Alex/repos/meow-sci/ksa-game-assemblies/current/dll/` |
| Authoritative sources | Both supplied sibling trees; the in-repo historical decomp was not used |

Upstream already reviewed the full 5403–5437 changelog and source/Content pair ending at 5438.
This follow-up audits the additional local integration and checks the combined solution. The
new feature adds no shader, GPU byte-offset, content asset or collision-shape dependency.

## Local integration findings

Paths below are relative to each source tree's `current/decomp/KSA`.

| Contract | Evidence in OLD → NEW | Verdict |
|---|---|---|
| Private static `PhysicsBubble.DetectStructuralFailure(VehicleUpdateState)` | :782–821 → :873–912, entire method source-identical | Keep exact signature and single-getter transpiler. |
| `StructuralLoad.GLoadFraction` getter | StructuralLoad.cs:15, entire file unchanged; consumed at detector :799 → :890 | Keep filtering only comparison input; telemetry and pressure branch remain native. |
| Detector scheduling/part-failure precedence | `FullPhysicsEndFrame` entire body source-identical; PartFailure.Detect still immediately precedes detector | No new hook. Native segmented-physics/contact behavior still needs acceptance. |
| Vehicle identity/liveness | VehicleUpdateState.cs:14 still readonly Vehicle; Vehicle.cs:617 still IsDisposed; shared provider unchanged | Keep concurrent exact-reference registry and shared saved-ID resolver. |
| Native G thresholds | VehicleStructuralLimits.cs and StructuralLoad.cs unchanged | Existing threshold/contact/pressure fixture remains valid. |
| Editor refresh | PartTree.ReinitializeDerivedValues(ModuleStateList), :308 → :321, body source-identical | Keep existing refresh. |
| Shared IVA | Upstream targets common PartModel.AddInstance with PerInstanceDent and appends paired dent entries | Preserve upstream helper; both local hosts still install it. |
| Save/load | Separate version-1 `kitchen-sink` bool and `kitchen-sink-g-load` string array; same shared lifecycle | No payload migration. Reset/rebind, legacy/missing/invalid/unavailable-target behavior retained. |
| Removed Flexo tests | No remaining production KitchenSinkSolverPatch or FlexoPartTest/FlexoSubpartTest consumers | Keep removal; do not revive stale reflection or solver documentation. |

Test fixture provenance and current integration tables now cite 5438. The two local investigations
remain historical 5402 research; their source links now point to PREVIOUS so line citations do not
accidentally refer to the upgraded source tree. No physics experiment was rerun for this task.

## Validation

Whole-solution `dotnet build ksa-mod-experiments.slnx --no-incremental -m:1 -nr:false
-p:UseSharedCompilation=false` passed: **71 projects, 0 warnings, 0 errors**.
Distribution was redirected with `UNSCIENCE_DIST_DIR` into
`scratchpad/ksa-5438-reconcile/dist`, outside the live game mods folder.

All **14 managed test executables passed** using `dotnet run --project <project> --no-build`:
`garrys-torch.tests`, `kitchen-sink.tests`, `pebbles.tests`, `godzilla.tests`, `byo-music.tests`,
`pyro.tests`, `sphinx.tests`, `iron-man.tests`, `iron-man-flight.tests`, `iron-man-mode.tests`,
`saves.tests`, `world-saves.tests`, `camera-saves.tests`, and `ksa-upgrade.tests`.
Kitchen Sink reports **54 G-load checks and 26 save/restore checks**. The latter exercise the
production adapter, JSON, resolver and coordinator through repeated/cross-scene rebinding,
legacy/vanilla reset, invalid/missing/ambiguous targets, patch failure and retained records.

Build log: `scratchpad/ksa-5438-reconcile-build.log`.
Suite logs: `scratchpad/ksa-5438-reconcile-<project>.log` (ignored local artifacts).
`git diff --check` passes; no conflict markers or unmerged index entries remain. All edits
are unstaged, matching the original working-tree state; no commit or push was performed.

## Native acceptance remaining

No KSA session was launched. Verify selected/unselected vehicles under contacts and high G loads,
pressure/part damage, debris/bubble transitions, row removal, typing and hidden-HUD updates;
native modded/vanilla and repeated save loads; and shared IVA rendering with dent-aware parts.
The wider upstream render/flight acceptance matrix remains open. Managed fixtures and matching
installed version metadata do not establish native patch installation or runtime behavior.
