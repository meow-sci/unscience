# KSA 5801 upgrade — impact, remediation and acceptance

## Verdict and inputs

Upgraded the suite to the supplied **2026.11.10.5801** build. Compilation breaks and confirmed
reflection, paint-flag, save-reconstruction and ownership regressions are fixed. Verification is
**static and managed**; this ARM64 macOS host has no native KSA installation or Vulkan game session.
The user's instruction to perform the upgrade authorizes the compatibility fixes identified below.

| Input | Value |
|---|---|
| CURRENT / NEW | `../ksa-game-assemblies/current`, build **2026.11.10.5801**, metadata date **2026-11-07**, `fromRevision=5707`, `toRevision=5801` |
| PREVIOUS / OLD | `../ksa-game-assemblies_prev/current`, build **2026.10.25.5707**; matches the prior recorded scope baseline |
| Changelog | 93 entries, every revision **5708–5800**; **5801 is unlogged**. No earlier baseline gap in this direct hop |
| Sources | Supplied CURRENT/PREVIOUS `decomp/` and `Content/` trees. In-repo `decomp/ksa` is historical and was not used as authoritative |
| Source delta | After normalizing line endings: 2,730 C# files changed, 289 added, 41 removed. Many differences are decompiler formatting/expression changes; raw counts are not semantic change counts |
| Content delta | 129 text files changed after newline normalization; 26 added, 2 removed. Raw comparison flags 558 changed files, mostly newline conversion |
| Build DLLs | Explicit `KSAFolder=/Users/asherwin/wip/unscience-upgrade-ksa/repos/ksa-game-assemblies/current/dll/`; output redirected to `/private/tmp/unscience-5801-dist/` |
| Dependencies | StarMap.API **0.3.6**, Lib.Harmony **2.4.2**, Tomlyn **0.19.0** unchanged. Brutal changed with the game (5770/5775) |

The prescribed skill predates this checkout's managed suites and current integration architecture.
Its statements that there are no tests, that Vehicle Paint is disabled, and that several historical
bugs remain open are obsolete. Current source, tests and dated scope entries take precedence.

## Changelog priorities

| Revisions | Coupling reviewed |
|---|---|
| 5708, 5712, 5756 | DataReference/KeyReference resolution, serialized asset vs keyed component identity, strict XML lists, module template placement; rings, clutter, materials, Pyro, anchors, capsule filter and Disco |
| 5709–5713, 5722–5731, 5735, 5743, 5746–5748, 5752, 5755, 5762–5764, 5785, 5788–5789, 5791–5793, 5797 | Plumbing, habitat supplies, crew control/death, native rules, symmetry lights, outside air, suit/cargo and condition animations |
| 5715–5716, 5742, 5744, 5768, 5778, 5782, 5799 | Editor undo/redo, asynchronous aero-mesh construction, variants/edges, collider snapping, waking off rails, throttle/G-limit behavior |
| 5717–5721, 5732–5733, 5736, 5754, 5757, 5766, 5777, 5781, 5787, 5790, 5794 | Part IDs, removed hatch/docking variants, re-imported colliders and animations; saved external/native asset identities |
| 5737–5740, 5745, 5758–5761 | Terrain blend, curvature masking/baking, render and collision terrain resources — Pebbles |
| 5770–5775 | Brutal collection API and native-memory/shader-stage ownership |
| 5714, 5741, 5765, 5767, 5779, 5796, 5798 | HDR controls, R11G11B10 scene format, shader macros, clouds and prefiltered sky LUT |
| 5749–5751, 5780, 5783–5784, 5786, 5795, 5800 | New body, radiation diagnostics, profiler/input/UI fixes, test scenario change, atmospheric mixtures/heights and runtime SI atmosphere |

## Confirmed findings and fixes

All findings here are **new in NEW relative to OLD**, not the retired 4750 baseline issues.
Paths in the evidence column are relative to each tree's `current/decomp/KSA/`, unless marked Content.

