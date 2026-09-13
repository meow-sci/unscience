# Character / Material / GPU-Customization Mods — Game Integration Scope

Permanent reference for detecting when KSA game updates break the kitten/character and
GPU-material customization mods (`doh`, `humble-arteest`, `kitten-animations`). Every
game-facing member, Harmony target, reflection string, GPU/Vulkan API, per-instance struct
byte-offset, and shader these mods touch is enumerated and verified against the decompiled
sources **and** the Content shader tree, in both game builds.

**Verified game versions**

- NEW decomp `2026.9.7.5402` root: `~/repos/meow-sci/ksa-game-assemblies/current/decomp`
- OLD decomp `2026.8.22.5348` root: `~/repos/meow-sci/ksa-game-assemblies_prev/current/decomp`
- NEW Content root: `~/repos/meow-sci/ksa-game-assemblies/current/Content`
- OLD Content root: `~/repos/meow-sci/ksa-game-assemblies_prev/current/Content`

Paths in the **Decomp/Content path (NEW)** column are relative to the NEW decomp root
(namespace-foldered, e.g. `KSA/PartModel.cs`) or the NEW Content root
(e.g. `Core/Shaders/Mesh/MeshIndirect.vert`). **Mod code** paths are relative to the repo
root `~/repos/meow-sci/unscience`. Every game target was grepped/read in BOTH
decomps and (for shaders) BOTH Content trees; "Δ vs OLD" records the real delta (line moves
are not deltas). Line numbers in the tables were last refreshed against **5402**; earlier
per-pass sections keep the line numbers of the build they were written against.

**How these mods are hosted (all three)**

- Reusable game-facing logic lives in the `*.lib` project (`doh.lib`, `humble-arteest.lib`,
  `kitten-animations.lib`); each exposes one or more `ISubmod`
  (`MeowSci.KsaAbstractions.ISubmod`) consumed two ways:
  1. **Standalone** StarMap mod (`doh/Mod.cs` **F8**, `humble-arteest/Mod.cs` **F11**,
     `kitten-animations/Mod.cs` **F11**) — own ImGui window + own `Harmony` in its `Patcher.cs`.
  2. **Embedded** in the **unscience** supermod as collapsible `ISubmod` sections sharing the
     single `Harmony("MeowSci.Unscience")` instance.
- Every top-level mod also applies shared `HotkeyGuard` (`ksa-abstractions.lib/HotkeyGuard.cs`,
  patches `GameSettings.OnKeyAll(GlfwKeyEvent):bool`) — catalogued elsewhere; one row per mod
  not repeated here.
- Vehicle/character enumeration is funneled through `ksa-abstractions.lib` (`VehicleProvider`
  → `Program.ControlledVehicle` / `Universe.CurrentSystem.All`; `CelestialProvider`), and
  part traversal through `PartHelpers.GetAllParts`.

**Summary of 4680 → 4750 risk**

| Mod | Verdict |
|---|---|
| **doh** | NO breaking deltas. Entire reflection + typed-API surface signature-identical; `MaterialData` byte-identical. Only additive change in scope: rev 4699 `KittenEva.IsControllable=>true`. |
| **humble-arteest** | *Kitten Color* and *Engine Emissive* intact. *Vehicle Paint* was **rebuilt for 5018** (2026-07-25) and works again: the dead shader-swap was replaced by a `ShaderModuleUtils.FromFile` prefix that compiles a patched `MeshIndirect(.Raytraced).frag` in memory, with the color carried in the free `StateBitFlag` bits 11..31. It no longer touches `MeshIndirect.vert` and no longer clobbers `EmissiveColor`/`Temperature`/`TfiThickness`/`Wetness`. Remaining game-coupling to watch: those free state-flag bits, and the `vec3 sampledColor` anchor in the two fragment shaders. |
| **kitten-animations** | NO breaking deltas. `CharacterAvatar.cs` + `CatExpressionAnim.cs` byte-identical; `AnimatedRenderable.cs` differs only by a trivial log line. |

**Key C# layout facts verified (load-bearing for the patches)**

- `KSA/PartModel.cs` @5018 — `PerInstanceData` (80 B) = `float4x4 ModelMatrix`(0) · `int StateBitFlag`(64) · `uint EmissiveColor`(68) · `int packing1`(72, read as `TfiThickness` by the shader) · `float Wetness`(76).
- `KSA/PartModelDynamic.cs` @5018 — `PerInstanceData` (80 B) = `float4x4 ModelMatrix`(0) · `int StateBitFlag`(64) · `float Temperature`(68) · `float TfiThickness`(72) · `float Wetness`(76).
- **`StateBitFlag` bit map** (identical in the static, dynamic and glass structs): game uses bits **0..10** (0 highlighted · 1 grabbed · 2 fake-translucent · 3 selected · 4 edited-vehicle/no-celestial-shadow · 5 IVA/no-planet-shine · 6 no-emissive · 7 add-emissive-color · 8 selected-connected · 9 selected-disconnected · 10 fuel-flow highlight). **Bits 11..31 are free** and are what humble-arteest's Vehicle Paint uses.
- **std430 stride is exactly 80 B in every variant** — the maximal enabled combination (static: `EMISSIVE`+`THIN_FILM`+`WETNESS`; dynamic: `TEMPERATURE`+`THIN_FILM`+`WETNESS`) fills it precisely, so there is **no spare trailing space** to append a field to. Any future per-instance mod data must reuse free bits, not new fields.
- `KSA/MaterialData.cs` — **byte-identical**, `[StructLayout(Sequential, Pack=1)]`: `int AlbedoTexture`(0) `int NormalTexture`(4) `int RoughMetallicAOTexture`(8) `int Sampler`(12) `float4 AlbedoColor`(**16**) `float4 RoughnessMetalScale`(32) `float4 ExtraData`(48) `int EmissiveTexture`(64).
- `KSA/CharacterAvatar.cs`, `KSA/CatExpressionAnim.cs`, `KSA/CatFurRenderable.cs`, `KSA/StaticMeshRenderable.cs`, `KSA/CharacterReference.cs`, `KSA/CharacterTexturesReference.cs`, `KSA/PbrMaterialReference.cs`, `KSA/GpuTextureSystem.cs`, `KSA/PartModelDynamicModule.cs` — all **byte-identical** OLD↔NEW (4680↔4750).
- **@5402 re-check:** `MaterialData` still byte-identical (the 80 B stride is `EmissiveTexture`(64) + `Padding0..2`(68-79)); both `PerInstanceData` structs byte-for-byte identical (`PartModel.cs:332-343`, `PartModelDynamic.cs:342-353`); `StateBitFlag` writers still stop at bit 10. `PartModel.AddInstance`/`PartModelDynamic.AddInstance` and the two `*Module.UpdateRenderData` now take `IViewport` (was `Viewport`) and `AddInstance` is gated on `ViewportOptionFlags.RenderPartModels` — see the 5348 → 5402 area summary.

---

## doh

**Purpose** — "Dynamically Originating Hominids": programmatically spawns `KittenEva` entities
near a vehicle (replicating `EVADoor.CreateKittenEva`), with optional per-kitten GPU material
cloning + `AlbedoColor` tint. Live recolor, batch spawn, "I'm Feeling Lucky" rainbow, despawn.

**Unscience integration** — `DohSubmod : ISubmod` (`doh.lib/DohSubmod.cs`). Spawning engine
`KittenSpawner` (`doh.lib/Spawning/`), material cloning `MaterialFactory` +
`MaterialSystemAccessor` + `KittenMaterialSet` (`doh.lib/Materials/`). Reaches the game via a
deep **reflection** bridge to `Program.Instance` render systems (no typed dependency on the
GPU material internals) plus a **typed** spawn path (`KittenEva`, `Orbit`, `Universe`, `Part`).

**UI / hotkeys** — Standalone **F8** window (`doh/Mod.cs:49`); embedded as `ISubmod` in unscience.
Vehicle/character filterable combos, offset/count, color picker + XKCD combo, per-kitten list.

**Persistence** — None. In-memory `SpawnedKittenRegistry` only; `DespawnAll()` + `MaterialFactory.Cleanup()` + `MaterialSystemAccessor.Cleanup()` on `Dispose`.

### Integration points

