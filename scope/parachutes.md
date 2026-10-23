# Parachutes (free-fallin) — Game Integration Scope

Permanent reference for detecting when KSA game updates break **free-fallin**, the global parachute
texture/tint/PBR customizer. Cataloged against KSA build **2026.9.22.5482** at
`../ksa-game-assemblies/current/decomp` and `../ksa-game-assemblies/current/Content` (diffed from
2026.9.10.5438; see the 5482 verification section) and re-verified against **2026.10.19.5638**
(see the 5638 section; the touchpoint table carries 5638 line numbers where they moved).

All logic is in `free-fallin.lib` (`FreeFallinSubmod : ISubmod`,
`FreeFallinPatches.Apply/Remove`), consumed by both the standalone `free-fallin` host and
`unscience`. The standalone Patcher also applies the required `HotkeyGuard`. Shader text
transforms live in the game-independent `CanopyShaderSource.cs`; `CanopyProjectionShaders.cs`
owns the game seams.

## Integration model

KSA creates one `ChuteRenderable` per live canopy and selects material slot zero from
`Parachute.CanopyMaterial?.Id`. Its `Draw` method updates the cloth pose and calls a private
`AnimatedRenderable`. Free-fallin prefixes that draw, reflects `_renderable`, then swaps element
zero of its protected `MaterialIndices` array to a mod-created material handle. The array is read
by `AnimatedRenderable.Draw(ViewHandle)` for the main, pre-pass, and shadow submissions of every
view (per-view buckets since 5482), so one substitution keeps every pass consistent and follows the
skinned cloth automatically.

Full Canopy additionally prefixes `KSA.Rendering.Utils.SetShaderFromMod` and
`ShaderModuleUtils.FromFile`, then patches `Model.vert`, `Model_Skinned.vert`, and `ModelPbr.frag` in
memory during the game's normal renderer rebuild. Before either prefix is installed, the
texture/material descriptor-set indices are parsed from the live `ModelPbr.frag` and cross-checked
against `ModelNormal.frag` and `ModelTranslucent.frag`; any parse, anchor or varying-slot failure
leaves the feature unavailable and every stock shader untouched. `SetShaderFromMod` ordinarily reuses the cached
`ShaderReference` module for these pipelines, bypassing `FromFile`; the first prefix forces only the
three targets through `CompileVariantWithCustomOptions`, allowing the second prefix to transform
their source. A marker plus projection scale/rotation in `MaterialData.ExtraData` gates the path to
Free Fallin's full-canopy materials. The skinned vertex shader derives a second UV from bind-pose
X/Z, normalized by `ChuteCanopyBones.MeasureBindHemRadius`; the fragment shader uses it only for
albedo. Authored UVs continue to sample the normal and PBR maps. Static `Model.vert` supplies a
pass-through varying so the shared fragment shader remains link-compatible with non-skinned
pipelines. No game shader file is changed on disk.

The material is built through KSA's public GPU systems:

- Stock mode reuses the stock albedo bindless handle and multiplies it by `MaterialData.AlbedoColor`.
- Replace mode decodes an imported PNG to RGBA8 and registers it with
  `GpuTextureSystem.TryAddTexture`.
- Full Canopy decodes the PNG identically, then uses the material-gated shader projection to map it
  once over the complete canopy rather than repeating it through the authored panel UVs.
- Center-decal mode detects KSA's runtime BC7 stock texture, reopens its source KTX2 with an explicit
  `Rgba32` transcode request, copies RGBA8 mip 0 into a `GenericTexture`, alpha-blends a scaled PNG
  into its center, and uploads the result. The stock alpha is retained so transparent decal pixels
  cannot cut holes in the canopy. A native, non-transcodable BC7 source degrades to a flat white
  (therefore tintable) base instead of disabling the feature.
- The stock normal texture is always retained. PBR controls either multiply the stock texture's
  R/G/B = AO/roughness/metallic channels through `RoughnessMetalScale`, or upload a 1×1 uniform PBR
  texture for direct 0–1 values.

