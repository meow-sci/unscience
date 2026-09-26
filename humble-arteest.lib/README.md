# Humble Arteest — KSA Part Painting & Visual Effects Mod

Humble Arteest provides three visual customization features for KSA:

1. **Vehicle Paint** — Recolors vehicle parts, per part instance, via a runtime-patched part fragment shader
2. **Kitten Color** — Tints kitten character materials and independently hides/shows visor glass
3. **Engine Emissive** — Controls per-engine glow/heat effects via Temperature field overrides

All features are accessible as `ISubmod` implementations for use in the unscience supermod or standalone.

---

## Architecture Overview

### Why Three Separate Approaches?

KSA renders vehicle parts and kitten characters through different pipelines, so each feature uses
the approach matched to its rendering path:

| Object Type | Shader Path | Per-instance data | How we color it |
|---|---|---|---|
| Vehicle parts (static) | `MeshIndirect.{vert,frag}`, `ENABLE_EMISSIVE`+`ENABLE_THIN_FILM` variant | `PartModel.PerInstanceData` | Free bits of `StateBitFlag` + patched fragment shader |
| Vehicle parts (dynamic) | `MeshIndirect.{vert,frag}`, `ENABLE_TEMPERATURE`+`ENABLE_THIN_FILM` variant | `PartModelDynamic.PerInstanceData` | Same, plus the game's own `Temperature` for glow |
| Kitten body/helmet | `ModelPbr.frag` | — (materials live in `GpuMaterialSystem`) | Write `AlbedoColor` into the GPU material buffer |
| Kitten visor glass | `ModelTranslucent.frag` (transparent pass) | `StaticMeshRenderable` submissions | Gate `VisorMesh.Draw()` to hide glass independently of fixed shader opacity |

> **Shader-merge note (KSA rev 4693+).** The separate `DynamicMeshIndirect` shader no longer exists —
> it was merged into a single, feature-gated `MeshIndirect.{vert,frag}` whose behaviour is selected at
> compile time by `ENABLE_EMISSIVE` / `ENABLE_TEMPERATURE` / `ENABLE_THIN_FILM` / `ENABLE_WETNESS` /
> `ENABLE_FROST` defines. "Static" vs "dynamic" parts are now **compile variants of the same shader**,
> not separate files.

---

## Feature 1: Vehicle Paint

Per-part-instance albedo recoloring. Rebuilt from scratch for KSA `2026.7.9.5018`.

### The two problems this design solves

Any per-part color mod has to answer two questions. The pre-5018 implementation answered both in ways
the game has since invalidated:

**1. How does a color reach the GPU per instance?**
The old design wrote floats into what were once padding ints at offsets **68 / 72 / 76** of
`PerInstanceData`. By 5018 all three are game-used:

```
PartModel.PerInstanceData (80 bytes)          PartModelDynamic.PerInstanceData (80 bytes)
  0  float4x4 ModelMatrix   64 bytes            0  float4x4 ModelMatrix   64 bytes
 64  int      StateBitFlag   4 bytes           64  int      StateBitFlag   4 bytes
 68  uint     EmissiveColor  4 bytes  ← used   68  float    Temperature    4 bytes  ← used
 72  int      packing1       4 bytes           72  float    TfiThickness   4 bytes  ← used
 76  float    Wetness        4 bytes  ← used   76  float    Wetness        4 bytes  ← used
```

There is also **no room to append a field**: in std430 the struct strides exactly 80 bytes in every
enabled variant, so a new trailing member would change the stride and desynchronize every draw.

**2. How do you get modified GLSL into the pipeline?**
The old design swapped `ShaderReference.Shader` and called `PartModelRenderer.ColorData.Rebuild()`.
Since rev 4693 the part color pipelines compile through
`ShaderReference.CompileVariantWithCustomOptions()`, which reads the GLSL **fresh from disk per
`ENABLE_*` variant** and destroys the module immediately — it never reads `ShaderReference.Shader`.
The swap was inert.

### How it works now

**Transport: the free bits of `StateBitFlag`.** The game writes only bits **0..10** of that field.
KSA 5482 defines them in `PartTreeRenderData.StateBit`: highlighted, grabbed, fake-translucent,
selected, no-celestial-shadows, no-planet-shine, no-emissive, add-emissive-color, selected-connected,
selected-disconnected and highlight-green. Bits **11..31** are free — 21 bits, which
carries a 7:7:7 sRGB color. `StateBitFlag` lives at offset 64 in *every* `PerInstanceData` variant
(static, dynamic, glass) and is already forwarded to every part fragment shader as the
`inStateFlags`@location 4 varying.