| Consumer / production code | Class and evidence in OLD → NEW | Resolution |
|---|---|---|
| Rings: `rocky-mcrock-face.lib/RingAssetCatalog.cs`, `RingSwapController.cs`; `bloomin-onion.lib/RingReferenceBuilder.cs`, `StockRingAssets.cs`, `RingDefinitionSerializer.cs`, `RingRendererRebuilder.cs` | **Retyped/removed**: OLD `PbrMaterialReference.cs:13,16` has direct textures/TexturePowerReference; NEW `:12,15` has resolved `DataReference<TextureReference>`. Ring material/mesh/band and glTF source links also changed | Build fully resolved links (`GameDataReference.To` sets both Ref and Value); read Value; retain original reference objects for restore. Discover normals through `ModLibrary.AllMaterials` normal slots. Removed native ring-object Name remains a compatibility field in existing presets/sidecars |
| Pebbles: `pebbles.lib/Runtime/Clutter{Capture,Graph,Colliders,GridNative,Resources}.cs`, asset discovery, controller grids | **Retyped/renamed/reflection signature**: OLD `ClutterEcotypeReference.cs:10` has Name; NEW `:11` inherits keyed identity. Biome Alias/collider Id → Key. LOD material lists are resolved; `GroundClutterLodReference.cs:100` `BuildMaterialIndirection()` → NEW `:105` `(string objectId)` | Preserve recipe field names/identities; translate to native keys/resolved refs, fill native readonly lists, pass object ID to the reflection call. Native snapshots still key removed/displaced clutter by the corresponding ecotype key |
| Pebbles: `Models/PebblesRecipe.cs`, `RecipeValidation.cs`, `Runtime/ClutterCapture.cs`, `ClutterGraph.cs`, `ClutterRetirement.cs` | **Semantic drift/ownership**: OLD has no curvature/terrain-baker set; NEW `GroundClutterPlacementReference`, `GroundClutterTerrainData`, `GroundClutterPlacementData`, `ClutterEcotype{Render,Physical}Data` require curve/spacing and own terrain/LUT resources. NEW `GroundClutterMaterialReference` adds TerrainFadeStart/End | Capture/replay optional curvature and blend fields. Missing legacy fields inherit the matching current stock placement/material. Set UsesCurvature from the replayed curve. Interrupted-build cleanup retires reachable terrain data and the curvature bindless slot as well as altitude resources |
| Pebbles imports / workshop | **Toolchain**: new `Brutal.Collections.List<T>` conflicts with managed List; NativeStrideList.FromSpan removed (5770) | Narrow the import; explicitly create/copy/dispose native stride lists, transferring successful buffers to their MeshAsset owners |
| DOH: `doh.lib/Materials/MaterialFactory.cs`; shared `ksa-abstractions.lib/GameDataReference.cs` | **Reflection miss**: OLD `CharacterReference.cs:32` carries a CharacterTexturesReference with Get(); NEW `:32` carries DataReference. Same drift through character material/texture slots. The reflected Get call returned null with no compiler error | Follow Value for links, keep direct assets direct, and keep unresolved links null so fallback behavior remains explicit. Linked production-helper regression covers these distinctions |
| Iron Man: `IronManConnectorPatches.cs`, `IronManAnchorModule.cs`, `IronManAnchorSerializers.cs`, `IronManConnectors.cs` | **Signature/save schema**: OLD `Part.cs:1566` private constructor has 10 args; NEW `:1601` has an added placement arg (11 total). OLD `PartInstance.cs:8` inherits SerializedId/Id; NEW is a plain DTO without Id. Connector template Id → Key | Patch the new private seam, still once per construction. Write the **same v1 JSON/base64 payload** in an `UnscienceIronManAnchors` native module-save envelope. Extend world and blueprint serializer getters with isolated XML mappings; migrate legacy Id attributes on deserialize. Restore before index reconnection; the passive module claims the already-restored record during native replay. Reject empty/duplicate/unsupported/malformed records. Copy-constructor ownership remains intact |
| Vehicle Paint: `humble-arteest.lib/VehiclePaint.cs`, `VehiclePaintShaderSource.cs`, `VehiclePaintShaders.cs` | **Byte/flag collision**: OLD PartTreeRenderData/mesh shaders use bits 0–10; NEW `PartTreeRenderData.cs:223,225` owns bits 11/12 for yellow/purple habitat highlights. Old paint would both corrupt highlights and interpret native highlights as paint | Move paint to bits **13–30**, **6:6:6 sRGB**, leaving native bits 0–12 intact. CPU encoder and injected decoder use the same constants. Saved floating RGB values and record versions do not change; only transient GPU precision changes |
| Pyro: `PlumeTemplates.cs`, `PlumeEmitter.cs`, `PlumePhysics.cs` | **Removed/reflection/unit drift**: OLD `VolumetricExhaustTemplate.cs:56` owns References/Get; NEW `ModLibrary.cs:143` owns AllExhaustTemplates. VolumetricExhaustReference removed; instance ctor accepts the template. OLD PhysicalAtmosphereReference helper returns atmospheres; NEW `Camera.cs:171` / `PhysicalAtmosphere.cs:106,111` expose runtime SI pressure/density | Enumerate the moved registry, use public ModLibrary.TryGet for lookups/fallback, construct from the template. Use runtime atmosphere density and camera pressure directly, removing the 101325 conversion |
| Capsule glass / Zippo Disco | **Key identity**: native module templates now inherit KeyedTemplate, not SerializedId | Capsule filter matches `model.Template.Key` with the same exact window model keys; Disco's private light template copies Key. User settings and save IDs stay unchanged |
| Thug Life, Graffiti, Pebbles workshop | **Native ownership** (5770–5775): VertexInput and ShaderStages now require explicit disposal; native collection finalizers were removed | Dispose temporary vertex-input/stage data after pipeline creation. Current RenderTechnique stages borrow shared shader modules (`ownsModules:false`), so disposal does not destroy stock shaders |
| BYO Music legacy host: `byo-music/Assets.xml` | **Asset schema**: OLD nested MusicFile/Sound declarations, channel/location attributes and flat children; NEW SoundReference/MultiSound/MusicPlaylist use top-level file assets, Ref links, SoundFiles/Sounds arrays and Location/Channel elements | Migrate the legacy host asset to the strict schema, retaining SabotageMusic and SabotageMulti IDs. The bundled direct-FMOD player is unchanged |