Applied settings are included in Unscience scene saves (see adapter below). Imported files persist in the shared `.unscience/pngs` catalog owned by
`ksa-abstractions.lib/PngLibrary.cs` and also consumed by graffiti. Generated KSA assets are
intentionally retained until renderer shutdown so frames in flight never reference a freed material
or bindless handle. Before the first replacement, Restore/unload records each renderable's native
slot-zero handle and restores that exact value only while the slot still contains free-fallin's
handle; the weak tracking table is then cleared so recreated canopies capture their new native
selections afresh.

## Touchpoints

| # | Kind | Mod code | Game member / asset | Decomp/content path (5482) | Risk / invariant |
|---|---|---|---|---|---|
| 1 | **Harmony prefix** | `FreeFallinPatches.cs:18-20` (lookup by name, no parameter types), `:54` (`BeforeDraw(ChuteRenderable __instance)`) | `ChuteRenderable.Draw(ViewHandle view, float3[], float[]?, floatQuat[]?, ref readonly double4x4, float, double)` | `KSA/ChuteRenderable.cs:34` | **Signature @5482 (compatible):** rev 5474 added the leading `ViewHandle view` and the nested call is now `_renderable.Draw(view)` (`:43`). Still the single overload; the prefix binds only `__instance`, so the new parameter is transparent. Caller chain: `Vehicle.UpdateParachuteRenderData(IViewport)` (`KSA/Vehicle.cs:3743`; 5638 `:3769`) → `Parachute.DrawCanopy(ViewHandle, double, ref readonly double4x4)` (`KSA/Parachute.cs:1185`; 5638 `:1183`). `ChuteRenderable.cs` is byte-identical 5482→5638. Rename/extra overload is loud at patch setup. Prefix must run before the nested draw. |
| 2 | **Private reflection** | `FreeFallinPatches.cs:12-13` | `ChuteRenderable._renderable : AnimatedRenderable` | `KSA/ChuteRenderable.cs:12` | String-named private field; rename is a silent-compile/runtime-patch failure. Add to every update's reflection watchlist. |
| 3 | **Protected reflection** | `FreeFallinPatches.cs:14-15` | `AnimatedRenderable.MaterialIndices : int[]` | `KSA/AnimatedRenderable.cs:32` (read per draw at `:204`, `:223`; 5638 `:226`, `:247`) | Slot zero must remain the canopy mesh's material. Rename or material-slot reordering breaks customization/restoration. @5482 colour (MainOpaque) and OpaquePrePass share `ColourMeshBucketHandles`, shadows use `ShadowRenderablePool`; all copy `MaterialIndices[i]` at draw time, so one slot-0 write still covers every pass in every view. |
| 4 | Direct GPU API | `CanopyMaterialController.cs` | `GpuObjectSystem<MaterialData>.CreateObject`; `GpuMaterialSystem.GetOrLoad` | `KSA/GpuObjectSystem.cs:45`; `KSA/GpuMaterialSystem.cs` | Allocates one immutable material per Apply. `MaterialData` field order is shader ABI. |
| 5 | Direct GPU API | `CanopyMaterialController.cs` | `GpuTextureSystem.TryAddTexture/GetOrLoad`, sampler/default handles | `KSA/GpuTextureSystem.cs:85` | Adds replacement/composited albedo and optional uniform PBR textures to KSA's bindless table. |
| 6 | Direct asset API | `CanopyMaterialController.cs` | `ModLibrary.Get<PbrMaterialReference>("ParachuteCanopy_Material_CheckerLongOrange")`; diffuse/normal/PBR references | `KSA/ModLibrary.cs`; `KSA/PbrMaterialReference.cs` | The old `ParachuteCanopy_Material` id was removed in 5438. CheckerLongOrange is an authored native entry and the stable source for the custom material's maps. |
| 7 | Asset + CPU transcode | `CanopyMaterialController.cs` | Native `ParachuteCanopy_Material_*` set, `TextureReference.ModPath`, selected diffuse/normal/PBR textures | `Content/Core/ParachuteAssets.xml:23-57`; `Brutal.TextureApi.Ktx/Loader.cs` | 5438 ships CheckerLongOrange, CheckerExtraLongOrange, CheckerLongRed, StripesRed, Tricolor and White. Center-decal mode reopens the selected source KTX2 and requests `KtxTranscodeFmt.Rgba32`; native/non-transcodable BC7 falls back to a flat tintable base. |
| 8 | Shader ABI | `CanopyMaterialController.cs` | `MaterialData.{AlbedoTexture,Sampler,AlbedoColor,NormalTexture,RoughMetallicAOTexture,RoughnessMetalScale,ExtraData,EmissiveTexture}` | `KSA/MaterialData.cs`; `Content/Core/Shaders/Common/MaterialSet.glsl:28-41` | Shader defines albedo multiplication and PBR channel order R=AO, G=roughness, B=metallic. Full Canopy owns `ExtraData = (projection scale, cos rotation, sin rotation, 31415 marker)`. Recheck layout and ownership together. |
| 9 | **Harmony prefix** | `CanopyProjectionShaders.cs` | `ShaderModuleUtils.FromFile(Device, string, out VkShaderStageFlags, CompileOptions?)` | `RenderCore/ShaderModuleUtils.cs:117` (5638 `:84`) | Intercepts only three exact shader filenames, preserves compile options and original path as debug/include root, and falls back to stock compilation on failure. Parameter names/types are Harmony-binding dependencies. |
| 10 | **Harmony prefix** | `CanopyProjectionShaders.cs` | `KSA.Rendering.Utils.SetShaderFromMod(SimpleShaderStages, Device, string modId, bool useCustomOptions)` | `KSA.Rendering/Utils.cs:589` (5638 `:625`, body unchanged) | Changes `useCustomOptions` to true only for the three projection shader ids. Without this seam, ordinary model pipelines reuse cached stock modules and Full Canopy renders identically to Replace. Parameter name `useCustomOptions` is load-bearing. |
| 11 | Shader text + assets | `CanopyShaderSource.cs`; `CanopyProjectionShaders.cs` | `Model.vert`, `Model_Skinned.vert`, `ModelPbr.frag` (+ `ModelNormal.frag`, `ModelTranslucent.frag` for the layout cross-check), `TextureSet.glsl`, `MaterialSet.glsl` | `Content/Core/DefaultAssets.xml:74,78-81`; `Content/Core/Shaders/Mesh/Model{,_Skinned}.vert`; `Mesh/Model{Pbr,Normal,Translucent}.frag`; `Common/{TextureSet,MaterialSet}.glsl` | Exact, unique declaration/assignment/call anchors are prevalidated. Varying location 3 must be free in both vertex outputs and the fragment inputs (checked). `SET_TEXTURE`/`SET_MATERIAL` are **parsed from `ModelPbr.frag`** (5638: 5/1; 5482: 1/2) and must match both partner fragments; missing/conflicting/non-numeric defines fail closed. Since 5530 `TextureSet.glsl` `#error`s without `SET_TEXTURE` and `MaterialSet.glsl` includes it, so both defines precede the first include. The material SSBO (`GpuMaterialSystem.cs:33`) and bindless library (`RenderCore.Systems/BindlessTextureLibrary.cs:80`) layouts include `VertexBit`. |
| 12 | Direct render API | `CanopyProjectionShaders.cs`; `CanopyMaterialController.cs` | `Program.RendererRebuildNeeded`; `GltfSystemSkinned.GetOrLoad("ParachuteCanopyGlb").Skeleton`; `ChuteCanopyBones.MeasureBindHemRadius` | `KSA/Program.cs:430,2141-2145` (5638 `:443,2277-2278`); `KSA/GltfPbrAssetRef.cs`; `KSA/ChuteCanopyBones.cs:65` | Shader arm/disarm rebuilds pipelines at the game's frame boundary. Full Canopy projection scale depends on the bind skeleton's X/Z hem radius and axis convention. |
| 13 | Lifecycle | both hosts | StarMap attributes, `ISubmod`, consolidated Harmony, `HotkeyGuard` | `free-fallin/Mod.cs`, `Patcher.cs`; `unscience/Mod.cs`, `Patcher.cs` | Standalone and umbrella hosts must apply/remove exactly once. |