That single choice removes almost all the fragility:

- no game field is clobbered — `EmissiveColor`, `Temperature`, `TfiThickness` and `Wetness` all keep working
- no struct, stride, or descriptor-set change
- **no vertex shader modification at all** — the varying already exists
- no varying-location collisions with `outEmissiveColor`/`outTfiThickness`/`outTemperature`/`outWetness` (locations 5–10)
- the same encoding works for every `ENABLE_*` variant, and survives the raytraced path (`RayTraceInstance.StateFlags` is an `int`)

The cost is color resolution: 7 bits per channel (128 steps, quantized in **sRGB** so the steps are
perceptually even; the shader converts to linear).

**Injection: a prefix on `ShaderModuleUtils.FromFile`.** Every shader compile in the game — including
every per-variant part-pipeline compile — funnels through
`RenderCore.ShaderModuleUtils.FromFile(Device, string filePath, out VkShaderStageFlags, CompileOptions?)`.
The prefix checks the file name; for `MeshIndirect.frag` and `MeshIndirectRaytraced.frag` it compiles a
patched source **string** via `ShaderModuleUtils.FromString`, passing:

- the caller's own `CompileOptions` untouched — so all `ENABLE_*` defines and the include callback still apply
- the original file path as the compiler's input-file name (NUL-terminated) — so relative `#include`s resolve exactly as they do stock

Nothing on disk is read-modify-written, and no temp file is created. Any failure (missing anchor,
compile error, unexpected exception) returns control to the original method, so the worst case is
**stock rendering**, never a broken pipeline.

### Data flow (KSA 5482)

```
PartTreeRenderData.EnsureBuilt(tree, frame)    ← HARMONY PREFIX: InvalidateStates() once after a paint
    │                                             change (RenderStateVersion differs for this tree)
    ▼
PartTreeRenderData.WriteState(batch, slot, part)             ← HARMONY POSTFIXES (private methods):
PartTreeRenderData.WriteDynamicState(batch, slot, module)       batch.StateBitFlags[slot] |= paintBits
    ▼                                             (runs only when the tree's cached state is dirty)
PartTreeRenderData.Compose / ComposeDynamic     (unmodified — per viewport, copies cached flags into
    │                                             instance lists, or RayTraceTransforms in raytraced IVA)
    ▼
PartModelRenderer upload + vkCmdDrawIndexedIndirect   (unmodified)
    ▼
MeshIndirect.vert                               (unmodified — forwards outStateFlags@4)
    ▼
MeshIndirect.frag / MeshIndirectRaytraced.frag  ← PATCHED: unpack bits 11..31, blend into sampledColor
```

KSA 5482 removed `PartModelModule.UpdateRenderData` and `PartModelDynamicModule.UpdateRenderData`.
Part render data is now cached per tree in `PartTreeRenderData` (`PartTree.RenderData`).
`EnsureBuilt` runs once per frame and rewrites a batch's cached `StateBitFlags` only when the tree
is dirty. The private `WriteState` and `WriteDynamicState` methods are the only writers of those
caches for static and dynamic models, and their postfixes OR in the paint bits. The raster `Compose`
path appends cached slots in bulk without calling `PartModel.AddInstance`, which is why the 5438
`AddInstance` prefix and part hand-off could no longer be used.

`VehiclePaint.RenderStateVersion` changes on every paint mutation: global enable/color, per-part
and per-template set/clear, clear-all and pruning. It also changes when the patched shaders are
installed or removed. The `EnsureBuilt` prefix keeps the last version seen for each tree in a weak
table. It calls `InvalidateStates()` once after a change and on a tree's first sighting. Otherwise
each frame costs one version comparison per tree. The cached bits reach raster **and** raytraced IVA submissions. Part thumbnails do not
use these caches and stay unpainted, as before. Engine Emissive still prefixes the dynamic
`AddInstance` submission, which runs every frame.

### The injected GLSL

Anchored on the albedo sample — matched as *the first line beginning `vec3 sampledColor` and ending
`;`*, not as an exact string, so incidental upstream edits do not break it:

```glsl
    vec3 sampledColor = gammaToLinear(texture(sampler2D(globalTextures[drawData.diffuseTextureIndex], textureSampler), inUv).xyz);

    // --- humble-arteest: per-instance paint, packed into state-flag bits 11..31 ---
    {
        uint hbPaintPacked = inStateFlags >> 11u;
        if (hbPaintPacked != 0u)
        {
            vec3 hbPaintColor = gammaToLinear(vec3(
                float((hbPaintPacked >> 14u) & 0x7Fu),
                float((hbPaintPacked >> 7u) & 0x7Fu),
                float( hbPaintPacked        & 0x7Fu)) * (1.0 / 127.0));
            sampledColor *= hbPaintColor;          // ← blend mode goes here
        }
    }
```

Placing it immediately after the sample means the painted color flows through thin film, frost and the
whole PBR evaluation exactly as the texture would.

**Blend modes** (baked into the snippet; switching one triggers a rebuild):

| Mode | Expression | Behavior |
|---|---|---|
| `Multiply` (default) | `sampledColor *= paint` | Keeps every texture detail. Can only darken — ideal for repainting light hulls. |
| `Tint` | `sampledColor = paint * luminance(sampledColor) * 2` | Keeps shading detail and can brighten. A mid-grey surface becomes exactly the picked color. |
| `Replace` | `sampledColor = paint` | Flat color; surface shape still comes from the normal and PBR maps. |

### Applying the change

Installing or removing the patched shaders sets **`Program.RendererRebuildNeeded = true`** rather than
calling `ColorData.Rebuild()` directly. That is the game's own deferred mechanism (`PrepareFrame`
consumes the flag at a frame boundary, after `Device.WaitIdle()`), and it is the same path a
Frost/Water graphics-setting change takes. Calling `Rebuild()` inline would destroy pipelines that the
current frame's command buffer may already reference.

Setting a color, on the other hand, costs nothing — it is just a dictionary write.

### Targeting

Resolution order per part, evaluated when KSA rewrites that part's cached state:

1. **Per part instance** — `Dictionary<Part, …>` (reference identity). The finest unit the render path exposes.
2. **Per part type** — keyed by `Part.Id` (the template id), so "all fuel tanks white" is one click.
3. **Global** — the "paint everything" fallback color.

A `Part` with several model modules paints as a unit; the modules of one part share its color. Parts
are enumerated by `PaintTargets`, which mirrors the two sources the game itself walks: vehicles in the
current system (flight) and `Program.Editor.EditingSpace.Parts` + `Editor.UnattachedPartTrees` (editor).

Glass parts are deliberately not painted — `MeshGlassIndirect.frag` declares `inStateFlags` but ignores
it, so windows stay clear.

### Critical game infrastructure used

- **`RenderCore.ShaderModuleUtils.FromFile(...)`** — the Harmony seam (param names `device` / `filePath` / `shaderStage` / `options` are load-bearing)
- **`ShaderModuleUtils.FromString(...)`** / **`ShaderStageFromFileExtension(...)`** — used to compile the patched source
- **`Brutal.ShaderCApi.CompileOptions`** — only to declare the prefix signature; passed through untouched
- **private `PartTreeRenderData.WriteState(Batch, int, Part)` / `WriteDynamicState(DynamicBatch, int, PartModelDynamicModule)`**
  plus the nested batches' `int[] StateBitFlags` (string lookups) — where the paint bits are ORed in;
  dynamic slots take part identity from `Module<T>.Parent`
- **`PartTreeRenderData.EnsureBuilt(PartTree, ulong)`** / **`InvalidateStates()`** — cache rewrite after a paint change
- **`Program.RendererRebuildNeeded`** — deferred renderer rebuild
- **`ModLibrary.Get<ShaderReference>("MeshIndirectFrag").ModPath`** — pre-flight anchor check only
- **`Program.Editor`**, **`VehicleEditor.EditingSpace.Parts` / `.UnattachedPartTrees`**, **`PartTree.Parts`** — editor paint targets

### What could break and how to fix it