| # | Kind | Mod code (file:line) | Game target (Type.Member + sig / struct-offset) | Decomp path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| 1 | Reflection (private) | MaterialSystemAccessor.cs:53,56 | `KSA.Program` type; `Program.Instance` static prop (`public static Program Instance { get; private set; }`) | KSA/Program.cs:453 | ✅ | none (was :434 @5348) | singleton root |
| 2 | Reflection | MaterialSystemAccessor.cs:63 | `Program.MaterialSystem : GpuMaterialSystem` (`public readonly` field) | KSA/Program.cs:118 | ✅ | none (was :99 @5348) | |
| 3 | Reflection (hierarchy) | MaterialSystemAccessor.cs:67 | `AssetManager<T>.AssetMap` (protected `ConcurrentDictionary<AssetName,T>`) | KSA/AssetManager.cs:11 | ✅ | none | walks base types |
| 4 | Reflection | MaterialSystemAccessor.cs:71 | `GpuObjectSystem<T>.BigBuffer : BufferEx` (public get/protected set) | KSA/GpuObjectSystem.cs:18 | ✅ | none | |
| 5 | Reflection (hierarchy) | MaterialSystemAccessor.cs:75 | `GpuObjectSystem<T>.DeviceCtx : IVulkanContext` (protected field) | KSA/GpuObjectSystem.cs:16 | ✅ | none | |
| 6 | Reflection (hierarchy) | MaterialSystemAccessor.cs:78,123 | `GpuObjectSystem<T>.CreateObject(AssetName, T) : bool` | KSA/GpuObjectSystem.cs:45 | ✅ | none | `(AssetName)name, MaterialData` |
| 7 | Reflection (hierarchy) | MaterialSystemAccessor.cs:81,151 | `AssetManager<T>.GetOrLoad(AssetName) : T` | KSA/AssetManager.cs:49 | ✅ | none | returns `GpuObjectAssetRef` |
| 8 | Reflection | MaterialSystemAccessor.cs:154,183,249 | asset-ref `.Handle` (int) on `GpuObjectAssetRef` | KSA/GpuObjectAssetRef.cs | ✅ | none | map name→buffer index |
| 9 | Reflection | MaterialSystemAccessor.cs:84,87,90 | `Program.SuperMeshRenderSystem`; `.TextureSystem : GpuTextureSystem` (`public readonly`); `GpuTextureSystem.GetOrLoad` | KSA/Program.cs:120; SuperMeshRenderSystem.cs:40 | ✅ | none (5402 `SuperMeshRenderSystem.cs` diff is two-sided skinned techniques + `IViewport` only) | texture bindless lookup |
| 10 | GPU write (Vulkan) | MaterialSystemAccessor.cs:282-295; KittenMaterialSet.cs:19,84 | `BufferEx.VkBuffer`; `IVulkanContext.Device.CreateStagingPool`; `VkUtils.StageAndUploadToBuffer`; `ByteSize.Of<MaterialData>()`; `Marshal.OffsetOf<MaterialData>(AlbedoColor)`=16 | KSA/MaterialData.cs:17; Brutal.Vulkan | ✅ | none | writes `float4` at `handle*80+16`; span→bytes via BCL `MemoryMarshal.AsBytes` (no `CommunityToolkit.HighPerformance` reference) |
| 11 | Typed | MaterialFactory.cs:247-257 | `KSA.MaterialData` ctor fields (Albedo/Normal/RoughMetallicAO/Sampler/AlbedoColor/RoughnessMetalScale/Emissive/ExtraData) | KSA/MaterialData.cs:6-23 | ✅ | none | `Pack=1`, identical |
| 12 | Reflection | MaterialFactory.cs:219 | `ModLibrary.Get<PbrMaterialReference>(string)` | KSA/ModLibrary.cs:1042 | ✅ | none (was :1040 @5348) | |
| 13 | Typed | MaterialFactory.cs:382,390 | `ModLibrary.Get<CharacterReference>`; `CharacterReference.CharacterTextures : CharacterTexturesReference` | KSA/CharacterReference.cs:32 | ✅ | none (file identical) | |
| 14 | Reflection | MaterialFactory.cs:406-408 | `CharacterTexturesReference.{CharacterBodyMaterial,CharacterHeadMaterial,CharacterEyeMaterial} : PbrMaterialReference` | KSA/CharacterTexturesReference.cs:9,12,15 | ✅ | none (file identical) | |
| 15 | Reflection | MaterialFactory.cs:413-418,242-245 | `PbrMaterialReference.{DiffuseReference,NormalReference,PBRMap,EmissiveMap,Id}`; non-generic `.Get()` | KSA/PbrMaterialReference.cs:9-18 | ✅ | none (file identical) | `.BindlessHandle` off resolved `TextureReference` |
| 16 | Reflection | MaterialFactory.cs:504-525 | `Program.CharacterRenderSystem`; `CharacterRenderSystem._resources : CharacterRenderResources`; `.FurTexture/.CatFurMaskTexture` (`.BindlessHandle`), `.FurSampler` (`.BindlessIndex`) | KSA/CharacterRenderSystem.cs:7; CharacterRenderResources.cs:24-30 | ✅ | fields none; file diff is internal shader wiring only (see below) | fur `ExtraData` handles |
| 17 | Reflection | MaterialFactory.cs:541-577,592-593 | `GpuTextureSystem.{SamplerRepeatHandle,DefaultWhiteTexture,DefaultBlackTexture}`; `SuperMeshRenderSystem.GltfSystem`; `GltfPbrSystem.BlankMaterialTexture.BindlessHandle` | KSA/GpuTextureSystem.cs:26,32,34; GltfPbrSystem.cs:31 | ✅ | none (GpuTextureSystem.cs identical) | default-texture fallbacks |
| 18 | Reflection (internal field) | KittenSpawner.cs:341,348 | `ModLibrary.AllParts` (internal static `SerializedCollection<PartTemplate>`); `.Find(KeyHash) : PartTemplate` | KSA/ModLibrary.cs:86; SerializedCollection.cs:37 | ✅ | none | `"KittenBackPackPart"` (`:275`) |
| 19 | Reflection (internal field) | KittenSpawner.cs:366,373 | `ModLibrary.AllCharacters` (internal static `SerializedCollection<CharacterReference>`); `.GetList() : List<T>` | KSA/ModLibrary.cs:100; SerializedCollection.cs:42 | ✅ | none (line was already `:100` @5348) | character enumeration |
| 20 | Typed | KittenSpawner.cs:156-164 | `new KittenEva(CelestialSystem system, string characterId, doubleQuat body2Cce, double3 bodyRates, IParentBody parent, string id, Part root, Orbit orbit)` | KSA/KittenEva.cs:78 | ✅ | none (5402 `KittenEva.cs` diff is `UpdateRenderData(IViewport)`, `UpdateHighlight(IGameViewport)` + new `DrawHud` only) | 8-arg ctor identical |
| 21 | Typed (pattern) | KittenSpawner.cs:13-21 | mirrors `EVADoor.CreateKittenEva(Vehicle, IVASeat, KittenRosterEntryData)` (private) | KSA/EVADoor.cs:194 | ✅ | none (file byte-identical 5348↔5402) | call shape mirrored, not invoked |
| 22 | Typed | KittenSpawner.cs:275-289 | `new Part(id, PartTemplate)` (`Part.cs:1386`); `Part.Tree.ReinitializeDerivedValues/RefillConsumables` (`PartTree.cs:302,793`); `Part.SubtreeModules.Get<Tank>()`; `Tank.ConfigureFor(ReactantMix, bool recreateResourceManagers = true)` | KSA/Tank.cs:804 | ✅ | none (`Tank.cs` byte-identical) | backpack/propellant |
| 23 | Typed | KittenSpawner.cs:281,301-306 | `SubstanceLibrary.TryGetReaction(KeyHash)` → `MixtureReaction.AtMixtureRatio(DefaultMixtureRatio).ReactantMix`; `KeyHash.Make` | KSA/SubstanceLibrary.cs:218 | ✅ | none (`SubstanceLibrary.cs` byte-identical; `TryGetCombustionProcess` removed at 5018) | `"MMH_NTO"` |
| 24 | Typed | KittenSpawner.cs:168-169,258 | `Orbit.CreateFromStateCci(IParentBody, UniverseTime, double3, double3, byte4)`; `Orbit.OrbitLineColor : byte4` | KSA/Orbit.cs:1563,1138 | ✅ | none (`Orbit.cs` differs elsewhere only) | |
| 25 | Typed | KittenSpawner.cs:56,121,167,257 | `Universe.CurrentSystem : CelestialSystem?`; `Universe.GetElapsedTime() : UniverseTime` | KSA/Universe.cs:94,2114 | ✅ | none (was `:2060` @5348) | `GetElapsedSimTime` was renamed at 5211 — fixed then |
| 26 | Typed | KittenSpawner.cs:231,239-242,230 | `Vehicle.GetAsmb2Cci()`; `.Body2Cce`; `.BodyRates`; `.Parent`; `.Orbit.StateVectors`(`.PositionCci/.VelocityCci`); `double3.Transform(doubleQuat)` | KSA/Vehicle.cs:3110,475,510,372 | ✅ | none (line moves) | spawn positioning |
| 27 | Typed | KittenSpawner.cs:171,174,175 | `KittenEva.Teleport(Orbit?,doubleQuat?,double3?)`; `IParentBody.Children.Add` (`IParentBody.cs:27`); `Vehicle.UpdatePerFrameData()` (override) | KSA/Vehicle.cs:2209,2613 | ✅ | none (line moves) | |
| 28 | Typed | KittenSpawner.cs:61,67,68 | `CelestialSystem.All.TryGet(string,out Astronomical)` (`All :64`); `CelestialSystem.Deregister(Astronomical)` (`:91`, takes the `Vehicle` by upcast); `Vehicle.Dispose()` | KSA/CelestialSystem.cs:64,91; Vehicle.cs | ✅ | none (5402 `CelestialSystem.cs` diff is the internal `AstronomicalRef` lookup only) | despawn |
| 29 | Reflection | KittenSpawner.cs:525,532 | `KittenEva._renderable : KittenRenderable` (`KittenEva.cs:15`) → `._characterAvatar : CharacterAvatar` (both private) | KSA/KittenEva.cs:15; KSA/KittenRenderable.cs:12 | ✅ | none | avatar root |
| 30 | Reflection (field path) | KittenSpawner.cs:388-430,538-575 | `CharacterAvatar.Core.CharacterModel.MaterialIndices`; `.Fur.CatFurRenderable.MaterialIndices`; `.Attachments.Helmet.HelmetMesh/.VisorMesh.MaterialIndices`; `.Attachments.Mmu.MmuMesh.MaterialIndices` | KSA/CharacterAvatar.cs:211,32,219/61,213/107,109,128; AnimatedRenderable.cs:34; CatFurRenderable.cs:22; StaticMeshRenderable.cs:31 | ✅ | 5402 additive only: `CharacterCore.HeadMeshIndices : List<int>` (`CharacterAvatar.cs:46`, from `CharacterCoreReference.HeadMeshIndices` `:21-22` / `CharacterAssets.xml:244-251`); `AnimatedRenderable.{PrePassIgnoreMeshIndices,MaskedMeshIndices,HideMaskedMeshes}` (`:62-66`) | `MaterialIndices` is `protected readonly int[]` on each renderable; in-place handle swap. ℹ️ `KittenRenderable.HideHead` (`:98`, set by `IVASeat.cs:103` when the camera is in that seat) masks the head meshes and skips the fur draw (`:355-360`) — cosmetic, the handle swap is untouched |
| 31 | Typed (context) | — (not referenced) | `KittenEva.IsControllable => true` / `Vehicle.IsControllable` (virtual) | KSA/KittenEva.cs:63; Vehicle.cs:588 | ✅ | **ADDED (rev 4699)** | informational: spawned kittens now controllable; not a break |
| 32 | Typed | KittenSpawner.cs:69,161,226-229 | `JobSystems.VehicleSolver : JobScheduler` (public static, `Brutal.Concurrency.Jobs`); `JobScheduler.Wait()` (spins until all runners idle) | KSA/JobSystems.cs:16; Brutal.Concurrency.Jobs/JobScheduler.cs:51 | ✅ | **ADDED (5402 fix)** | Guards `new KittenEva` and `Vehicle.Dispose()` against `ConstraintSim.UnlockShapes()` (`ConstraintSim.cs:116`) throwing while `VehicleUpdateTask.Run` (`:176`, `BeginVehicleUpdate`/`EndVehicleUpdate`) is stepping on the solver thread. Depends on frame ordering in `Program.PrepareFrame` (`Program.cs:2102-2145`): `VehicleSolver.Wait()` → `ApplyVehicleSolvers` → `ExecuteNextVehicleSolvers` queues the next step; nothing re-queues mid-frame. `doh.lib.csproj` now references `Brutal.Concurrency.dll`. Game's own equivalent is staging via `InputEvents.EvaSpawnBuffer` (`InputEvents.cs:992`, applied `:1072`) |

