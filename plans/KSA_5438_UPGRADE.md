# KSA 5438 upgrade — impact, remediation and acceptance

## Verdict and inputs

The supplied update introduces compile-visible API changes and several silent render/asset
regressions. This change migrates those integrations on the existing branch without committing.
**Static and managed verification are distinct from native acceptance: no live KSA pass was run.**

| Input | Verified value |
|---|---|
| CURRENT | `../ksa-game-assemblies/current`, build **2026.9.10.5438**, metadata date 2026-09-15 |
| PREVIOUS | `../ksa-game-assemblies_prev/current`, build **2026.9.7.5402**, metadata date 2026-09-02 |
| Recorded baseline | **5402**, equal to PREVIOUS; no missing intermediate baseline span |
| Authoritative sources | Both provided `decomp` and `Content` trees, not the in-repo historical decomp |
| Build DLLs | Explicit `KSAFolder=/Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/dll/`; this macOS checkout's default resolves the same tree |
| Native installation | No separate native install used or validated; supplied KSA DLL cannot load for execution in this ARM64 macOS process |
| Distribution | `UNSCIENCE_DIST_DIR=/tmp/unscience-ksa-5438-dist`, keeping build checks out of live mod folders |
| Dependencies | .NET SDK 10.0.100; StarMap.API 0.3.6, Lib.Harmony 2.4.2, Tomlyn 0.19.0 unchanged |

The current solution/test inventory supersedes the skill's old project counts and “no tests” claim.
Only Unscience is distributed; development hosts and managed test projects remain part of the build.

## Verification results

**Final whole-solution build passed: 70 projects, 0 warnings, 0 errors** against CURRENT's DLLs.
The non-incremental serial build completed in 1m11s. **All 13 managed test executables passed**:

`byo-music.tests`, `camera-saves.tests`, `garrys-torch.tests`, `godzilla.tests`,
`iron-man-flight.tests`, `iron-man-mode.tests`, `iron-man.tests`, `pebbles.tests`, `pyro.tests`,
`saves.tests`, `sphinx.tests`, `world-saves.tests`, and the new `ksa-upgrade.tests`.

New regression coverage links production code for Blinky's selected drain-view traversal, Free
Fallin's per-canopy material ownership/restoration through managed Harmony, and Parts Now's V8
definition exclusions. Pyro's managed suite additionally covers throttle pressure, the native
refraction ramp and template aliases. Existing suites cover timing, save/lifecycle, transforms,
collider/layout guards and scoped Harmony behavior. These fixtures do not execute the supplied
native KSA assembly, GPU rendering, or native resource-graph construction.

Final build output: `/tmp/ksa-5438-build-final.log`. Test output:
`/tmp/ksa-5438-test-<project>.log`. `git diff --check` passed. Changes remain uncommitted on `main`;
the pre-existing untracked `plans/KSA_COLLIDER_SUBTRACTION_RESEARCH.md` was preserved.

The first compile gate failed in shared save preflight/hooks with CS0012 for `KeyHash`'s new
assembly owner. After adding that reference, the next gate exposed Blinky's drain-order type,
Pyro's state/renderer/nozzle math APIs, and Vulkan enum spellings. These are real API changes;
the reference path was verified and includes its trailing separator.

The default parallel build stalled without producing diagnostic output in this sandbox and was
stopped. Subsequent checks use serial MSBuild, no node reuse and no shared compiler:

```sh
KSAFolder=/Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/dll/ \
UNSCIENCE_DIST_DIR=/tmp/unscience-ksa-5438-dist \
DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 \
dotnet build ksa-mod-experiments.slnx --no-incremental -m:1 -nr:false -p:UseSharedCompilation=false
```

A metadata-only PEReader check of **16 current binary types** confirms the exact private static/
dynamic part submission overloads and argument names, depth-resolve signature, plume update/final
submission signatures, PrepareFrame, hotkey signature, native material/instance field lists and
KeyHash assembly ownership, resource drain-view accessors, canopy material fields, and the expanded
ExhaustInstance field list. This does not execute game code or claim native Harmony installation.
Direct assembly loading was rejected as architecture-incompatible; managed fixtures supply the
behavioral tests, while actual native patch/render acceptance remains in the matrix below.

## Changelog and complete source span