| Breakage | Symptom | Fix |
|---|---|---|
| KSA starts using `StateBitFlag` bit 11 or above | Paint and that feature corrupt each other | Re-audit the bit map; shrink the paint payload (e.g. 6:6:6 or a palette index) or move the transport |
| `vec3 sampledColor = …;` anchor moves or is renamed | "Enable" fails with a UI message; rendering stays stock | Update the anchor predicate in `VehiclePaintShaders.Inject` |
| `inStateFlags` varying renamed or removed | Same — `Inject` refuses and reports why | Follow the new state-flag varying name |
| `ShaderModuleUtils.FromFile` signature or param names change | Log shows fewer than 4/4 hooks attached; UI warns | Update `ResolveFromFile` + the prefix signature |
| `PartTreeRenderData.WriteState`/`WriteDynamicState` or nested `Batch`/`DynamicBatch.StateBitFlags` renamed or re-typed | Fewer than 4/4 hooks; that model kind renders unpainted | Find the new writers of the cached state flags |
| `EnsureBuilt`/`InvalidateStates` change role | Paint changes appear only when something else dirties the tree | Find the new per-tree state invalidation point |
| Fragment shader gains a *new* file that renders parts | New shader renders unpainted | Add its file name to `VehiclePaintShaders.TargetFileNames` |

### Files

- `VehiclePaint.cs` — paint registry (per part / per type / global), bit encoding, blend-mode setting
- `VehiclePaintShaders.cs` — GLSL transform, source cache, install/uninstall + rebuild request
- `VehiclePaintPatches.cs` — the four Harmony seams
- `PaintTargets.cs` — enumerates paintable parts in flight and in the editor
- `VehiclePaintSubmod.cs` / `VehiclePaintSubmodTables.cs` — ISubmod UI panel

---

## Feature 2: Kitten Color (GPU Material Buffer Writes)

### How It Works

Kitten body/helmet models use `ModelPbr.frag`, which reads materials from `GpuMaterialSystem`.
These materials include an `AlbedoColor` field (float4) that is **multiplied** with the albedo texture.
Fur shells use `Fur.frag`; glass and eyes use separate variants of `ModelTranslucent.frag`.
Material tinting and alpha behavior therefore depend on the rendering path.

The default `AlbedoColor` is `(1, 1, 1, 1)` (white = no tint). By writing a different color to the GPU material buffer, we tint all characters using that material.

### Step-by-Step Data Flow

1. **Initialization** (`KittenColor.Initialize()`)
   - Uses reflection to access `Program.Instance` → `MaterialSystem` (type `GpuMaterialSystem`)
   - Reads the `AssetMap` (ConcurrentDictionary) which maps material names to handles
   - Caches `BigBuffer` (the GPU buffer containing all MaterialData) and `DeviceCtx` (Vulkan context)

2. **Color Application** (`KittenColor.WriteAlbedoColor()`)
   - Calculates the byte offset: `handle * sizeof(MaterialData) + offsetof(AlbedoColor)`
   - Creates a Vulkan staging pool and command buffer
   - Uses `VkUtils.StageAndUploadToBuffer()` to write the new float4 color at the correct offset
   - This is a direct GPU buffer write — the change takes effect immediately

3. **Reset** — writes `(1, 1, 1, 1)` to all materials, restoring the original white multiplier

### Why This Doesn't Work for Vehicle Parts

Vehicle parts use `MeshIndirect.frag` which reads texture indices from `PerDrawData` and samples textures directly — it **never reads** from `GpuMaterialSystem`. The `AlbedoColor` field only exists in the `MaterialSet.glsl` include used by `ModelPbr.frag`.

The material list from `GpuMaterialSystem.AssetMap` is populated by:
- `CharacterRenderResources` — kitten fur, glass, eye materials
- `GltfPbrSystem` — GLTF model materials

Vehicle parts never register here — which is exactly why Vehicle Paint needs its own transport.

### Alpha Channel Behavior

The `ModelPbr.frag` shader discards fragments when `albedo.a < 0.1`. This does not apply to visor glass.

### Visor glass visibility

Enable **Active**, then click **Hide visor glass** (the button changes to **Show visor glass**).
It affects all kittens, including new spawns, without initializing the material editor. Turning
Active off or disposing the submod restores drawing. Color Reset only resets colors; the visor
toggle is independent and session-only.

KSA build `2026.9.7.5402` constructs `CharacterAvatar.Helmet.VisorMesh` as a separate
`StaticMeshRenderable` with `isOpaque: false` and `CastShadows = false`
(`CharacterAvatar.SetupAttachmentsFromRenderRef`). `CharacterRenderResources.GlassRenderer`
uses `ModelVert` + `ModelTranslucentFrag` in the transparent pass, with alpha blending and depth
writes enabled. The shader does **not** discard on alpha: its non-`EYE` branch hard-codes
`opacity = 0.75`, then computes `finalAlpha = mix(opacity, 1.0, fresnel * 0.5)`. Changing
`AlbedoColor.W` cannot make that glass invisible. The `EYE` variant hard-codes opacity to 0.3.