## Game-update checklist

1. Build against the new KSA assemblies.
2. Recheck the two reflected fields by exact name and type.
3. Verify `ChuteRenderable.Draw` still owns the only canopy submission and still uses material slot 0.
4. Diff `MaterialData` against `MaterialSet.glsl`; confirm AO/roughness/metallic channel semantics
   and that `ExtraData` remains available for the projection marker/parameters.
5. Verify `ParachuteAssets.xml` ids, `TextureReference.ModPath`, and that reopening the stock KTX2
   with `KtxTranscodeFmt.Rgba32` produces RGBA8.
6. Verify `SetShaderFromMod` still defaults to cached modules and that forcing `useCustomOptions`
   still routes all three target ids through `CompileVariantWithCustomOptions` and `FromFile`.
7. Run `ksa-upgrade.tests` with `KSAFolder` set (it derives the sets from and patches the live
   shaders), then compile/link both static+fragment and skinned+fragment pairs offline and reflect
   them; verify varying location 3, `MaterialBuffer` at `(SET_MATERIAL, 0)` in the skinned vertex
   shader, and that every pipeline pairing `ModelSkinnedVert` (`SuperMeshRenderSystem`,
   `CharacterRenderResources.EyeRenderer`) still binds materials and the bindless library there.
8. Live-test stock tint, panel replacement, Full Canopy orientation while reefing/inflating, center
   decal, uniform metallic/roughness, secondary viewports, shadows, Restore Stock, and unload with a
   canopy already deployed.