CURRENT logs **35 commits: 5403–5437**, fromRevision 5402/toRevision 5438. There is **no individual
5438 changelog entry**. OLD logs only 5401, but OLD already matches the baseline. Source comparison
covers both endpoints regardless of log coverage: **165 KSA C# files changed, 43 added, 5 removed**;
all decomp files combined: 686 changed, 178 added, 55 removed (includes generated Brutal bindings).
Content: **25 changed, 4 added, none removed**; an ID can still disappear inside a changed XML file.

| Revisions reviewed | Relevant change / suite exposure |
|---|---|
| 5403 | Galactic-plane coordinates/debug overlay; camera transforms reviewed, no consumed API break |
| 5404, 5406 | Vessel mesh dents: new submission overloads and parallel dent buffers; paint/emissive/IVA |
| 5405, 5429–5431 | Display selection, abandoned frames, swapchain lifecycle; camera and custom renderer native watch |
| 5407, 5408, 5415, 5417, 5425, 5435 | Explosions/audio/translucency/bloom; Parts Now registries and Graffiti resolve ordering |
| 5409–5411 | Collider-volume crash limits, contact-point budgets, authored light/battery mass; grids/welds/scaling |
| 5412, 5413, 5419 | Profiler/KeyHash move and Brutal packages; explicit assembly and enum fixes |
| 5414, 5432 | Solid-motor performance/mass and staged delta-v; existing public recompute paths remain compatible |
| 5416, 5426, 5427 | Flight-plan point ownership and relative-state numerics; orbit weld/frame reads checked |
| 5418, 5422 | Canopy material choices/mesh and MMU texture correction; Free Fallin assets/restoration |
| 5420, 5423, 5424, 5428 | Mid-step bubble merges, debris lifecycle/save and fictitious forces; native physics acceptance |
| 5421, 5436 | Exhaust merging, new template/physics data, renderer-owned calculations; Pyro migration |
| 5433 | Shared flat resource drain paths and early stopping; Blinky diagnostics/view traversal |
| 5434 | Non-root part scales restored during native load; existing size/save adapters inherit fix |
| 5437 | Terrain sampling optimization; clutter/terrain-dependent placement inspected, live surface acceptance |

## Confirmed findings and fixes

All findings below are **new in CURRENT versus PREVIOUS**, except explicitly labeled standing issues.
Decomp paths are relative to the named input's `current/decomp`; asset paths to `current/Content`.

### 1. Native identity hash assembly moved — toolchain / assembly reference

OLD defines KSA.KeyHash in KSA.dll; CURRENT defines the same namespace/type in **Planet.Render.Core.dll**
(rev 5412, confirmed in binary metadata). `ksa-abstractions.lib/Persistence/NativeSavePreflight.cs:41`
and `NativeSaveHooks.cs:150` require its assembly when accessing native hashes. Add a compile-only
Planet.Render.Core reference to the shared project. Other directly referencing libraries already
carry it. Blast radius: save dependency checks and every project depending on abstractions.
**Verdict: code change required, implemented; compilation verifies binding.**

### 2. Blinky drain orders became views — retyped API and diagnostic semantics

OLD `ResourceManager` consumption orders expose arrays of levels. CURRENT
`KSA/FlowOrder.cs:5` provides `LevelCount`, indexed spans, reversed distance and same-stage filtering.
Rev 5433 can retain a level whose selected span is empty. Blinky's two feed checks and Diagnose
used nullable arrays/Length/foreach, which fail compilation and could misclassify empty levels.
`blinky.lib/PropellantFeedDiagnostics.cs` now counts actual entries through the selected view;
`BlinkySubmod.cs` and `LcdGridBuilder.cs` share it. Existing declared feed connectors stay intact.
**Verdict: implemented; native ignition, resource consumption and pixel output still need live checks.**

### 3. Vulkan index enum spellings — toolchain

The shipped wrapper renamed `VkIndexType.Uint16/Uint32` to **UInt16/UInt32**. Values and index widths
remain unchanged in both enum definitions. Update `graffiti.lib/DecalRenderer.cs`,
`thug-life.lib/ThugLifeQuadRenderer.cs`, `pebbles.lib/Preview/PreviewScene.cs` and
`sphinx.lib/StaticModelResources.cs`. **Verdict: implemented; no pipeline redesign required.**

### 4. Part submission overloads and dent alignment — reflection / render semantics