`KittenVisorPatches` transpiles `KittenRenderable.UpdateRenderData`, replacing exactly one visor
draw call with a conditional draw helper. Since KSA 5482 meshes are bucketed per render view, and
the call is `VisorMesh.Draw(view)`. The transpiler matches `ldfld CharacterAvatar.Helmet.VisorMesh`,
a local load of the view and `StaticMeshRenderable.Draw(ViewHandle)`, then substitutes
`DrawVisor(StaticMeshRenderable, ViewHandle)`. Skipping `Draw(view)` omits both transparent color
and depth-prepass submissions for that view.
The helmet shell, eyes, other glass, animation, attachment state and mesh visibility flags are
untouched. No shader recompilation or renderer rebuild is needed. Both hosts apply/remove the
patch. If the expected single IL match changes, patch installation fails and the UI disables the
button with an error instead of guessing which mesh to hide.

Validation: solution compilation and managed draw-gating checks; live KSA visual acceptance remains
required (hide/show multiple kittens, spawn while hidden, switch views, deactivate and unload).

### Critical Game Infrastructure Used

- **`KSA.Program.Instance`** — game singleton (accessed via reflection: `typeof(Part).Assembly.GetType("KSA.Program")`)
- **`GpuMaterialSystem`** (field on Program) — manages the GPU material buffer
- **`GpuMaterialSystem.AssetMap`** — `ConcurrentDictionary<string, GpuObjectHandle>` mapping names to handles
- **`GpuMaterialSystem.BigBuffer`** — `BufferEx` containing all `MaterialData` structs packed sequentially
- **`MaterialData.AlbedoColor`** — `float4` field at a specific offset within the struct
- **`VkUtils.StageAndUploadToBuffer()`** — uploads data to a Vulkan buffer via staging

### MaterialData Struct Layout

The `AlbedoColor` offset is determined via `Marshal.OffsetOf<MaterialData>("AlbedoColor")`. If KSA reorganizes `MaterialData`, this offset may change. The struct is defined in `KSA.MaterialData`.

### What Could Break and How to Fix It

| Breakage | Symptom | Fix |
|----------|---------|-----|
| `MaterialData.AlbedoColor` field renamed/removed | Offset calculation fails | Find the new field name or offset in the decompiled struct |
| `GpuMaterialSystem` internals restructured | Initialization fails | Update reflection paths — walk the hierarchy to find AssetMap/BigBuffer |
| `ModelPbr.frag` stops reading AlbedoColor | Colors have no effect | Check if a new uniform or buffer binding replaced it |
| `Program.Instance` accessor changed | Can't reach MaterialSystem | Update the reflection path to the game singleton |
| New material types registered | Unexpected tinting | Filter the AssetMap by material name patterns |

### Files

- `KittenColor.cs` — reflection-based GPU buffer access + color writes
- `KittenColorSubmod.cs` — ISubmod UI panel
- `KittenVisorPatches.cs` — targeted visor draw gate, with Apply/Remove lifecycle

---

## Feature 3: Engine Emissive (Temperature Field Override)

### How It Works

Dynamic vehicle parts (engines, heat shields) use `PartModelDynamic.PerInstanceData` which includes a `Temperature` float field. The game's merged `MeshIndirect` shader (its `ENABLE_TEMPERATURE` variant — formerly `DynamicMeshIndirect.frag` before rev 4693) already reads this field and uses it as an index into an emissive color lookup table — this is how running engines glow.

We simply override the Temperature value before it reaches the GPU, giving explicit control over engine glow intensity.

### Step-by-Step Data Flow

1. **Harmony Prefix on the shared `PartModelDynamic.AddInstance()` submission** (`EngineEmissivePatches.cs`)
   - Checks `EngineEmissive.TryGetEffective()` for per-engine or global overrides
   - Uses `Unsafe.As<>` to reinterpret the struct and write `Temperature` and `TfiThickness`
   - No shader modifications needed — Temperature is already wired end-to-end

2. **Per-Engine Targeting**
   - `EngineEmissive.ScanDynamicParts(vehicle)` traverses the part tree
   - Finds parts with `PartModelDynamicModule` and extracts their `PartModelDynamic` reference
   - Each engine gets its own slider in the UI

