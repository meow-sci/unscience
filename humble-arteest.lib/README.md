# Humble Arteest — KSA Part Painting & Visual Effects Mod

## KSA 5801 compatibility

Vehicle Paint now encodes 6:6:6 sRGB in StateBitFlag bits 13–30, preserving native habitat highlight bits 11/12. VehiclePaintShaderSource is the pure production transform used by both runtime and managed CURRENT-GLSL checks. Saved floating RGB values and record versions stay unchanged; native appearance remains pending.

See [upgrade evidence and native acceptance](../plans/KSA_5801_UPGRADE.md).

The reported white-vessel glow on Enable remains under native investigation. Activation diagnostics
and a process-local **stock shader A/B** are available; follow the exact environment-variable and
restart steps in [the 5801 paint investigation](../plans/HUMBLE_ARTEEST_5801_PAINT_INVESTIGATION.md).
The comparison forwards unmodified stock GLSL and intentionally applies no vehicle colors.

Humble Arteest provides vehicle part paint and engine glow:

1. **Vehicle Paint** — Recolors vehicle parts, per part instance, via a runtime-patched part fragment shader
2. **Engine Emissive** — Controls per-engine glow/heat effects via Temperature field overrides

All features are accessible as `ISubmod` implementations for use in the unscience supermod or standalone.

---

## Architecture Overview

### Rendering approaches

KSA renders vehicle parts and kitten characters through different pipelines, so each feature uses
the approach matched to its rendering path:

| Object Type | Shader Path | Per-instance data | How we color it |
|---|---|---|---|
| Vehicle parts (static) | `MeshIndirect.{vert,frag}`, `ENABLE_EMISSIVE`+`ENABLE_THIN_FILM` variant | `PartModel.PerInstanceData` | Free bits of `StateBitFlag` + patched fragment shader |
| Vehicle parts (dynamic) | `MeshIndirect.{vert,frag}`, `ENABLE_TEMPERATURE`+`ENABLE_THIN_FILM` variant | `PartModelDynamic.PerInstanceData` | Same, plus the game's own `Temperature` for glow |

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

**Transport: the free bits of `StateBitFlag`.** The game writes only bits **0..12** of that field.
KSA 5482 defines them in `PartTreeRenderData.StateBit`: highlighted, grabbed, fake-translucent,
selected, no-celestial-shadows, no-planet-shine, no-emissive, add-emissive-color, selected-connected,
selected-disconnected and highlight-green. KSA 5801 also uses bits 11/12 for yellow/purple habitat highlights. Bits **13..30** carry 18 bits, which
carries a 6:6:6 sRGB color. `StateBitFlag` lives at offset 64 in *every* `PerInstanceData` variant
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
MeshIndirect.frag / MeshIndirectRaytraced.frag  ← PATCHED: unpack bits 13..30, blend into sampledColor
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

    // --- humble-arteest: per-instance paint, packed into state-flag bits 13..30 ---
    {
        uint hbPaintPacked = inStateFlags >> 13u;
        if (hbPaintPacked != 0u)
        {
            vec3 hbPaintColor = gammaToLinear(vec3(
                float((hbPaintPacked >> 12u) & 0x3Fu),
                float((hbPaintPacked >> 6u) & 0x3Fu),
                float( hbPaintPacked        & 0x3Fu)) * (1.0 / 63.0));
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

## Former kitten visor control

The control and `KittenVisorPatches` draw gate now live in [War Paint](../war-paint.lib/README.md).
Humble Arteest no longer renders visor controls, status or cross-mod hint text, installs the visor
patch, or resets it during panel disposal. To retain scene compatibility, this library's unchanged
`humble-arteest` v1 `HideVisor` field still captures/resets/restores the War Paint flag. Existing hidden
scenes stay hidden; fresh/vanilla scenes show glass. This compatibility adapter is the sole durable
save owner; the draw patch and checkbox belong to War Paint.

---

## Feature 2: Engine Emissive (Temperature Field Override)

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
int      StateBitFlag      4 bytes  ← Vehicle Paint uses bits 13..30 here; do not write the low bits
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
├── EngineEmissive.cs              — Per-engine temperature state management
├── EngineEmissivePatches.cs       — Harmony prefix on PartModelDynamic.AddInstance
├── EngineEmissiveSubmod.cs        — ISubmod UI for engine glow control
├── HumbleArteestSubmod.cs         — Vehicle paint and engine glow panel
├── Experiments/                   — Retained validation experiments
│   ├── MaterialColorTest.cs       — Historical AlbedoColor material experiment
│   └── TemperatureTest.cs         — Temperature field override test (Engine Emissive)
└── humble-arteest.lib.csproj
```

---

## Unscience Supermod Integration

`HumbleArteestSubmod` implements `ISubmod` and provides the vehicle paint and engine glow panel
in Unscience and its development host. The patcher applies/removes `VehiclePaintPatches` and
`EngineEmissivePatches`. Dispose cleans vehicle/engine ownership. War Paint owns visor controls
and draw patching; the legacy scene adapter here retains the existing visor field.

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

The unchanged version-1 `humble-arteest` participant persists shader activation/blend mode,
global/template/instance vehicle paint, global and exact-module engine glow, and the compatibility
`HideVisor` flag for War Paint's global visor setting.
Part addresses use shared stable vehicle IDs and validated full-part/subpart paths. Reset clears
paint/emissive registries before native vehicle destruction; replay synchronizes the panels.

The legacy `MaterialColors` dictionary remains readable in this payload. Restore forwards those
named recipes to War Paint, which finds matching source materials on reconstructed live EVA
kittens and applies independent owned material sets. It reports unmatched material names and failed
targets so the save coordinator retains incomplete legacy data. New captures leave the dictionary
empty; active kitten paint is captured by War Paint's scene participant. Its later replay takes
precedence over migrated legacy paint. Global reusable presets live in War Paint's separate disk
library. Humble Arteest no longer writes global kitten-material colors.