OLD `PartModel.AddInstance` and `PartModelDynamic.AddInstance` each had one submission overload.
CURRENT `KSA/PartModel.cs:459–470` and corresponding dynamic code add a public dent-span wrapper and
a private common sink taking `(PerInstanceData, PerInstanceDent, IViewport, int)`. Both wrappers call
that sink. Name-only Harmony lookup is ambiguous; selecting only the legacy wrapper also misses
stock dent-aware draws. Humble Arteest paint/emissive and shared IVA now bind the exact common sink.

IVA's editor-only insertion also appends the matching `DentInstanceList` entry; otherwise the
instance and dent-buffer counts diverge. The existing visibility/shadow-proxy gates remain.
Paint's static argument is `instanceData`, dynamic is `inInstanceData`; binary metadata confirms
those names. `StateBitFlag` bits 11–31 are still unowned by KSA. **Verdict: implemented; live paint,
emissive/temperature/TFI, dents and IVA acceptance required.**

### 5. Free Fallin material removal and restoration — asset / ownership

OLD `Core/ParachuteAssets.xml` defines `ParachuteCanopy_Material`; CURRENT removes that ID inside
the file and defines six authored choices. CURRENT `Parachute.CanopyMaterial` and
`ChuteRenderable(ChuteClothTopology,string? materialId)` preserve each native selection; OLD
construction forced one material. The mod's old Apply lookup therefore throws, and restoring one
hardcoded handle would discard native choices.

`CanopyMaterialController` uses the verified current orange-checker material as the explicit baseline
for the global custom material. `FreeFallinPatches` captures a separate original handle per weakly
tracked renderable before replacement, tracks the last applied handle, and restores owned values
on disable/load cleanup/unload. It releases tracking so later native choices are captured afresh.
**Verdict: implemented; all native variants, projection and late deployment need live acceptance.**

### 6. Graffiti now sees two resolves — signature-compatible silent behavior change

OLD `KSA.Rendering/RenderTarget.cs:315` takes only CommandBuffer; CURRENT adds optional
`bool inResolveDepth=true`. OLD main flight has one resolve. CURRENT `Program.cs:4737` resolves
color only before bloom/underwater, then `:4765` resolves depth before GridPass. The old postfix
would draw twice, first with stale depth. `GraffitiPatches` binds the explicit new signature and
returns when depth is not resolved, retaining main-target/main-view/editor filters.
**Verdict: implemented; one native composite with MSAA/bloom/underwater needs live confirmation.**

### 7. Pyro renderer/physics redesign — API, semantic drift and assets

OLD UpdateState returned bool and accepted PlumeData; CURRENT returns void, receives gas/velocity/
emitter/rest/air state and exposes IsLive. Native PlumeData adds mass flow, core velocity and
stagnation pressure; the old copied initializer is invalid. The renderer replaces six-argument
AddInstance with native instance/bend/fade submission and initializes expanded shader structs.
Use native complete physics and transient calculations, maintaining part→assembly→body→ego frames,
last active shutdown state and independent plume submission inside the stock grouping window.

CURRENT AddInstanceCore overwrites absorption/refraction from the template after copying private
_shaderData; the old reflection write would silently lose controls even after compilation succeeds.
Apply per-plume controls only to final submitted ExhaustInstance within the mod's submission scope,
with exception-safe cleanup and no shared-template mutation. Fractional throttle affects synthetic
chamber pressure after native throttle modifiers were removed.

OLD ExhaustAssets defines EngineAVernier/EngineATurbine; CURRENT replaces both with
**EngineAAuxiliary**, and CorePropulsionAGameData explicitly maps both nozzle classes to it.
Migrate those legacy IDs centrally for entries/presets/scene restore and update fallback discovery;
unknown IDs continue to fail explicitly. `RocketNozzle.RecomputeGasVisibilityDensity` becomes
`RecomputeVolumetricExhaustLimits`.

**Verdict: migration implemented; native controls, tails, grouping and template replay require live
acceptance.** The separate native `_hasRefractionInstances` defect is discussed below.

### 8. New explosion registries bypass runtime rollback — asset schema

OLD AssetBundle has no explosion definitions; CURRENT `KSA/AssetBundle.cs:70–71` accepts Explosion
and ExplosionVolume. `ExplosionVolumeTemplate.OnDataLoad` and
`KSA.Rendering.Particles/ExplosionReference.OnDataLoad` register in new collections, which Parts
Now does not snapshot, purge or restore. V8 now rejects these **top-level definitions before
registration**; same-named nested references are allowed. This prevents stale registered entries
following failure/unload/reload. **Verdict: implemented; no expanded runtime registry ownership.**