## Surfaces checked without another confirmed code break

- **Shared shell/lifecycle:** 57 direct KSA AccessTools name lookups have a corresponding CURRENT
  source name; full compilation clears typed calls. Real DLL tests check the PrepareFrame solver order,
  native detector signature/anchors, Part constructor chaining, all five clutter material-buffer
  binding methods and Sphinx's null-pipeline guard. StarMap's Program UI/menu entry names remain present;
  this does not execute the native loader or third-party patch chains.
- **Reflection watchlist:** real-DLL metadata checks cover FOV, the EVA/avatar chain, Core.Scale,
  material-index fields, expression-pose cache, the 17 ground-animation fields and four existing
  processors, light template members, registry fields, render batches, ring/texture/mesh private
  fields, material-buffer allocator/device and asset maps. Pyro's moved field and DOH's changed
  dereference semantics are fixed above. Static presence is not proof of native patch installation.
- **Layouts:** MaterialData and static/dynamic/glass PerInstanceData field blocks are identical after
  whitespace normalization. MaterialData remains 80 bytes with AlbedoColor at byte 16; paint flags
  retain their position at byte 64. The changed meaning of bits 11/12 required a fix despite identical layout.
- **Rendering:** `Program.cs:227` changes R16G16B16A16SFloat → B10G11R11UfloatPack32. Thug Life calls
  OffscreenTarget.SetupGraphicsPipeline; Graffiti uses Program.ColorFormat. Both already derive
  target compatibility. Pebbles workshop owns a separate preview format. UnlitMesh shader files are
  unchanged. Both paint shaders still have their albedo anchor; production transforms pass all three
  blend modes under LF/CRLF. Full Canopy production-parser checks pass against CURRENT shaders.
  New Common/Global sky-LUT data replaces padding without changing the descriptor scheme; Graffiti
  uses live headers and its own explicit global/depth/texture set ordering.
- **Assets:** a Content Id inventory changes 8,240 → 5,507 IDs (3,448 disappear, 715 appear), largely
  because inline component Ids became Keys. The only removed exact production string literals that
  are actual asset dependencies are the two capsule model identities, now correctly matched as Keys.
  Engine A2–A6, LightPart, KittenBackPackPart, stock ring and shader IDs still exist. Removed/reimported
  native part IDs can invalidate old craft; existing native missing-part handling and Unscience
  unresolved-target diagnostics still apply. This inventory is not proof that every old saved asset resolves.
- **Vehicle/physics/editor:** fuel/electricity prefixes still precede worker snapshots. Native refill now
  visits ISubstanceStore (including habitat supplies; carried EVA cargo is excluded by KSA). New life
  support/radiation rules, conditions/death and suit supplies belong to native saves. The Tick/Kitchen
  Sink remain force protections, not life-support invulnerability. AeroMeshBuilder gathers detached
  source before asynchronous work; no new persistent reader of live Part geometry was found here.
  Editor undo/redo reconstructs parts through the anchor seam; new snap/variant UX needs native use.
- **Remaining suite:** providers/telemetry, dent-wizard, eternal-flame, garrys-torch, godzilla,
  i-feel-seen, kiwis-marbles, glass, camera-controller-override, hot-pursuit, blinky, its-so-shiny,
  kitten-animations, dont-stifle-me, skittles, save-yourself, sphinx and the-tick compile and retain
  their documented entry seams. Kittens' new condition animation can blend with manual expression
  playback; changed atmospheres/terrain can affect surface placement/cameras/plumes without API failure.

## Save compatibility