### PerInstanceData Layout (Dynamic — 80 bytes)

```
float4x4 ModelMatrix      64 bytes
int      StateBitFlag      4 bytes  ← Vehicle Paint uses bits 11..31 here; do not write the low bits
float    Temperature       4 bytes  ← we override this
float    TfiThickness      4 bytes  ← we override this
float    Wetness           4 bytes  ← game-used (ENABLE_WETNESS); do NOT write
```

### What Temperature Does in the Shader

The merged `MeshIndirect` shader (`ENABLE_TEMPERATURE` variant) uses Temperature to:
1. Sample a color from a temperature lookup table (LUT) texture
2. Add the LUT color as emissive lighting
3. Higher Temperature values → more intense glow (cool blue → orange → bright white)

Negative Temperature drives the `ENABLE_FROST` path instead. `TfiThickness` controls thin-film
interference effects (rainbow sheen).

### What Could Break and How to Fix It

| Breakage | Symptom | Fix |
|----------|---------|-----|
| `PartModelDynamic.PerInstanceData` layout changes | Wrong field overridden | Update the mirror struct `WritablePerInstanceData` to match |
| `PartModelDynamic.AddInstance` overloads or signature changes | Harmony patch fails or applies twice | Resolve the shared private `(PerInstanceData, PerInstanceDent, IViewport, int)` submission and update its parameter names/types |
| Temperature field renamed | Compiler error in mirror struct | Rename in `WritablePerInstanceData` |
| `PartModelDynamicModule` removed/renamed | Part scanning fails | Find the new module type for dynamic parts |

### Files

- `EngineEmissive.cs` — per-engine state management + part scanning
- `EngineEmissivePatches.cs` — Harmony prefix on `PartModelDynamic.AddInstance`
- `EngineEmissiveSubmod.cs` — ISubmod UI panel

---

## Project Structure

```
humble-arteest/                    — Standalone mod (F11 toggle)
├── Mod.cs                         — StarMapMod lifecycle, creates submods
├── Patcher.cs                     — Harmony setup: VehiclePaint + EngineEmissive patches
├── humble-arteest.csproj
└── mod.toml

humble-arteest.lib/                — Core library (referenced by unscience supermod)
├── VehiclePaint.cs                — Paint registry + StateBitFlag bit encoding
├── VehiclePaintShaders.cs         — GLSL injection, source cache, install/rebuild
├── VehiclePaintPatches.cs         — The four Harmony seams
├── PaintTargets.cs                — Paintable part enumeration (flight + editor)
├── VehiclePaintSubmod.cs          — ISubmod UI: shader state, brush, blend mode
├── VehiclePaintSubmodTables.cs    — ISubmod UI: per-part and per-part-type tables
├── KittenColor.cs                 — GPU material buffer AlbedoColor writes
├── KittenColorSubmod.cs           — ISubmod UI for kitten coloring
├── EngineEmissive.cs              — Per-engine temperature state management
├── EngineEmissivePatches.cs       — Harmony prefix on PartModelDynamic.AddInstance
├── EngineEmissiveSubmod.cs        — ISubmod UI for engine glow control
├── HumbleArteestSubmod.cs         — Composite ISubmod grouping all three
├── Experiments/                   — Retained validation experiments
│   ├── MaterialColorTest.cs       — AlbedoColor material test (Kitten Color)
│   └── TemperatureTest.cs         — Temperature field override test (Engine Emissive)
└── humble-arteest.lib.csproj
```

---

## Unscience Supermod Integration

All three submods (`VehiclePaintSubmod`, `KittenColorSubmod`, `EngineEmissiveSubmod`) implement `ISubmod` from `ksa-abstractions.lib`, grouped by `HumbleArteestSubmod`. Unscience registers them alongside other submods and provides Harmony patches via its consolidated patcher.

Unscience's `Patcher.cs` calls:
- `VehiclePaintPatches.Apply(harmony)` / `.Remove(harmony)`
- `EngineEmissivePatches.Apply(harmony)` / `.Remove(harmony)`

Unscience's `Patcher.Unload()` also calls:
- `VehiclePaint.Cleanup()` — removes the patched shaders (requesting a rebuild) and clears paint state
- `EngineEmissive.Cleanup()` — clears all engine overrides