## Every feature / shared area reviewed

| Feature / shared surface | Result after migration |
|---|---|
| Unscience shell, abstractions | Hash reference + IVA sink/dent fix; hotkey, StarMap hooks and solver handoff retained |
| Eternal Flame | Refill/solver APIs unchanged; old during-burn symptom remains open |
| Garry's Torch | Teleport changes only log source line; scale/avatar/collision hooks retained; segmented-physics live check |
| Godzilla | Visual-radius, world matrix, collider scale/rebuild hooks retained; native scaling/load/bubble check |
| i-feel-seen | Vehicle render/world-matrix and camera ego contracts unchanged |
| Kiwis Marbles | SetOrbit, child reparenting, frame reads and solver timing retained; native orbit/weld check |
| Zippo | Light nested types/fields and Disco template ownership unchanged; authored light mass changed |
| Glass | FOV field and projection methods unchanged |
| Camera Controller Override | Controller.Camera + OnFrame arguments retained; decompiler-local renames only |
| Hot Pursuit | Fixed controller, registry lease and raycast interfaces retained; secondary rendering native watch |
| Blinky | Flow-order migration; native feed/ignition and dense grid acceptance |
| Its So Shiny | LightPart, connection, battery and render-skip signatures retained; native density/mass check |
| Thug Life | Vulkan spelling; unlit shaders, RenderMainPass and MSAA/reverse-Z interface retained |
| DOH | Avatar/material reflection and spawn waits retained; native spawn/recolor acceptance |
| Humble Arteest | Common render sink fix; paint transport/shaders/material/visor invariants retained |
| Kitten Animations | All reflected animation fields/processor ownership and UpdateAnimation retained |
| Free Fallin | Current material ID + per-canopy restore; native cloth/projection acceptance |
| Graffiti | Final-depth resolve only; canopy triangles/picking and shader headers retained |
| Pyro | Physics/submission/appearance/template migration; native rendering acceptance |
| Rocky McRock Face | Ring mesh/catalog/reflection and renderer rebuild contracts retained |
| Bloomin Onion | Ring creation/painted texture/distant shadow contracts retained |
| Parts Now | New explosion schema guard; registry/headroom/thumbnail descriptors compatible |
| Don't Stifle Me | Scale/quantize/diameter targets retain bodies; native editor acceptance |
| Kitchen Sink | Shared IVA fix; private RecomputeStaticMass/solver hook retained |
| Skittles | Current Brutal ImGui compilation; native style/input acceptance |
| BYO Music | Current FMOD wrapper compilation and stream/listener contracts retained |
| Pebbles | Vulkan spelling; shaders/graphs/materials/staging contracts retained |
| Sphinx | Vulkan spelling; static renderer and per-simulation collision hooks retained |
| Iron Man | Constructor/serializer, flight/gauge/render/editor/teleport invariants retained |
| Scene-save adapters | Native save hooks, ordered cleanup/replay and dependencies retained; native roundtrip pending |

The template hosts and development references remain compile/discovery inventory, not newly shipped
features. There is no telemetry.md or standalone-mods.md in the current scope tree; current feature
areas and audio.md supersede the stale skill filenames.

## Reflection and Harmony watchlist reconciliation

“Retained” below means both source trees were checked, including field versus property and overload
shape where relevant. It **does not** mean native patch installation or visual behavior was executed.