Existing sidecar IDs and versions stay unchanged. Pebbles gains optional fields with stock inheritance;
paint continues to save floating RGB and re-encodes it at runtime. Iron Man changes its **native XML
carrier**, not its v1 anchor payload. Legacy Id attributes migrate, and both world and blueprint
serializer hooks round-trip connected anchor indices in managed fixtures. Old-world cleanup, exact
vehicle/part resolution and post-native sidecar replay retain their existing order. Native rules,
conditions, doses, supplies and cargo remain native-owned. GPU handles, caches, native modules and
serializer instances are reconstructed/transient and are never written to the sidecar.

## Known-broken baseline reconciliation

The old skill's Controller.___Transform and Zippo Color reflection bugs are already retired; public
Camera access and ColorRgb remain. Vehicle Paint was already revived before this hop and is repaired
for the newly occupied flag bits here. The old nullability compile break and missing supermod
IvaForceRender wiring are already fixed. Prior Blinky/feed, burn-refill, teleport race, repeating
expression, 5707 anchors/AO and cross-mod startup issues are **not** relabelled as 5801 regressions.
Previously pending native acceptance remains pending.

## Validation and native acceptance

Whole-solution build, all 18 managed executables, the 19-case runtime-bootstrap harness and distribution
policy validation are the acceptance gates used here. New regressions cover actual production anchor
patches, legacy/new XML round-trip through both serializer getters, repeated reconstruction,
malformed/duplicate data, paint/native-highlight isolation, real-DLL fields/constructors/layout,
production paint transforms against CURRENT GLSL, resolved material links and optional recipe data.
Managed fixtures do not execute native reconstruction, Vulkan/shaderc or GPU draw correctness.

Final results on 2026-11-08:

| Check | Result |
|---|---|
| Clean `--no-incremental` whole-solution CURRENT build | **PASS — 0 warnings, 0 errors** |
| Fresh Debug managed executables | **18/18 PASS**: byo-music, camera-saves, dent-wizard, doh, garrys-torch, godzilla, iron-man, iron-man-flight, iron-man-mode, kitchen-sink, ksa-upgrade, pebbles, pyro, save-yourself, saves, sphinx, the-tick, world-saves |
| PREVIOUS-DLL negative control | **PASS**: CURRENT metadata suite rejects the prior 10-argument Part constructor |
| Runtime bootstrap / actual built entry module | **19/19 PASS**, initializer present |
| Distribution validator | **PASS**: one Unscience mod, 31 explicit feature assemblies, no KSA.dll |
| Legacy sound XML | **PASS**: array shapes and every local/stock Ref target resolve in CURRENT Content; no native load was run |
| `git diff --check` | **PASS** |
| Native game/editor/Vulkan acceptance | **NOT RUN**, no native install on this host |

Commands (run at the repository root):

```sh
KSAFolder=<CURRENT>/dll/ UNSCIENCE_DIST_DIR=/private/tmp/unscience-5801-dist/ \
DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 dotnet build ksa-mod-experiments.slnx \
  --no-incremental --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
# Execute every *.tests/bin/Debug/net10.0/*.tests.dll with the same KSAFolder.
python3 tools/check_runtime_bootstrap.py --entry-assembly unscience/bin/Debug/net10.0/MeowSci.Unscience.dll
python3 scripts/check-dist.py /private/tmp/unscience-5801-dist
```

Native acceptance still required:

- Load a legacy 5707 world and blueprint with connected custom anchors; save/reload in 5801,
  duplicate/copy, undo/redo with occupied nodes, reset and unload with equipment attached.
- Paint with every habitat highlight (yellow/purple/green), all blend modes, main/secondary/IVA views;
  save/load and disable/re-enable without false paint or native highlight colors.
- DOH tinted single/shared/unique batches, body/head/eyes/fur/attachments, repeated despawn/load
  and material budget recovery.
- Pebbles Earth/Mars curvature and terrain-blend parity, main/secondary depth-normal/AO passes,
  collision/displacement, legacy recipe replay and failed-build/reapply GPU-memory recovery.
- Rocky/Bloomin rings: stock normals, mesh/painted-band overlays, apply/restore, scene replay and renderer rebuild.
- Pyro templates, on/off cycles, sea level/high altitude/vacuum on the new atmospheres; plume pressure
  and density agree with native SI values. Native shader compilation for all custom passes.
- Broad F11 editor/flight smoke, Ctrl+Z/variants/scaling, secondary feeds/HDR/R11G11B10, Graffiti and
  Thug Life, IVA/portrait visibility, Disco symmetry and manual animations under condition poses.
- Native saves around life-support/radiation death, boarding/suit supplies and protected vessels;
  Eternal Flame supply refills; all earlier native and updated-Purr2D2 coexistence checks remain open.