Kitten Color's tinting uses GPU buffer writes; its independent visor toggle uses
`KittenVisorPatches.Apply(harmony)` / `.Remove(harmony)` in both hosts. `KittenColorSubmod.Dispose`
also clears the hide flag.

---

## Key Decompiled Source References

For future maintenance when KSA updates break this mod. The authoritative current decomp/assets live
under `ksa-game-assemblies/current/decomp` and `.../current/Content`. Line numbers are for KSA 5482.

| File | What to Check |
|------|---------------|
| `KSA/PartModel.cs` | `PerInstanceData` layout (:383-394), public wrappers and private submission (:459-490) |
| `KSA/PartModelDynamic.cs` | Dynamic `PerInstanceData` (:393-404), public wrappers and private submission (:463-481; Engine Emissive) |
| `KSA/PartTreeRenderData.cs` | **`StateBit` bit map** (:196-219), `InvalidateStates` (:323), `EnsureBuilt` (:444), `WriteDynamicState` (:1088), `WriteState` (:1210), `Compose` (:1258) |
| `KSA/PartModelDynamicModule.cs` | Dynamic module with Temperature/TFI |
| `KSA/KittenRenderable.cs` | `VisorMesh.Draw(view)` (:370) — the visor transpiler match |
| `KSA/PartModelRenderer.cs` | `ColorData.BuildPipelineModel/Dynamic` — which `ENABLE_*` defines each pipeline uses |
| `KSA/ShaderReference.cs` | `CompileVariantWithCustomOptions()` — why `.Shader` swapping is inert |
| `RenderCore/ShaderModuleUtils.cs` | `FromFile` (:115) / `FromString` (:77) — the interception seam |
| `KSA/Program.cs` | `RendererRebuildNeeded` (:430), consumed in `PrepareFrame` (:2141) |
| `Content/Core/Shaders/Mesh/MeshIndirect.frag` | Paint anchor (:114) and the `inStateFlags` bit tests (:308-353) |
| `Content/Core/Shaders/Mesh/MeshIndirectRaytraced.frag` | Same anchor (:156) for the IVA raytraced path |
| `Content/Core/Shaders/Common/Shared.glsl` | `gammaToLinear` (:203), `unpackRGB` (:50) |
| `KSA/MaterialData.cs` | `MaterialData` struct with `AlbedoColor` (Kitten Color) |
| `KSA/GpuMaterialSystem.cs` | Material GPU buffer, `AssetMap`, `BigBuffer` (Kitten Color) |
| `Content/Core/Shaders/Mesh/ModelPbr.frag` + `Common/MaterialSet.glsl` | AlbedoColor multiplication (Kitten Color) |

---

## Experiment History

The `Experiments/` directory retains the validation tests still relevant to the live features:

| Experiment | Result | What It Proved |
|-----------|--------|---------------|
| Material AlbedoColor | ✅ MIXED | Works for kittens (ModelPbr), not vehicle parts (MeshIndirect) |
| Temperature Override | ✅ PASSED | Per-instance data flows correctly, no shader changes needed |

The shader-swap-era experiments (`ShaderLoadTest`, `PaddingTest`, `ShaderHotReloadTest`, `GamePaths`)
were removed with the mechanism they validated: the padding bytes they probed are now game-used, and
`ShaderReference.Shader` swapping no longer affects part rendering.

## Scene saves

The `humble-arteest` participant persists shader activation/blend mode, global/template/instance
paint, global and exact module engine-emissive settings, named material tints and visor visibility.
Part addresses use vehicle IDs and validated full-part/subpart paths. Restore rebuilds shader state
through the existing deferred renderer flag and synchronizes panel activation/entries, so opening
the panel cannot immediately erase the loaded result.

Successful Kitten Color writes now retain original colors and detached applied colors. Save reset
restores only owned writes before native vehicle destruction, then clears paint/emissive registries.
Shared `MaterialColorState` records known Unscience-created colors so DOH material originals and
later tint edits survive without GPU readback. Native stock PBR materials begin white; colors written
by unrelated mods without this tracking are outside the original-value guarantee. Private `doh_*`
GPU names are excluded from this participant: DOH captures their effective colors under stable
kitten/material identity. Missing material names, parts or module slots appear in Saves status.
Material cleanup verifies the original asset still owns its GPU slot before restoring its color.
DOH and Free Fallin private materials persist through their owning features, including Humble
Arteest edits, so generated asset names and recycled handles cannot leak between scenes.