| Watch entry (including supplemental area entries) | 5438 result |
|---|---|
| Controller OnFrame / public Camera | Retained; old ___Transform injector remains retired |
| Camera._fovRadians, ChangeFieldOfView, UpdateProjection | Retained |
| Vehicle.GetWorldMatrix / UpdateRenderData / private UpdateCollisionGeometry | Retained |
| VolumetricExhaustTemplate.References | Retained; catalog aliases changed |
| VolumetricExhaustInstance._shaderData | Field retained but old writes semantically ineffective; replaced by final-submission control |
| PartModel / PartModelDynamic.AddInstance | New overloads; explicit common sinks required |
| KittenRenderable.ModelToBodyMatrix / _characterAvatar / CharacterAvatar.Core.Scale | Retained; Core remains value-type field |
| ChuteRenderable._renderable / AnimatedRenderable.MaterialIndices | Retained; original ownership tracked individually |
| CharacterAvatar.Core model/fur/attachments MaterialIndices | Retained |
| CatExpressionAnim._expressionPose | Retained |
| KittenRenderable ground/ladder/jump/flail/moon/swim/seated clips and pair/blend samplers | All 17 catalog fields retained |
| KittenRenderable personality/reactive expression, eye/ear fields; AnimatedRenderable.UpdateAnimation | Retained |
| KSA.LightModule+TemplateData / PartTemplate.Components / Intensity / ColorRgb / FloatReference.Value | Retained; old Color lookup remains retired |
| ColorRgbReference R/G/B/OnDataLoad | Retained; only XML formatting added |
| Program.Instance / MaterialSystem / SuperMeshRenderSystem / CharacterRenderSystem | Retained |
| GpuObjectSystem BigBuffer/DeviceCtx/CreateObject; AssetManager AssetMap/GetOrLoad; GpuObjectAssetRef.Handle | Retained |
| GpuTextureSystem and Pbr/Character reference bridge | Retained; DisplayName added without changing material resolution |
| ModLibrary AllParts/AllCharacters/AllMeshes/AllFiles/AllGltfs/AllMaterials/AllPartGameDataReferences/AllEditorTagDefinitions | Retained; new AllExplosions deliberately unowned/rejected by Parts Now |
| SerializedCollection GetList/Find/private _collection | Retained; concrete ConcurrentDictionary unchanged |
| VehicleEditor._editorTagLookup | Retained |
| MeshReference HostPrimitives/PrimitiveMaterialIds private setters/backing field | Retained; bounds additions do not change property ownership |
| Program._planetTransparenciesRenderer / _ringsRenderer / _ringRendererCreated / _anyRings | Retained, whole renderer file identical |
| TextureReference.TextureAsset backing field; Texture/ImageView/BindlessHandle setters | Retained |
| StaticCelestial._distantRenderer / DistantSphereRenderer._data + ring shadow fields | Retained |
| VehicleEditor ScaleBoundsFor/UpdateSelectedScale/UpdateScaleGizmo/QuantizeScale/ForEachPartWithSymmetry | Retained |
| VehicleEditor.DrawParachuteSection / Parachute.SetDiameter | Material combo added; clamp transpiler anchors and SetDiameter unchanged |
| PartTree.RecomputeStaticMass | Retained; retired Part matrix writes stay retired |
| Blinky graph diagnostics | Old reflection row obsolete; typed FlowOrder is current |
| GameSettings.OnKeyAll / Program.OnDrawUiConsole | Bodies retained |
| Program.PrepareFrame ordered seven seams / Universe.ExecuteNextVehicleSolvers | Retained; input polling moves before handoff |
| ShaderReference properties/backing field/DoLoad/CompileVariantWithCustomOptions; ShaderModuleUtils.FromFile | Retained; current paint patches compilation rather than obsolete module swapping |
| Iron Man private editor methods, Part ctor/serializer, Ctrl2Body, orbit frame/pan, ThrusterController map | Retained; exact call-count predicates checked |
| Iron Man gauge EVA checks/generic policy calls and teleport helper | Retained; exact 2/1/1 match invariants checked |
| Celestial BodyTemplate backing field / Astronomical.bodyTemplate | Retained |
| GroundClutterRenderer _renderPassInfo / _planetClutterMaxBoundingRadius; PlanetRenderer creation flag | Retained |
| Universe._vehicleUpdateTask / VehicleUpdateTask.SyncWindowBubbles | Retained; drain window still joins workers |
| GroundClutterPlacementData._exclusionCache / GroundClutterLodReference.BuildMaterialIndirection | Retained |
| StagingPool._submitted/_commandBufferIndex; ClutterEcotypePhysicalData shape lists | Retained; fence pooling inspected against failure cleanup |
| StaticObjectRenderer private color/prepass overloads | Retained; native static shaders identical |
| NarrowPhaseCallbacks.AllowContactGeneration / Sim / one BepuHandles.IsGroundSurface call | Retained |
| Native save hook and supplemental adapter lookups | Retained; lifecycle ownership and reflection paths checked in saves area |

## Shader, byte-layout and behavioral invariants

- MaterialData remains sequential pack 1: **80-byte stride, AlbedoColor offset 16**. Static/dynamic
  PerInstanceData remain **80 bytes**, StateBitFlag at 64. Static emissive at 68; dynamic temperature
  at 68/TFI at 72; wetness at 76. Game flag producers/readers stop at bit 10. Dent fields use
  separate descriptor buffers (DENT_SET 3, bindings 1/2), not paint's high bits.