## Area summary — Update-risk findings (5402 → 5438)

KSA 5438 removed the former global `ParachuteCanopy_Material` asset and authored a native set of
selected canopy materials. `CanopyMaterialController` now uses
`ParachuteCanopy_Material_CheckerLongOrange` as its stable source for normal/PBR maps and texture
metadata. Runtime canopies still choose their own native material through
`Parachute.CanopyMaterial?.Id`; free-fallin captures each renderable's slot-zero handle before the
first replacement, restores that handle only when the slot still contains the mod's handle, and
clears tracking after restoration. This preserves native choices across disable, scene reload and
unload, including canopies that selected a different authored style.

**Residual live check:** deploy canopies using multiple native styles, apply and remove a custom
material, then reload the scene and verify every canopy returns to the style it originally selected.
The 5438 XML ids and source contract are verified; final build and managed test results are recorded in the upgrade report.

## KSA 5482 (5438 → 5482) verification

Verified 2026-09-25 against `2026.9.22.5482`, diffed from `2026.9.10.5438`. Static/managed only;
no native KSA run. **No free-fallin code change was needed.** Evidence:
[KSA_5482_UPGRADE](../plans/KSA_5482_UPGRADE.md).

- ✅ **`ChuteRenderable.Draw` gained a leading `ViewHandle view` (compatible).** Rev 5474 keyed mesh
  buckets by render pass and view: `Vehicle.UpdateParachuteRenderData(IViewport)` (`Vehicle.cs:3743`)
  now passes `ViewForViewport(viewport)` through `Parachute.DrawCanopy(ViewHandle, …)`
  (`Parachute.cs:1185`) to `ChuteRenderable.Draw(ViewHandle, …)` (`ChuteRenderable.cs:34`), which calls
  `_renderable.Draw(view)` (`:43`). The name-only lookup still finds the single overload and the prefix
  binds only `__instance`, so it attaches and still runs before the nested draw.
- ✅ **Slot-0 swap still reaches every pass and view.** `AnimatedRenderable.Draw(ViewHandle)`
  (`:187-233`) sends colour and opaque prepass through `ColourMeshBucketHandles` and shadows through
  `ShadowRenderablePool` / `DepthMeshBucketHandles`; each copies `MaterialIndices[i]` into the instance
  data at draw time (`:204`, `:223`). `_renderable` (`ChuteRenderable.cs:12`) and `MaterialIndices`
  (`AnimatedRenderable.cs:32`) keep their names and types.
- ✅ **Full Canopy shader seam unchanged.** `SetShaderFromMod` is still the path for the static,
  skinned and skinned two-sided PBR pipelines (`SuperMeshRenderSystem.cs:644-645,886-898`, helper `SetShaders` `:943-951`);
  `Utils.cs`, `ShaderModuleUtils.cs` and `ShaderReference.cs` are byte-identical. `Model.vert`,
  `Model_Skinned.vert`, `MaterialSet.glsl`, `TextureSet.glsl`, `DefaultAssets.xml`, `ParachuteAssets.xml`,
  `ChuteCanopyBones.cs` and `GltfPbrAssetRef.cs` are identical; `ChuteCanopyPose.cs` changed only a log
  line. All anchors hold; varying location 3 is still free.