### Game assets referenced

- **Characters** by id from `ModLibrary.AllCharacters` (e.g. `"Calico"`, user-selectable / random). No hardcoded character id.
- **Part template** `"KittenBackPackPart"` (`KittenSpawner.cs:275`).
- **Reaction** `"MMH_NTO"` (`KittenSpawner.cs:281` → `TryGetReactantMix`), defined in
  `Core/Reactions.xml` as `<MixtureReaction Id="MMH_NTO">` with `DefaultMixtureRatio` 1.65.
  Was the combustion process `"MMH_NTO_1.6"` before 5018 — the mixture ratio is no longer part of
  the id, so the old id resolves to nothing.
- **Fur texture** `"FurNoise"` reached indirectly via `CharacterRenderResources.FurTexture` (game loads it; mod only reads `.BindlessHandle`).
- No shader files referenced directly. Tint takes effect through `ModelPbr.frag` → `MaterialSet.glsl` (`albedo = mat.albedoColor * texture(...)`, **identical** both builds).

### Update-risk findings (5117 → 5261)

- **CONFIRMED COMPILE BREAK (rev 5211) — doh.** `doh.lib/Spawning/KittenSpawner.cs:167,257` called
  `Universe.GetElapsedSimTime()`, renamed to `Universe.GetElapsedTime()` when `SimTime` became
  `UniverseTime` (`Int128` nanoseconds). Both sites feed the value straight into
  `Orbit.CreateFromStateCci(parent, <time>, pos, vel, colour)`, whose parameter followed the same
  rename, so the fix is the method name only — **no precision or arithmetic handling changed**.