- MeshIndirect and raytraced fragment shaders, UnlitMesh vert/frag, Model/Model_Skinned/ModelPbr,
  ModelTranslucent/Fur and shared material/camera/texture headers are byte-identical. Paint,
  emissive/TFI and canopy projection anchors remain valid; native vertex deformation stays stock.
- ExhaustInstance gains trailing fade/shape/sample fields and shaders gain shape-LUT/VolumeBounds
  contracts. Pyro delegates construction/upload to native renderer code instead of assuming old size.
- All **19 ground-clutter shaders, five clutter XML graphs and three static-object shaders** are
  byte-identical. Pebbles/Sphinx layouts and ring rebuild/property ownership remain compatible.
- ThumbnailRenderResources expands native storage bindings for dents; Parts Now already uses this
  class directly. Shared interleaved-buffer headroom still reserves after LoadAll and before Bind.
- Stock A2–A6 engine IDs, LightPart, kitten assets, ring assets and unlit shader IDs remain. Removed
  consumed IDs are the old canopy material and two exhaust templates described above.
- Worker scheduling now permits segmented bubble merge/split. Shared main-thread mutation handoff,
  Sphinx per-simulation ownership and Torch per-pass shape suppression retain their seams. Only native
  flight can establish collision behavior under the new scheduler and crash/deformation model.

## Known-broken baseline reconciliation

| Historical item | Current evidence / status |
|---|---|
| Camera ___Transform injector | Already retired; current code uses Controller.Camera. No new regression. |
| Zippo Color field | Already fixed to ColorRgb. No new regression. |
| Vehicle Paint dead since 4693 | Obsolete diagnosis: rebuilt for 5018. New overload regression fixed here; live rendering pending. |
| Garry's Torch nullability compile error | Already fixed; no recurrence in this build. |
| Missing shell IvaForceRender.Patch | Already wired at unscience/Patcher.cs:84; new overload/dent integration fixed here. |
| Blinky propellant feed | Existing declared-connector repair retained; new diagnostic view migration completed. |
| Kitten animation repetition | Existing owned processor/clip fix retained; reflected members unchanged. |
| Eternal Flame fuel during burns | Still open. UI-timed fuel versus solver-timed electricity remains a reproduction lead, not a new 5438 finding. |
| Torch error spam | Prior actuator/shape-suppression fixes retained; segmented stepping/crash changes require native logs. |
| Pyro refraction pass | Standing native defect: both supplied renderers reset _hasRefractionInstances=false and never set it true. Correct submitted intensity alone cannot establish a visible pass. |
| Old controllability/editor tag/connector/size watches | No new blocking symbol drift; current native gameplay checks remain. |

## Native acceptance still required

1. Launch the built Unscience package on a supported KSA host; verify load logs, every patch group,
   F11 toolbox, typing protection and F2 hidden-HUD updates.
2. Paint static/dynamic parts with and without dents; all paint modes, emissive/TFI/wetness, kitten
   tint and visor hide/show. Verify IVA editor instance/dent alignment and secondary views.
3. Pyro full/fractional/zero throttle, vacuum/atmosphere, on/off tail cycles, rotation, absorption,
   stock-engine isolation, grouping and old preset/sidecar aliases. Treat native refraction separately.
4. Free Fallin all six native canopy choices, global override, PNG/UV/full projection, restoration,
   late deployment, disable/re-enable, unload and scene roundtrip.
5. Graffiti one composite per final resolve, MSAA on/off, bloom/underwater, main versus secondary
   cameras and animated canopy picking. Thug Life quad, engine/light grids and ring textures/LODs.
6. Welds, animations and all Godzilla scale/collider combinations through crashes, debris removal,
   bubble merge/split/origin shifts, pause/warp; refill during active burns with native logs.
7. Parts Now valid runtime load/unload/reload/thumbnails and V8 rejection; scale/parachute editor;
   Pebbles/Sphinx materials, live edits, collision/grounding and GPU/physics resource retirement.
8. Iron Man EVA/rocket modes, editor/flight-computer/RCS, equipment rendering and surface teleport;
   camera leases/feeds; BYO Music codec/localization/target loss; all scene-save adapters.

Do not interpret a green build or managed fixture result as completion of this list.