- ⚠️ **BRDF fix (rev 5472, visual).** `ModelPbr.frag:145` now samples the BRDF LUT with `roughness`
  instead of `1.0 - roughness`; the line is far from every Full Canopy anchor. The in-memory patcher
  reads the current on-disk source, so it inherits the fix. Roughness *values* keep their meaning
  (R=AO, G=roughness, B=metallic), but saved roughness/metallic presets render with different
  fresnel/sheen.
- ℹ️ Test fidelity: `ksa-upgrade.tests/FreeFallinFixture.cs:37` still mirrors the parameterless
  `Draw()`. The check still passes because the prefix binds only `__instance`.
- **Native acceptance pending:** every mode with a deployed canopy in the main and a secondary
  viewport, including shadows; Full Canopy compiles for `Model_Skinned.vert` and `ModelPbr.frag`;
  uniform roughness/metallic 0 vs 1 under the corrected BRDF; Restore Stock; the multi-style restore
  check above (outstanding since 5438).

## KSA 5638 (5482 → 5638) verification

Verified 2026-10-22 against `2026.10.19.5638` (`ksa-game-assemblies` `edda844`), diffed from
`2026.9.22.5482` (`a2f724f`). Static/managed plus offline shader compilation; no native KSA run.

- 🔴 **Full Canopy descriptor-set mismatch — fixed.** Rev 5530 renumbered the mesh-technique sets.
  `ModelPbr.frag` went from `SET_TEXTURE 1`/`SET_MATERIAL 2` (CSM 3, lighting 4, global 5) to
  `SET_TEXTURE 5`, `SET_MATERIAL 1`, `SET_CSM 2`, `SET_LIGHTING 3`, `SET_GLOBAL 4`, AO 6, RT output 7
  (`Mesh/ModelPbr.frag:4-32`). The layout comes from `MeshRenderTechnique` (`MaterialSetBinding = 1`,
  `:46`; bindless library at `GlobalShaderBindingsBinding + 1`, `:68-77`) and
  `SimplePipeline.CreateLayout` (`RenderCore.Pipelines/SimplePipeline.cs:96`). The old hard-coded
  defines still compiled but declared `MaterialBuffer` at `(2,0)`, which is now the CSM UBO. Because
  `Apply` arms unconditionally, this patched vertex shader would have replaced stock
  `ModelSkinnedVert` in every skinned pipeline: kittens, eyes, pre-pass and two-sided canopies.
  `CanopyShaderSource` now parses both defines from the live `ModelPbr.frag`. It requires
  `ModelNormal.frag` and `ModelTranslucent.frag` to agree, because the patched vertex shader is
  shared by:
  - the skinned PBR, two-sided, raytraced and pre-pass techniques
    (`SuperMeshRenderSystem.cs:602-612,641-658,715-724,817-839,882-949`);
  - `CharacterRenderResources.EyeRenderer` (`:82,318`).

  All of these declare material set 1, global 4 and bindless 5. Any parse, anchor or location failure
  happens before patching and leaves the stock shaders untouched.
- ✅ **Other shader assumptions hold.** The anchors are unique in the 5638 sources: `#version 450`,
  the `outInstanceID` declaration, the `outWorldPosV` assignment, the `inInstanceID` declaration and the
  `GetMaterialData(...)` call (`ModelPbr.frag:65`). Location 3 is free in `Model.vert` and
  `Model_Skinned.vert` outputs and in `ModelPbr.frag` inputs. The `Material` struct
  (`MaterialSet.glsl:12-21`) and the `MaterialData` field order are unchanged. `TextureSet.glsl:21-23`
  `#error`s without `SET_TEXTURE`, and `MaterialSet.glsl:6` now includes it itself. The patch defines
  both indices before its includes, which still works with the 5482 header. Stock material sampling now
  marks indices `nonuniformEXT`, and the Full Canopy albedo lookup does the same.