- ✅ **The whole avatar reflection chain still resolves** — `KittenEva` (compared by type-name string),
  `KittenEva._renderable`, `KittenRenderable._characterAvatar`, `CharacterAvatar.Core` (still a
  **value-type field**, which garrys-torch's `SetValue` depends on), `CharacterCore.Scale`, and
  `CatExpressionAnim._expressionPose` (still declared on `CatExpressionAnim` itself, byte-identical
  OLD↔NEW).
- ✅ **`KittenEva` changes are purely additive** — rev 5179 added `KittenControlMode` (View/Direct),
  revs 5203/5233/5249 added ladder grab/board and jump/tumble/landing state. New members only
  (`LadderHost`, `HasGrabCandidate`, `AnimPlaybackRate`, `AnimJumpChainStage`, `SetControlMode`, …);
  **nothing doh, garrys-torch or kitten-animations reads was removed.**
- ✅ **GPU layouts identical** — `MaterialData` is byte-identical, so doh's staged `BigBuffer` writes
  at `handle*80+16` (`AlbedoColor` at offset 16, stride 80) still land correctly; `PerInstanceData`
  is byte-identical, so humble-arteest's padding-byte hijack is safe. The render bridge
  (`Program.MaterialSystem`/`SuperMeshRenderSystem`/`CharacterRenderSystem`,
  `GpuObjectSystem.{BigBuffer,DeviceCtx,CreateObject}`, `AssetManager.AssetMap`) all still resolves.
- ✅ **humble-arteest Engine Emissive unaffected.** `Content/Core/Shaders/Mesh/MeshIndirect.frag`
  changed by **exactly one line** — rev 5196 added
  `lightColor += SamplePortraitLight(inWorldPosition, N, sampledColor, metallic);` for IVA portrait
  lights. The mod's `vec3 sampledColor …;` anchor (line 114) and its `inStateFlags` guard both still
  match, and the `ENABLE_TEMPERATURE` LUT still lives in that file.
  `MeshIndirect.vert` is **byte-identical**.
- ❌ **humble-arteest Vehicle Paint remains dead by design** (rev 4693
  `CompileVariantWithCustomOptions` recompiles from disk and ignores `ShaderReference.Shader`). Both
  self-detect and disable. `ShaderReference.{Shader,DoLoad,ModPath,LocalPath}` and
  `RenderCore.ShaderModuleUtils.FromFile` all still resolve, so the probes still work.
- ⚠️ **Behavioral watch items (need a live pass):** rev 5230 *"Fixed Fur not rendering after a bug was
  introduced when setting up lights for the kitten cam"* — re-check doh's fur/attachment
  `MaterialIndices` path. Revs 5203/5233/5235/5244/5249 add ladder and jump/tumble anim states, the
  prime suspect area for the **"kitten animations always the same expression"** entry in
  [`../ISSUES.md`](../ISSUES.md). Rev 5193 added kitten cameras + portrait UI, and rev 5198 fixed
  drawn kittens leaking into the editor — both touch doh's spawn/render assumptions.

### Update-risk findings (4750 → 5018)

- **BREAKING (fixed):** the combustion model was replaced by a reaction model.
  `SubstanceLibrary.TryGetCombustionProcess` is gone, along with `CombustionObject`,
  `CombustionProcess`, `CombustionProcessTemplate` and `CombustionTable`. The replacements are
  `SubstanceLibrary.TryGetReaction(KeyHash) → Reaction`, with `FixedReaction : Reaction, IReactantMix`
  carrying a `ReactantMix` directly and `MixtureReaction : Reaction` exposing
  `AtMixtureRatio(float) → FixedReaction` plus `DefaultMixtureRatio`. `Tank.ConfigureFor` now takes a
  `ReactantMix` (`ConfigureFor(ReactantMix, bool recreateResourceManagers = true)`).
  `KittenSpawner.CreateBackpackPart` was rewritten accordingly (`TryGetReactantMix` helper).
- **Everything else clean.** `CharacterAvatar.cs`, `GpuMaterialSystem.cs`, `KittenEva.cs`,
  `KittenRenderable.cs` and `CharacterRenderSystem.cs` are **unchanged** 4750→5018.
- `MaterialData` (the GPU write target) is byte-identical (`AlbedoColor` @ offset 16, stride 80) — the
  staged Vulkan write remains correct. `ModelPbr.frag` and `Common/MaterialSet.glsl` are also
  byte-identical, so Kitten Color is unaffected.
- `CharacterAvatar.CharacterCore` is still a **struct** with `public float Scale` — garrys-torch's
  boxed `SetValue` write-back pattern still works.

#### Carried over from the 4680 → 4750 review
- `CharacterRenderResources.cs` **does** differ between builds, but only in **internal shader wiring**: `using Brutal.ShaderCompilerApi` → `Brutal.ShaderCApi`, and the eye/glass technique now compiles `ModelTranslucentFrag` (with `CreateEyeCompileOptions`) instead of `ModelEyeFrag`/`ModelGlassFrag` (rev 4745 ModelGlass+ModelEye merge). The fur/eye **fields** doh reflects (`FurTexture`, `CatFurMaskTexture`, `FurSampler`, `EyeRenderer`) are unchanged → no impact on doh.
- rev 4699 added `KittenEva.IsControllable => true`; doh does not reference it, so additive only (spawned kittens gain controllability).

---

## humble-arteest

**Purpose** — Three independent visual-customization features matched to KSA's three rendering
data paths: **(A) Vehicle Paint** (per-part-instance albedo tint — color packed into the free high
bits of `PerInstanceData.StateBitFlag`, applied by a runtime-patched copy of the part fragment
shaders), **(B) Kitten Color** (`AlbedoColor` writes to the GPU material buffer), **(C)
Engine Emissive** (Harmony override of the per-instance `Temperature`/`TfiThickness` engines glow).

**Unscience integration** — Three `ISubmod`s (`VehiclePaintSubmod`, `KittenColorSubmod`,
`EngineEmissiveSubmod`). Harmony patches applied through the shared instance:
`VehiclePaintPatches.Apply` (five seams — see A1–A6) and `EngineEmissivePatches.Apply`
(on `PartModelDynamic.AddInstance`). `VehiclePaint.Cleanup()` + `EngineEmissive.Cleanup()` on
unload. Kitten Color tinting uses GPU writes; its visor toggle adds `KittenVisorPatches.Apply/Remove`
on `KittenRenderable.UpdateRenderData` in both hosts.

### Kitten visor visibility (@5402)

**Hide/Show visor glass** gates all kittens' visor submissions for the session. It works before
material initialization and restores drawing on Kitten Color deactivation, disposal or patch removal.
Color Reset is independent. Managed draw-gating checks cover isolation and restoration; live-game
visual acceptance (multiple/new kittens, view changes, deactivation and unload) remains pending.

| Integration | Game source (`current/decomp` or `current/Content`) | Dependency / update check |
|---|---|---|
| Harmony transpiler `KittenRenderable.UpdateRenderData` | `KSA/KittenRenderable.cs:310,368` | `KittenVisorPatches.Transpile` requires exactly one adjacent `ldfld CharacterAvatar.Helmet.VisorMesh` + `StaticMeshRenderable.Draw()` call, replaced with a conditional helper. Missing/duplicate match fails installation and disables the UI. |
| Typed `CharacterAvatar.Helmet.VisorMesh` and `StaticMeshRenderable.Draw()` | `KSA/CharacterAvatar.cs:109,467`; `KSA/StaticMeshRenderable.cs:73` | Visor is a distinct non-opaque static mesh, `CastShadows=false`. Skipping Draw prevents both translucency and depth-prepass submission. Helmet/eyes/window draws and attachment state are preserved; no retained game objects or per-frame reflection. |
| Glass pipeline + shader (research dependency, no modification) | `KSA/CharacterRenderResources.cs:49,77,189`; `Core/Shaders/Mesh/ModelTranslucent.frag` | Glass uses transparent pass, alpha blend and depth writes. Non-EYE opacity is hard-coded `0.75`; final alpha mixes toward 1 by Fresnel, ignoring material alpha. Eye variant is also translucent but stays untouched. |

These current sources correct the older blanket claim that all kitten surfaces use `ModelPbr.frag`.
The regular PBR alpha-discard path remains valid for body/helmet; it cannot hide the visor.

**UI / hotkeys** — Standalone **F11** window (`humble-arteest/Mod.cs:66`); embedded in unscience.

**Persistence** — None. In-memory dictionaries keyed by `Part` (per-instance) and part template id;
global toggle + color; all cleared on unload.

**Vehicle Paint mechanism (rewritten for 5018)** — The paint color is quantized to 7:7:7 sRGB and
ORed into `StateBitFlag` **bits 11..31**, which the game leaves unused (it writes only bits 0..10).
That field exists at the same offset in *every* `PerInstanceData` variant and is already forwarded
to every part fragment shader as the `inStateFlags`@loc4 varying, so **no vertex shader, struct
layout, stride, descriptor binding, or game-used field is touched** — in particular `EmissiveColor`,
`Temperature`, `TfiThickness` and `Wetness` are all left alone. Only the *fragment* shader is
modified, and only in memory: a prefix on `ShaderModuleUtils.FromFile` compiles a patched source
string (with the caller's own `CompileOptions`, so every `ENABLE_*` variant still builds) instead of
the file on disk. Installation requests the game's own deferred `Program.RendererRebuildNeeded`
rebuild, which is what recompiles the part pipelines.

### Integration points

| # | Kind | Mod code (file:line) | Game target (Type.Member + sig / struct-offset / shader path) | Decomp/Content path (NEW) | In NEW? | Δ vs OLD | Risk/notes |
|---|---|---|---|---|---|---|---|
| A1 | Harmony PREFIX | VehiclePaintPatches.cs `ResolveFromFile`/`FromFilePrefix` | `RenderCore.ShaderModuleUtils.FromFile(Device, string filePath, out VkShaderStageFlags shaderStage, CompileOptions? options) : VkShaderModule` (**static**) | RenderCore/ShaderModuleUtils.cs:115 | ✅ | n/a (new seam) | The only interception point that works ≥4693: part pipelines recompile per variant straight from disk. Prefix returns `true` (stock behavior) for every non-target path and on any error |
| A2 | Typed call | VehiclePaintPatches.cs `FromFilePrefix` | `ShaderModuleUtils.FromString(Device, ReadOnlySpan<byte>, VkShaderStageFlags, CompileOptions?, ReadOnlySpan<byte> debugName)`; `ShaderStageFromFileExtension(string)` | RenderCore/ShaderModuleUtils.cs:77,198 | ✅ | n/a (new) | `debugName` = the original file path (NUL-terminated) so relative `#include`s resolve exactly as stock; `options` passed through unmodified |
| A3 | Harmony PREFIX | VehiclePaintPatches.cs:49-50,160-164 `PartModelModulePrefix` | `PartModelModule.UpdateRenderData(in double4x4, bool, IViewport viewport, int) : void`; reads `Module<T>.Parent : Part` | KSA/PartModelModule.cs:87; KSA/Module.cs:419 | ✅ | 5402: `Viewport`→`IViewport` (single overload, resolved by name → no impact); light-switch test collapsed to `Parent.FullPart.IsLightSwitchedOff()` (`:106-108`, still bit 6) | Records which `Part` is about to submit. Callers of `PartModel.AddInstance`: this (`:155`) **and** `KSA.Rendering.Thumbnails/ThumbnailPart.cs:226` (thumbnails, `StateBitFlag = 0`, no `UpdateRenderData` → `_pendingPart` is null → unpainted, harmless) |
| A4 | Harmony PREFIX | VehiclePaintPatches.cs:51-52,166-170 `PartModelDynamicModulePrefix` | `PartModelDynamicModule.UpdateRenderData(in double4x4, bool, IViewport viewport, int) : void` | KSA/PartModelDynamicModule.cs:55 | ✅ | 5402: `Viewport`→`IViewport`; same `IsLightSwitchedOff()` collapse (`:97-99`) | Same hand-off for dynamic parts; callers of `PartModelDynamic.AddInstance`: this (`:127`) and `ThumbnailPart.cs:231` (same null-slot guard) |
| A5 | Harmony PREFIX | VehiclePaintPatches.cs:53-54,174-178 `AddInstancePrefix` | `PartModel.AddInstance(PerInstanceData instanceData, IViewport viewport, int frameIndex) : void` — ORs paint into `instanceData.StateBitFlag` | KSA/PartModel.cs:408 (struct :332-343) | ✅ | 5402: `Viewport`→`IViewport`; new early-return `if (!viewport.HasAny(ViewportOptionFlags.RenderPartModels)) return;` (`:410`) runs **after** the prefix — the pending slot is still consumed, nothing leaks | Binds by param name `instanceData` (unchanged); **no** `Unsafe.As` mirror struct any more — writes the public field directly |
| A6 | Harmony PREFIX | VehiclePaintPatches.cs:55-56,180-184 `AddInstanceDynamicPrefix` | `PartModelDynamic.AddInstance(PerInstanceData inInstanceData, IViewport viewport, int inFrameIndex) : void` | KSA/PartModelDynamic.cs:412 (struct :342-353) | ✅ | 5402: `Viewport`→`IViewport`; same `RenderPartModels` gate (`:414`) | Param name `inInstanceData` (unchanged) |
| A7 | Typed | VehiclePaintShaders.cs:108 `RequestRendererRebuild` | `Program.RendererRebuildNeeded : bool` (public static) | KSA/Program.cs:431 (consumed at :2097 `PrepareFrame`) | ✅ | none (line moves) | The game's own deferred-rebuild flag — the same path a Frost/Water graphics-setting change takes, so pipelines are destroyed at a frame boundary, not mid-record |
| A8 | Typed | VehiclePaintShaders.cs:256-257 `TryResolveShaderPath` | `ModLibrary.Get<ShaderReference>("MeshIndirectFrag")` → `FileReference.ModPath : string` | KSA/PartModelRenderer.cs:110,195; KSA/FileReference.cs:23 | ✅ | none | Pre-flight check only, so a shader change fails visibly at "Enable" instead of silently |
| A9 | **Shader text edit** (in memory) | VehiclePaintShaders.cs `Inject`/`BuildSnippet` | `MeshIndirect.frag` **and** `MeshIndirectRaytraced.frag` — anchor = first line starting `vec3 sampledColor` and ending `;`; also requires the `inStateFlags` varying | Content/Core/Shaders/Mesh/MeshIndirect.frag:114; MeshIndirectRaytraced.frag:156 | ✅ | n/a (new anchors) | Anchored on the albedo *declaration*, not an exact line, so incidental upstream edits do not break it. Snippet appends after the sample so paint flows through thin film / frost / PBR. Uses `gammaToLinear` (Common/Shared.glsl:203) |
| A10 | **Per-instance bit budget** | VehiclePaint.cs:41,228-235 `EncodeBits` (`PaintBitShift`=11, 7:7:7) | `PerInstanceData.StateBitFlag` **bits 11..31** — game writes only bits 0..10 | writers: KSA/PartModelModule.cs:90-116, PartModelDynamicModule.cs:81-99, PartModelGlassModule.cs:82-86; readers: MeshIndirect.frag:312-353, MeshIndirectRaytraced.frag:291-333 | ✅ | none @5402 — writers still `<<0..3`, `<<8..10`, `0x10..0x80`; thumbnails write `StateBitFlag = 0` | 🔶 **The one thing to re-check on every game update:** if KSA starts using bit 11 or above, paint and that feature will corrupt each other. `RayTraceInstance.StateFlags` is `int` (RaytracingRenderer.cs:32, copied at :1107), so the bits survive the RT path too |
| A11 | Typed | PaintTargets.cs:63,73-74,132-135,144,155 | `Program.Editor : VehicleEditor?`; `VehicleEditor.EditingSpace : VehicleEditingSpace` → `.Parts : PartTree?`; `.UnattachedPartTrees : List<PartTree>`; `PartTree.Parts : ReadOnlySpan<Part>`; `Part.SubParts/Id/DisplayName/Modules` | KSA/Program.cs:226; VehicleEditor.cs:545,689; VehicleEditingSpace.cs:16; PartTree.cs:95; Part.cs:1079,698,700,680 | ✅ | none (line moves) | Enumerates paint targets in both flight (via `VehicleProvider`) and the editor — mirrors the two sources `Program` itself walks |
| B1 | Reflection | KittenColor.cs:58-73 | `Program.Instance`→`MaterialSystem`→`AssetMap`/`BigBuffer`/`DeviceCtx` (same chain as doh #1-5) | KSA/Program.cs:453,118; GpuObjectSystem.cs:16,18; AssetManager.cs:11 | ✅ | none | |
| B2 | GPU write (Vulkan) | KittenColor.cs:204-214 | `BigBuffer.VkBuffer` + `VkUtils.StageAndUploadToBuffer` at `handle*ByteSize.Of<MaterialData>() + OffsetOf(AlbedoColor=16)` | KSA/MaterialData.cs:17 | ✅ | none (file byte-identical) | tints fur/body/eyes; span→bytes via BCL `MemoryMarshal.AsBytes` (no `CommunityToolkit.HighPerformance` reference) |
| B3 | Shader path (read-only) | (effect) KittenColor.cs concept | `ModelPbr.frag` → `MaterialSet.glsl`: `albedo = mat.albedoColor * texture(...)` (`:31`); alpha `discard` (`ModelPbr.frag:67`) | Content/Core/Shaders/Mesh/ModelPbr.frag:65-75; Common/MaterialSet.glsl:31 | ✅ | MaterialSet.glsl **identical**; ModelPbr.frag @5402 adds only `faceNorm = gl_FrontFacing ? inNormal : -inNormal` (`:70-73`, two-sided parachute canopy) — albedo path untouched | tint path intact |
| C1 | Harmony PREFIX | EngineEmissivePatches.cs:46-47,57,76-78 | `PartModelDynamic.AddInstance(PerInstanceData inInstanceData, IViewport viewport, int inFrameIndex) : void` (prefix `ref … inInstanceData`) | KSA/PartModelDynamic.cs:412 | ✅ | 5402: `Viewport`→`IViewport` + `RenderPartModels` gate (`:414`, after the prefix) — resolved by name, single overload → no impact | param name `inInstanceData` matches |
| C2 | Struct reinterpret (`Unsafe.As`) | EngineEmissivePatches.cs:34-42,83-86 | `PartModelDynamic.PerInstanceData` — writes `Temperature`@**68**, `TfiThickness`@**72** | KSA/PartModelDynamic.cs:342-353 | ✅ | **none** (struct byte-identical; game use of bytes 68–79 unchanged: `MeshIndirect.vert:82 outTemperature = instanceData.Temperature`) | ✅ mirror struct matches **exactly** |
| C3 | Typed | EngineEmissive.cs:123,129,159 | `Part.Modules.Get<PartModelDynamicModule>()`; `PartModelDynamicModule.PartModelDynamic` (`required`) | KSA/PartModelDynamicModule.cs:32 | ✅ | none | engine discovery via `PartHelpers.GetAllParts` |
| C4 | Shader path (read-only) | (effect) — no mod edit | Temperature→emissive LUT logic, formerly `DynamicMeshIndirect.frag`, now `MeshIndirect.frag` under `#ifdef ENABLE_TEMPERATURE` | Content/Core/Shaders/Mesh/MeshIndirect.frag:46-48 (decl: `inTemperature`@loc7, `temperatureLut` binding 9), :297-304 (LUT sample); vert:46-47,81-82 | ✅ | **MOVED** (4693): `DynamicMeshIndirect.frag/.vert` files **removed**; dynamic pipeline now compiles `MeshIndirectVert/Frag` with `ENABLE_TEMPERATURE` (PartModelRenderer.cs:197,209). Both shader files byte-identical 5348↔5402 | ✅ feature still works — game still reads `PerInstanceData.Temperature` |

### Game assets referenced

- **Fragment shaders patched in memory (never on disk):** `Content/Core/Shaders/Mesh/MeshIndirect.frag` and `Content/Core/Shaders/Mesh/MeshIndirectRaytraced.frag`. Matched by **file name** at `ShaderModuleUtils.FromFile` time; `"MeshIndirectFrag"` is also resolved by `ModLibrary` id for the pre-flight check. `MeshIndirect.vert` is **no longer touched at all**.
- **Glass parts are deliberately not painted** — `MeshGlassIndirect.frag` declares `inStateFlags` but ignores it, so windows stay clear.
- **GPU material buffer** (`GpuMaterialSystem.BigBuffer`) for Kitten Color (no asset path).
- **Removed assets the mod's design once assumed:** `DynamicMeshIndirect.vert/.frag` (gone, 4693), `ModelEye.frag`, `ModelGlass.frag` (gone, 4745). `ModelTranslucent.frag` is **new** (4747). None are referenced by the current implementation.

### Update-risk findings (as of 5018)

- ✅ **Vehicle Paint REBUILT for 5018 (2026-07-25).** The 4693-era shader-swap mechanism is gone;
  see "Vehicle Paint mechanism" above and rows A1–A11. What this bought:
  - **No per-instance field is clobbered any more.** The old design wrote floats at offsets 68/72/76,
    which by 5018 were the game-used `EmissiveColor`/`TfiThickness`(`packing1`)/`Wetness` (static) and
    `Temperature`/`TfiThickness`/`Wetness` (dynamic). The new design writes **only** free bits of
    `StateBitFlag`@64, so battery status lights, engine heat glow, thin film and vessel wetness all
    keep working while painted.
  - **No varying-location collisions.** `inStateFlags`@loc4 already exists in every part fragment
    shader, so nothing is added to the vertex shader and locations 5–10
    (`outEmissiveColor`/`outTfiThickness`/`outTemperature`/`outWetness`/frost) are untouched.
  - **Works with the per-variant compile.** Interception moved from `ShaderReference.Shader` down to
    `ShaderModuleUtils.FromFile`, which every variant compile goes through, so
    `ENABLE_EMISSIVE`/`ENABLE_TEMPERATURE`/`ENABLE_THIN_FILM`/`ENABLE_WETNESS`/`ENABLE_FROST` all
    build correctly and the mod never has to reason about which variant it is patching.
  - **Granularity:** per **part instance** (the finest unit the render path exposes), plus per part
    template and a global fallback. A part with several model modules paints as a unit — the modules
    of one `Part` share its paint.
  - **Precision:** 7 bits per channel (128 steps, quantized in sRGB). Not a limitation of the design
    — it is the whole free-bit budget in `StateBitFlag`.
- 🔶 **The one paint invariant to re-check every game update:** `StateBitFlag` bits **11..31** must
  stay unused by KSA. Writers to audit: `PartModelModule.UpdateRenderData` and
  `PartModelDynamicModule.UpdateRenderData`. Readers to audit: the `inStateFlags` bit tests in
  `MeshIndirect.frag`, `MeshIndirectRaytraced.frag`, `MeshGlassIndirect(.Raytraced).frag` and
  `Selected.comp`. At 5018 the game uses bits 0,1,2,3,4,5,6,7,8,9,10 only.
- 🔶 **Secondary paint anchors:** the `vec3 sampledColor = …;` line in `MeshIndirect.frag` (:114) and
  `MeshIndirectRaytraced.frag` (:156). If either moves or is renamed, `Enable` fails loudly with a UI
  message and rendering falls back to stock — it cannot half-apply.
- ✅ **Engine Emissive's LUT survived.** The `#ifdef ENABLE_TEMPERATURE` block (with
  `temperatureLut` sampler and `inTemperature`@loc7) is still present in `MeshIndirect.frag`.
- ✅ `GpuMaterialSystem.cs` and `MaterialData` are **unchanged** → Kitten Color's staged Vulkan write
  is still correct.
- ℹ️ **`PerInstanceData` fields the mod must NOT write (4750→5018 change, still true).**
  `PartModel.PerInstanceData.packing2` → **`public float Wetness`** and
  `PartModelDynamic.PerInstanceData.packing1` → **`public float Wetness`**, feeding the
  `ENABLE_WETNESS` variant (`outWetness`/`inWetness`@loc8) compiled when
  `GameSettings.Current.Graphics.VesselWater` is on; a sibling `ENABLE_FROST` variant arrived with it.
  Engine Emissive writes only `Temperature`/`TfiThickness`, whose offsets did not move.

#### Carried over from the 4680 → 4750 review

- ✅ **Vehicle Paint Harmony plumbing is intact:** `PartModel.cs` is byte-identical OLD↔NEW;
  `AddInstance` signature and `PerInstanceData` layout unchanged.
- ✅ **Engine Emissive intact:** `PartModelDynamic.cs` + `PartModelDynamicModule.cs` byte-identical;
  mirror struct (`Temperature`@68, `TfiThickness`@72) exact; the Temperature→emissive path survived
  the 4693 merge (now inside `MeshIndirect.frag` under `ENABLE_TEMPERATURE`), so no shader edit
  needed and the feature still works.
- ✅ **Kitten Color intact:** `MaterialData` byte-identical (`AlbedoColor`@16); `MaterialSet.glsl`
  identical; `ModelPbr.frag` changed only in SSAO ordering (rev 4671). Note rev 4745 routes
  eye/glass through `ModelTranslucentFrag`; the buffer-write mechanism is unaffected, though
  glass/eye tint appearance may shift since those materials render via a different (merged) shader.

---

## kitten-animations

**Purpose** — Drives a selected live EVA kitten's `CharacterAvatar`: plays every animation the game
loaded for it (the full ground/EVA locomotion set, the MMU set, the live blend samplers, the overlay
poses), triggers the five facial expressions, and exposes the animation-processor blend weights plus
the animation-facing slice of `KittenLocomotionTuning.Current`. A filterable picker follows the
controlled kitten by default or pins the panel to any current-system `KittenEva` by stable vehicle id;
selection never changes game control.

**Unscience integration** — `KittenAnimationsSubmod : ISubmod`
(`kitten-animations.lib/KittenAnimationsSubmod.cs`) resolves and binds the selected `KittenEva` and owns a
`KittenAnimationCatalog`, a `KittenAnimProcessors`, a `KittenExpressionController` and a
`KittenAnimationDriver`. `Update(dt)` (from `[StarMapBeforeGui]`) rebinds on kitten/avatar change and
advances the expression envelope. Explicit targets survive control changes; missing targets unbind
without fallback and rebind if the same id returns. Driver bind/unbind snapshots and restores the
persistent ear, eye-angle and personality values that the mod actually changed.

**Harmony (new this pass)** — one prefix on `AnimatedRenderable.UpdateAnimation(double dt)`
(`KittenAnimationPatches`). Applied from `kitten-animations/Patcher.cs` standalone and from
`unscience/Patcher.cs` (`TryApply("kitten-animations", …)`) when embedded. Required because
`KittenRenderable.UpdateRenderData` calls `SetAnimation` unconditionally for nearly every locomotion
mode, so a clip set from a StarMap callback is discarded before it is sampled. The prefix runs for
every `AnimatedRenderable` in the scene and returns immediately unless the instance is the kitten body
model; it also re-applies processor knobs the game rewrites per frame and can scale `dt`.

**UI / hotkeys** — Standalone **F11** window (`kitten-animations/Mod.cs`); embedded in unscience.
Sections: filterable Target Kitten selector, Playback, Animations, Expressions, Animation Strength,
Locomotion Anim Tuning.

**Persistence** — None.

### Integration points

| # | Kind | Mod code (file) | Game target (Type.Member + sig) | Decomp path (NEW) | In NEW? | Risk/notes |
|---|---|---|---|---|---|---|
| 1 | Typed (via abstraction) | KittenAvatarAccessor.cs, TargetSection.cs | automatic mode: `VehicleProvider.GetControlledVehicle()` → `Program.ControlledVehicle : Vehicle?`; explicit mode/list: `VehicleProvider.{GetAllVehicles(),FindVehicle(string)}` → `Universe.CurrentSystem.All`, with `Vehicle.Id`; pattern-match `is KittenEva` | KSA/Program.cs; KSA/Universe.cs; KSA/CelestialSystem.cs; KSA/Astronomical.cs; KSA/KittenEva.cs:13 | ✅ | target stored by stable id, never combo index/object reference; live list excludes debris through VehicleProvider |
| 2 | Typed | KittenAvatarAccessor.cs | `KittenEva.Renderable : KittenRenderable` (public property) | KSA/KittenEva.cs:59 | ✅ | **replaces the old `_renderable` reflection** — added rev ≤5348 |
| 3 | Reflection (private) | KittenAvatarAccessor.cs | `KittenRenderable._characterAvatar : CharacterAvatar` | KSA/KittenRenderable.cs:12 | ✅ | same field doh/garrys use |
| 4 | Typed | PlaybackSection.cs | `KittenEva.{LocomotionState, ControlMode, AnimPlaybackRate, AnimJumpChainStage, AnimJumpChainCountdown}` | KSA/KittenEva.cs:51,67,53,55,57 | ✅ | read-only status; all public |
| 5 | Typed | PlaybackSection.cs, TuningSection.cs | `LocomotionState.{Mode, GroundSpeed, GravityMagnitude}`; `LocomotionMode`, `JumpChainStage` enums | KSA/LocomotionState.cs:7,13,35; LocomotionMode.cs; JumpChainStage.cs | ✅ | struct copy per frame |
| 6 | Reflection (private, cached `FieldInfo`) | KittenAnimationCatalog.cs | `KittenRenderable.{_groundIdleAnim,_groundWalkAnim,_groundRunAnim,_ladderAnim,_jumpIntroAnim,_flailAnim,_jumpLandAnim,_moonWalkAnim,_moonRunAnim,_swimAnim,_swimIdleAnim,_seatedIdleAnim} : AnimationAssetRef?`, `_seatedIdleActionAnims : List<AnimationAssetRef>?` | KSA/KittenRenderable.cs:42-66 | ✅ | **the only route to the ground set** — it is not on `CharacterAvatar`. Unresolved names are collected and shown in the UI |
| 7 | Reflection (private, cached `FieldInfo`) | KittenAnimationCatalog.cs | `KittenRenderable.{_walkPairSampler,_runPairSampler,_swimPairSampler} : AnimationPairBlendSampler?`, `_blendSampler : AnimationDirectionalBlendSampler` | KSA/KittenRenderable.cs:68-72,38 | ✅ | playable + `.Weight` readout |
| 8 | Typed | KittenAnimationCatalog.cs | `AnimationPairBlendSampler.Weight : float`; `AnimationDirectionalBlendSampler` (type) | KSA.Rendering/AnimationPairBlendSampler.cs:15; AnimationDirectionalBlendSampler.cs | ✅ | read-only display |
| 9 | Reflection (private) | KittenAnimProcessors.cs | `KittenRenderable.{_catPersonalityExpressionAnim,_catExpressionAnim} : CatExpressionAnim`, `_catEyeAnim : CatEyeAnim`, `_catEarAnim : CatEarAnim` | KSA/KittenRenderable.cs:30-36 | ✅ | resolved **by name**, not `OfType<>()` — two of the four are the same type with different roles |
| 10 | Typed | KittenAnimationDriver.cs | `CatEarAnim.ExpressionWeight : float` | KSA/CatEarAnim.cs:13 | ✅ | game sets it once at construction; mod value holds |
| 11 | Typed | KittenAnimationDriver.cs | `CatEyeAnim.{MaxLookAtAngle, LookPitchOffsetDeg} : float` | KSA/CatEyeAnim.cs:22,24 | ✅ | `LookPitchOffsetDeg` is rewritten every frame by `UpdateLocomotionAnimationState`, so it is re-applied from the pose prefix |
| 12 | Typed | KittenAnimationDriver.cs | `CatExpressionAnim.ExpressionWeight : float` on the personality + reactive processors | KSA/CatExpressionAnim.cs:12 | ✅ | reactive weight is damped from acceleration every frame in `UpdateRenderData`, so it can only be **capped**, never held |
| 13 | Typed | KittenExpressionController.cs | `new CatExpressionAnim { CharacterAvatar, ExpressionAnim, ExpressionWeight, Priority }` appended to `AnimatedRenderable.AnimProcessors : List<IAnimProcessor>` | KSA/CatExpressionAnim.cs:8; CatPostAnim.cs:10,12; AnimatedRenderable.cs:52 | ✅ | **mod-owned processor** — required because the game rewrites its own. `CharacterAvatar` is a `required` member on `CatPostAnim` |
| 14 | Reflection (private, cached `FieldInfo`) | KittenExpressionController.cs:27-28 | `CatExpressionAnim._expressionPose : TransformTRS[]?` (set null to bust the sampled-pose cache) | KSA/CatExpressionAnim.cs:16 | ✅ | now busted on the mod's **own** processor; cache logic at `UpdateLocalPose`. File byte-identical 5348↔5402 |
| 15 | Typed | KittenExpressionController.cs | `CharacterAvatar.Expressions.{Angry,Awe,Happy,Sad,Scared} : List<AnimationAssetRef>?` | KSA/CharacterAvatar.cs:194-202 | ✅ | per-variant selection, not just a random pick |
| 16 | Typed | KittenAnimationCatalog.cs | `CharacterAvatar.Animations.MmuAnimations.{MmuIdleDefaultAnim, MmuIdleActionsAnim, MmuMove{Left,Right,Forward,Backward,Up,Down}LoopAnim, MmuArmRetractAnim}` | KSA/CharacterAvatar.cs:162-178 | ✅ | `MmuIdleActionsAnim` list + `MmuArmRetractAnim` are new to the mod this pass |
| 17 | Typed | KittenAnimationCatalog.cs | `CharacterAvatar.Animations.{HelmetMaskAnim, BlinkAnim} : AnimationAssetRef?` | KSA/CharacterAvatar.cs:151,153 | ✅ | overlay poses |
| 18 | Typed | KittenAnimationCatalog.cs | `AnimationAssetRef.{Id, AnimLength, LoopPeriod}`; `IAnimation.{AnimLength, LoopPeriod}` | KSA/AnimationAssetRef.cs:8-16; IAnimation.cs:7 | ✅ | `Id` needs a `Planet.Core` assembly reference (`Core.AssetName`) |
| 19 | Harmony prefix | KittenAnimationPatches.cs:31-39 | `AnimatedRenderable.UpdateAnimation(double dt)` — `(AnimatedRenderable __instance, ref double dt)` | KSA/AnimatedRenderable.cs:134 | ✅ | **the whole override mechanism**; runs for every animated renderable in the scene. 5402: single overload, signature unchanged; new early-out `if (SkinningPoseIsViewportInvariant && _lastSkinningFrameNumber == Program.FrameNumber)` (`:140-144`) — the flag (`:42`) is only ever set by `ChuteRenderable.cs:28` (parachutes), never on the kitten body model, so the prefix's per-frame behaviour is unchanged for the kitten |
| 20 | Typed | KittenAnimationDriver.cs | `AnimatedRenderable.{SetAnimation(IAnimation, float), PlayAnimation(IAnimation, float), FreezeAnimation}` | KSA/AnimatedRenderable.cs:124,129,58 | ✅ | `SetAnimation` is a no-op when the clip is already current (`KSA/BoneAnimRuntime.SetAnimation`), so it is safe per frame |
| 21 | Typed | KittenAnimationsSubmod.cs | `CharacterAvatar.Core.CharacterModel : AnimatedRenderable`; `CharacterAvatar.Personality` | KSA/CharacterAvatar.cs:211,32,221 | ✅ | model identity is what the prefix matches on |
| 22 | Typed (mutating global) | TuningSection.cs | `KittenLocomotionTuning.Current` (public static field) + `Default`; fields `AnimBlendTime`, `IdleSpeedThreshold`, `PlaybackRateMin/Max`, `Walk/Run/Ladder/TumbleClipNominalSpeed`, `Moonwalk*`, `NominalSwimAnimSpeed`, `SwimBlendFullSpeed/HalfLife`, `SwimEyePitchFactor`, `JumpLandDuration`, `JumpLandBounceIgnoreTime`, `LadderEyePitchDeg` | KSA/KittenLocomotionTuning.cs:5-221 | ✅ | **global** — affects every kitten. The game ships the full editor at menu bar → Debug → Kitten Tuning (`Program.cs:3718`) |
| 23 | Typed | TuningSection.cs | `KittenLocomotion.{ComputeMoonwalkWeight(float, in KittenLocomotionTuning), ResolveSwimBlend(float, in KittenLocomotionTuning)}` | KSA/KittenLocomotion.cs:24,476 | ✅ | derived-weight readout only |

### Game assets referenced

- **None by string/path.** Every animation is a typed `AnimationAssetRef` / `IAnimation` already
  resolved by the game on the live `KittenRenderable` / `CharacterAvatar`; the mod selects from them
  and never loads an asset by id. No shader, material, or character-id references.

### Current target-selection findings

- ✅ **Any live EVA kitten can now be targeted without taking control.** The selector uses the same
  filterable-combo UX as other vehicle pickers, defaults to following `Program.ControlledVehicle`,
  and pins explicit choices by `Vehicle.Id`. KSA's render path walks every `KittenEva` in
  `Program.VehiclesInFrame` and calls its `KittenRenderable.UpdateRenderData`, so the existing
  model-identity-filtered `AnimatedRenderable.UpdateAnimation` prefix works unchanged for an
  uncontrolled target. Seated IVA crew are separate renderables and remain out of scope.
- ✅ **Target changes release target-owned state.** A forced clip/expression is cleared, the mod-owned
  expression processor is detached, and persistent ear/eye-angle/personality values are restored on
  the previous target. Reactive face and eye pitch remain game-driven and are rewritten before the
  prefix each render. No new Harmony, reflection, shader or asset surface was added.

### Update-risk findings (5348, rework pass)

- ✅ **Root cause found for the standing `ISSUES.md` entry** *"kitten animations don't properly play
  each one, always the same"*. It was **not** the rev-5278 pose-frame gate. The old
  `KittenAnimationController` located the expression processor with
  `AnimProcessors.OfType<CatExpressionAnim>().LastOrDefault()`, which resolves to
  `KittenRenderable._catExpressionAnim` — the **reactive** face, whose `ExpressionWeight` is
  overwritten every frame by
  `_catExpressionAnim.ExpressionWeight = AnimationUtils.DampingExact(…, accelDerivedTarget, 0.2f, dt)`
  in `UpdateRenderData`, immediately before `UpdateAnimation` samples the pose. The mod's eased weight
  never survived to render, leaving the permanent `_catPersonalityExpressionAnim` mood face on screen
  — i.e. always the same expression. **Fixed** by giving the mod its own `CatExpressionAnim` appended
  to `AnimProcessors`; the reactive processor is now only *capped*, never written.
- ⚠️ **New Harmony surface in this area.** `AnimatedRenderable.UpdateAnimation` is now patched. It is
  a hot path (called for every animated renderable per frame) — the prefix must stay a reference
  compare plus an early return, and must never throw. Any future rename/resignature of
  `UpdateAnimation` breaks the entire override feature (loud: `MissingMethodException` at `Apply`).
- ⚠️ **The ground animation set is reflection-only.** Twelve `AnimationAssetRef` fields plus one list
  and four samplers on `KittenRenderable` are read by name. A rename fails **silently per field** — the
  catalog collects the misses in `UnresolvedFields`, logs them once on bind and shows a red warning in
  the UI, so a game update degrades visibly instead of quietly dropping buttons.
- ✅ **`CharacterAvatar.Animations.WalkingAnimations` is superseded — deliberately dropped.**
  `InitalizeFromCharacterRef` (`CharacterAvatar.cs:406-408` @5402) only ever assigns `WalkingAnim` (a
  duplicate of the ground walk clip) and **never assigns `RunningAnim`**, so the mod's old "Running"
  button was a permanent no-op. Run now comes from `CharacterGroundAnimations.AnimRun` via
  `KittenRenderable._groundRunAnim`.
- ⚠️ **`Planet.Core` is a new assembly reference** on `kitten-animations.lib.csproj`, needed for
  `Core.AssetName` behind `AnimationAssetRef.Id`. Same `$(KSAFolder)` conditional pattern doh uses.
- ✅ Rev-5278 per-frame pose gate (`_lastPoseUpdateFrameNumber`) is benign for the new design: the
  cache bust happens on trigger and the next frame's `UpdateLocalPose` re-samples.

#### Carried over from earlier passes

- `CharacterAvatar.cs` and `CatExpressionAnim.cs` have been byte-identical across 4680 → 5348 for
  every member the mod touches (expression lists, MMU animation fields, `ExpressionAnim` /
  `ExpressionWeight`, the private `_expressionPose` cache).
- rev 4699 (`KittenEva.IsControllable => true`) is additive and *helps* `GetControlledVehicle()`
  return a `KittenEva`; not a break.

---

## Area summary — Update-risk findings (5261 → 5348)

- ✅ **kitten-animations "always the same expression" — root-caused and reworked (superseding the
  rev-5278 pose-guard theory).** The rev-5278 guard (`_lastPoseUpdateFrameNumber` replacing
  `if (!FreezeAnimation)` in `KSA/AnimatedRenderable.cs`) is real but benign: the mod's
  `_expressionPose` cache-bust happens on trigger and the *next* frame re-samples. The actual defect
  was that the mod wrote `ExpressionWeight` to `KittenRenderable._catExpressionAnim` — the reactive
  face, whose weight `UpdateRenderData` damps toward an acceleration-derived target every frame right
  before the pose is sampled — so the permanent `_catPersonalityExpressionAnim` mood face was all that
  ever showed. The mod now appends **its own** `CatExpressionAnim` and only *caps* the reactive one.
  See the kitten-animations section above. **Live pass still wanted to confirm on screen.**
- ⚠️ **kitten-animations now patches `AnimatedRenderable.UpdateAnimation(double)`** (prefix) so a forced
  clip survives `KittenRenderable.UpdateRenderData`'s unconditional per-frame `SetAnimation`. New hot
  path + new breakage surface in this area.
- ✅ **doh's MMU attachment survives a retype.** Rev 5269 (MMU fold-away anim) changed
  `CharacterAvatar.Attachments.Mmu.MmuMesh` from `StaticMeshRenderable` to `AnimatedRenderable`, and
  `CharacterAvatar` now builds it with `MeshRendererSkinnedPbr` + an `AnimationScrubSampler ArmScrub`.
  `doh.lib/Spawning/KittenSpawner.cs:542-556` walks by **field name** and then finds `MaterialIndices`
  anywhere in the runtime type hierarchy — and `AnimatedRenderable` declares
  `protected readonly int[] MaterialIndices` (`:35`) exactly as `StaticMeshRenderable` does (`:31`).
  **No change needed**, but confirm the MMU still recolours in game.
- ✅ **The whole KittenEva reflection chain is intact and still field-shaped.**
  `KittenEva` (type-name compare) → `KittenEva._renderable` (`:15`) → `KittenRenderable._characterAvatar`
  (`:12`) → `CharacterAvatar.Core` (`public CharacterCore Core;`, `:209`) → `CharacterCore.Scale`
  (`public float Scale = 0.01f;`). **`Core` and `Scale` are both still plain fields**, so garrys-torch's
  `FieldInfo.SetValue` path still works. Rev 5329 turned `Module.Parent` into a property — audited, and
  no mod in this area reflects on it.
- ✅ **GPU byte layouts identical.** `MaterialData` and both `PerInstanceData` structs diff clean, so
  doh's `handle*80+16` `BigBuffer` writes and humble-arteest's `StateBitFlag` padding-byte hijack are
  safe. The render bridge (`Program.{Instance,MaterialSystem,SuperMeshRenderSystem,CharacterRenderSystem}`,
  `GpuObjectSystem.{BigBuffer,DeviceCtx,CreateObject}`, `AssetManager.*`, `BindlessHandle`, `Handle`)
  resolves unchanged.
- ✅ **humble-arteest Engine Emissive unaffected.** `MeshIndirect.frag` changed by exactly one line
  (`SamplePortraitLight` → `SampleMeshForwardLights`, rev 5301); the `vec3 sampledColor` anchor is still
  at `:114`, `inStateFlags` is intact, and the `ENABLE_TEMPERATURE` LUT still lives in that file.
  `MeshIndirect.vert` is **byte-identical**. `PartModel/PartModelDynamic.AddInstance` signatures unchanged.
- ❌ **humble-arteest Vehicle Paint** — still dead by design since rev 4693; self-disables. Unchanged.
- ⚠️ **doh — new raytracing paths.** Rev 5312 added receive-only raytracing for IVA kittens;
  `ModelPbr.frag` gained a `RAYTRACED_REFLECTIONS` variant (three new samplers at set 7) and
  `AnimatedRenderable` gained `RaytracedMeshBucketHandles`. The `albedo` path doh's material clones drive
  is unchanged and `Common/MaterialSet.glsl` is untouched — but confirm cloned materials still read
  correctly with raytracing on.
- ℹ️ Additive kitten work this span, no binding impact: seated idle + fidget (5268), MMU fold-away (5269),
  low-gravity walk/run (5284), swimming + `KittenLocomotion` swim state (5314), crew-portrait bone
  tracking and FOV (5270/5273), vehicle destruction now kills crew (5316).

---

## Area summary — Update-risk findings (5348 → 5402)

Revisions 5349–5400 are **unlogged** in any KSA changelog (only rev 5401 "Fixed crash for incorrect
data stride for thumbnail rendering" is logged), so this pass is source-diff-only. Solution builds
clean against 5402 (52 projects, 0 warnings, 0 errors). **No code change was needed in this area.**

- ✅ **The `Viewport` → `IViewport` rework is a no-op for every Harmony seam here.**
  `PartModelModule.UpdateRenderData` (`:87`), `PartModelDynamicModule.UpdateRenderData` (`:55`),
  `PartModel.AddInstance` (`:408`) and `PartModelDynamic.AddInstance` (`:412`) all changed their
  viewport parameter type only; parameter **names** are unchanged, each has a single overload, and
  humble-arteest resolves them with `AccessTools.Method(type, name)` and binds only `__instance` /
  `ref … instanceData` / `ref … inInstanceData`. Both `AddInstance`s gained
  `if (!viewport.HasAny(ViewportOptionFlags.RenderPartModels)) return;` (`PartModel.cs:410`,
  `PartModelDynamic.cs:414`) — it runs **after** the prefixes, so the `_pendingPart` hand-off is still
  consumed 1:1 and nothing leaks across submissions. `MAIN_GAME`, `SECONDARY_GAME`, `PART_THUMBNAIL`
  and `CHARACTER_PORTRAIT` presets all include `RenderPartModels` (`ViewportPresets.cs:5-11`).
- ✅ **GPU byte layouts identical.** `MaterialData.cs` byte-identical (`AlbedoColor`@16, 80 B stride
  incl. `Padding0..2`), so doh's and Kitten Color's `handle*80+16` staged writes still land.
  `PartModel.PerInstanceData` (`:332-343`) and `PartModelDynamic.PerInstanceData` (`:342-353`) are
  byte-for-byte identical to 5348; the game's use of bytes 68–79 (`EmissiveColor`/`packing1`/`Wetness`,
  `Temperature`/`TfiThickness`/`Wetness`) is unchanged, so Engine Emissive's mirror struct is exact.
- ✅ **`StateBitFlag` bits 11..31 invariant holds.** Writers (`PartModelModule.cs:90-116`,
  `PartModelDynamicModule.cs:81-99`, `PartModelGlassModule.cs:82-86`) still stop at bit 10; the only
  body change is the light-switch test collapsing to `Parent.FullPart.IsLightSwitchedOff()` (still bit
  6). Readers `MeshIndirect.frag:312-353` / `MeshIndirectRaytraced.frag:291-333` unchanged;
  `RayTraceInstance.StateFlags` still `int`. Correction to A3/A4: `ThumbnailPart.cs:226,231` is a second
  caller of both `AddInstance`s (with `StateBitFlag = 0`, no `UpdateRenderData`) — the null
  `_pendingPart` guard already covers it, so thumbnails are simply unpainted.
- ✅ **Shaders.** `MeshIndirect.vert`, `MeshIndirect.frag`, `MeshIndirectRaytraced.frag`,
  `MeshGlassIndirect(.Raytraced).frag`, `Common/MaterialSet.glsl`, `Common/Shared.glsl` and
  `Selected.comp` are **byte-identical** 5348↔5402 — the `vec3 sampledColor` anchors (`:114` / `:156`),
  `inStateFlags`, `gammaToLinear` (`:203`) and the `ENABLE_TEMPERATURE` LUT (`:46-48`, `:297-304`) are
  all in place.
  `ModelPbr.frag` gained only a `gl_FrontFacing` normal flip for the new two-sided parachute canopy
  (`:70-73`); the albedo path Kitten Color/doh depend on (`:65-75`, `MaterialSet.glsl:31`) is untouched.
- ✅ **The KittenEva reflection chain is intact and still field-shaped.** `KittenEva` (type-name
  compare) → `_renderable` (`:15`, private field) → `KittenRenderable._characterAvatar` (`:12`, private
  field) → `CharacterAvatar.Core` (`public CharacterCore Core;`, `:211`, `public struct` at `:30`) →
  `CharacterCore.Scale` (`public float Scale = 0.01f;`, `:34`). `CatExpressionAnim.cs`, `CatEyeAnim.cs`,
  `CatEarAnim.cs`, `CatPostAnim.cs`, `CatFurRenderable.cs`, `StaticMeshRenderable.cs` are byte-identical;
  all 17 `KittenRenderable` private animation/sampler fields (`:30-38`, `:42-72`) and
  `KittenEva.{Renderable,LocomotionState,ControlMode,AnimPlaybackRate,AnimJumpChainStage,
  AnimJumpChainCountdown}` (`:51-67`) and the 8-arg ctor (`:78`) are unchanged.
- ℹ️ **Additive character changes, no binding impact.** `CharacterCore.HeadMeshIndices : List<int>`
  (`CharacterAvatar.cs:46`, loaded from the new `<HeadMeshIndices>` in `CharacterAssets.xml:244-251`
  via `CharacterCoreReference.cs:21-22`); `KittenRenderable.HideHead` (`:98`, set by `IVASeat.cs:103`
  when the camera is in that seat) masks the head meshes and skips the fur draw (`:355-360`);
  `AnimatedRenderable.{SkinningPoseIsViewportInvariant, PrePassIgnoreMeshIndices, MaskedMeshIndices,
  HideMaskedMeshes}` (`:42`, `:62-66`) and a two-sided skinned depth technique in the ctor (`:89`).
  doh's `MaterialIndices` handle swap and garrys-torch's boxed `Core` write-back are unaffected (the
  extra reference-typed member in the struct copies through `SetValue`).
- ✅ **kitten-animations' `UpdateAnimation` prefix is unaffected by the new skinning cache.**
  `AnimatedRenderable.UpdateAnimation(double)` (`:134`) keeps its signature and single overload; the
  new `SkinningPoseIsViewportInvariant` early-out (`:140-144`) is only ever armed by
  `ChuteRenderable.cs:28`. The `_lastPoseUpdateFrameNumber` gate (`:147`) is unchanged; the skeleton
  local pose is now re-applied from `_processedPose` on every call (`:170-174`), which is benign for
  the mod's forced clip and mod-owned expression processor.
- 🔍 **Needs a live pass:** (a) doh recolour still lands on body/head/fur/helmet/MMU, including with a
  seated kitten whose head is hidden in its own seat cam; (b) Vehicle Paint / Engine Emissive on a
  vehicle rendered in a secondary viewport (both `AddInstance` gates + per-viewport `UpdateRenderData`
  pairing); (c) kitten-animations forced clips + expressions on screen (still outstanding from the 5348
  pass); (d) cloned materials with raytracing on (carried over from 5261→5348).

### Save/load ownership and material recipes

`humble-arteest.lib/HumbleArteestSubmod.Persistence` captures VehiclePaint global/template/instance
state and exact `PartModelDynamicModule` occurrence within a validated part for Engine Emissive.
Shader replay uses the existing deferred rebuild path. `KittenColor` now tracks successful writes
by native material name and owns original-color restoration; native stock PBR albedo initializes
white, and `ksa-abstractions.lib/Persistence/MaterialColorState` supplies known mod-created colors.
No GPU readback or additional native reflection is introduced. Unknown external-mod GPU writes
remain outside this baseline guarantee. DOH and Free Fallin private names are excluded and delegated
to their owners. Original-color cleanup retains the native `Core.AssetName` key (not its display
string) and validates exact current AssetMap object ownership, name,
and Handle before upload, so disposed canopy materials cannot redirect cleanup into recycled slots.

`doh.lib/DohSubmod.Persistence` restores registry/material ownership onto native-created `KittenEva`
objects by saved vehicle ID, never spawns replacements. It calls existing cloned-material reflection
paths and keeps private slots for reuse across A→B→A when native shared handle identity matches.
Per-material persisted keys are source/name; GPU handles stay runtime-only. Successful native GPU
uploads update shared color tracking so later Humble Arteest edits are included. Registry entries
retain a runtime Vehicle reference for rename-safe snapshot capture.

`kitten-animations.lib/KittenAnimationsSubmod.Persistence` resets processor ownership and rebinds
native avatars after load. It persists exact selected clip identity/phase and reusable settings. Forced looping clips and
frozen poses resume, as do latched expression clips; unlatched one-shot expressions stay stopped. `SavedAnimationTuning` reads/writes the 20 already exposed
`KittenLocomotionTuning.Current` animation fields; physics fields are excluded. No new Harmony target is added. `KittenPlaybackPhase` resolves protected
`AnimatedRenderable.RuntimeAnim`, `BoneAnimRuntime.TimeSinceTransition`, and
`BoneAnimRuntime.CurrentAnimation`; it verifies selected-clip identity when reading the clock and
uses `BoneAnimRuntime.SampleCurrentAnimation()` before the first frozen skinning pass. Missing
fields produce an explicit failure instead of silently capturing another clip. It does not serialize
native bone buffers; in-progress cross-fades restore the selected clip pose. In-game material tint/visor, private-slot reuse, rename and
processor reset/selection acceptance remains pending.