- ✅ **Offline reflection proof.** The production `CanopyShaderSource` patched the live 5638 sources.
  They were compiled with `glslc --target-env=vulkan1.3 -DINSTANCED=1` (plus a
  `-DRAYTRACED_REFLECTIONS=1` fragment variant) and reflected with `spirv-cross --reflect`:
  - patched `Model_Skinned.vert`: `UBOScene (0,0)`, `JointMatrices (0,1)`, `MaterialBuffer (1,0)`, the
    bindless arrays at set 5 (bindings 0/1), and output `outFreeFallinUv` at location 3 (`vec2`);
  - patched `ModelPbr.frag`: `MaterialBuffer (1,0)`, CSM `(2,0)`, lighting set 3, `Global`/`GlobalConstants`
    at set 4, bindless set 5, AO `(6,0)`, RT set 7, and input `inFreeFallinUv` at location 3;
  - patched `Model.vert`: output at location 3 only.

  Both layouts include the vertex stage: `GpuMaterialSystem.cs:33` and `BindlessTextureLibrary.cs:80`.
- ✅ **Unchanged seams.** These are unchanged:
  - `ChuteRenderable.cs` (byte-identical) and `ParachuteAssets.xml`;
  - the `SetShaderFromMod` body (`Utils.cs:625`) and `ShaderModuleUtils.FromFile` (`:84`; decompiler
    churn only);
  - `MaterialData`, `GpuMaterialSystem` and `AnimatedRenderable.MaterialIndices`. The latter is still
    written into the colour/pre-pass and shadow instance data (`:226,247`); 5638 only changed shadow
    bounds.

  `ChuteCanopyBones.MeasureBindHemRadius` (`:65`), `AssetManager.AssetMap` (`:11`),
  `GpuTextureSystem.TryAddTexture` (`:104`) and `GpuObjectSystem.CreateObject` (`:45`) keep their
  shapes.
- ℹ️ `ksa-upgrade.tests/FreeFallinFixture.cs` still models a parameterless `ChuteRenderable.Draw()`.
  The prefix binds only `__instance` and the lookup is by name, so the test is unaffected and was left
  as is. The new `FreeFallinShaderChecks` cover the parser and the live-shader patch.
- **Native acceptance pending:**
  - Full Canopy compiles: look for the `free-fallin: full-canopy projection uses SET_TEXTURE 5,
    SET_MATERIAL 1` and `compiled full-canopy shader` console lines;
  - with Vulkan validation enabled, there are no descriptor-type errors on kittens, eyes or canopies
    after arming;
  - canopy orientation while reefing/inflating, in the main and secondary viewports;
  - every earlier pending item above.

## Save/load adapter (feature/saves)

`FreeFallinSubmod.Saves` registers `ISaveParticipantSource` with applied canopy settings.
`CanopyMaterialController.AppliedSettings` is a detached recipe committed after successful
material assignment; reset invokes `FreeFallinPatches.RestoreStock` before native teardown.
`ReplaceObserved(handle)` updates the existing weakly tracked `AnimatedRenderable` material
indices before retiring a private material or texture.

New reflection seam: `CanopyGpuAssets.Own<T>` reads protected
`KSA.AssetManager<T>.AssetMap` as `ConcurrentDictionary<Core.AssetName,T>` and removes only
its exact owned reference before `LoadedAssetRef.Dispose()`. Current KSA
`GpuTextureAssetRef.Destroy` uses `RetiredResourceQueue`; material disposal returns its own
object slot. Private releases wait on `Program.GetRenderer().Device.WaitIdle()`, preserve
failed retirement ownership and do not touch borrowed stock assets. Recheck map type,
reference identity removal and native material/texture retirement on updates.

Acceptance: repeated save/load/apply/stock cycles, both stock/custom PBR, all texture modes,
missing PNG, hidden HUD and loaded/deployed canopies; inspect material/texture slot reuse.

The adapter also stores the effective albedo tracked by `MaterialColorState`, independently
of the authored recipe. This includes Humble Arteest writes to the private canopy material.
New allocations register `MaterialData.AlbedoColor`; their exact allocation release callback
forgets the runtime handle. Humble excludes `free-fallin/` generated names and guards its
original-color resets by material identity. Acceptance must include canopy Apply → Humble
recolor → save/load and canopy reapply → load, checking tint and recycled material slots.
