# Parachutes (free-fallin) — Game Integration Scope

Permanent reference for detecting when KSA game updates break **free-fallin**, the global parachute
texture/tint/PBR customizer. Cataloged against KSA build **2026.9.10.5438** at
`../ksa-game-assemblies/current/decomp` and `../ksa-game-assemblies/current/Content`.

All logic is in `free-fallin.lib` (`FreeFallinSubmod : ISubmod`,
`FreeFallinPatches.Apply/Remove`), consumed by both the standalone `free-fallin` host and
`unscience`. The standalone Patcher also applies the required `HotkeyGuard`.

## Integration model

KSA creates one `ChuteRenderable` per live canopy and selects material slot zero from
`Parachute.CanopyMaterial?.Id`. Its `Draw` method updates the cloth pose and calls a private
`AnimatedRenderable`. Free-fallin prefixes that draw, reflects `_renderable`, then swaps element
zero of its protected `MaterialIndices` array to a mod-created material handle. The array is read
by `AnimatedRenderable.Draw` for the main, pre-pass, and shadow submissions, so one substitution
keeps every pass consistent and follows the skinned cloth automatically.

Full Canopy additionally prefixes `KSA.Rendering.Utils.SetShaderFromMod` and
`ShaderModuleUtils.FromFile`, then patches `Model.vert`, `Model_Skinned.vert`, and `ModelPbr.frag` in
memory during the game's normal renderer rebuild. `SetShaderFromMod` ordinarily reuses the cached
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

| # | Kind | Mod code | Game member / asset | Decomp/content path (5438) | Risk / invariant |
|---|---|---|---|---|---|
| 1 | **Harmony prefix** | `FreeFallinPatches.cs` | `ChuteRenderable.Draw(float3[], float[]?, floatQuat[]?, ref readonly double4x4, float, double)` | `KSA/ChuteRenderable.cs:32` | Single overload today. Rename/signature change is loud at patch setup/build. Prefix must run before the nested `_renderable.Draw()`. |
| 2 | **Private reflection** | `FreeFallinPatches.cs` | `ChuteRenderable._renderable : AnimatedRenderable` | `KSA/ChuteRenderable.cs:13` | String-named private field; rename is a silent-compile/runtime-patch failure. Add to every update's reflection watchlist. |
| 3 | **Protected reflection** | `FreeFallinPatches.cs` | `AnimatedRenderable.MaterialIndices : int[]` | `KSA/AnimatedRenderable.cs:34` | Slot zero must remain the canopy mesh's material. Rename or material-slot reordering breaks customization/restoration. |
| 4 | Direct GPU API | `CanopyMaterialController.cs` | `GpuObjectSystem<MaterialData>.CreateObject`; `GpuMaterialSystem.GetOrLoad` | `KSA/GpuObjectSystem.cs:45`; `KSA/GpuMaterialSystem.cs` | Allocates one immutable material per Apply. `MaterialData` field order is shader ABI. |
| 5 | Direct GPU API | `CanopyMaterialController.cs` | `GpuTextureSystem.TryAddTexture/GetOrLoad`, sampler/default handles | `KSA/GpuTextureSystem.cs:85` | Adds replacement/composited albedo and optional uniform PBR textures to KSA's bindless table. |
| 6 | Direct asset API | `CanopyMaterialController.cs` | `ModLibrary.Get<PbrMaterialReference>("ParachuteCanopy_Material_CheckerLongOrange")`; diffuse/normal/PBR references | `KSA/ModLibrary.cs`; `KSA/PbrMaterialReference.cs` | The old `ParachuteCanopy_Material` id was removed in 5438. CheckerLongOrange is an authored native entry and the stable source for the custom material's maps. |
| 7 | Asset + CPU transcode | `CanopyMaterialController.cs` | Native `ParachuteCanopy_Material_*` set, `TextureReference.ModPath`, selected diffuse/normal/PBR textures | `Content/Core/ParachuteAssets.xml:23-57`; `Brutal.TextureApi.Ktx/Loader.cs` | 5438 ships CheckerLongOrange, CheckerExtraLongOrange, CheckerLongRed, StripesRed, Tricolor and White. Center-decal mode reopens the selected source KTX2 and requests `KtxTranscodeFmt.Rgba32`; native/non-transcodable BC7 falls back to a flat tintable base. |
| 8 | Shader ABI | `CanopyMaterialController.cs` | `MaterialData.{AlbedoTexture,Sampler,AlbedoColor,NormalTexture,RoughMetallicAOTexture,RoughnessMetalScale,ExtraData,EmissiveTexture}` | `KSA/MaterialData.cs`; `Content/Core/Shaders/Common/MaterialSet.glsl:28-41` | Shader defines albedo multiplication and PBR channel order R=AO, G=roughness, B=metallic. Full Canopy owns `ExtraData = (projection scale, cos rotation, sin rotation, 31415 marker)`. Recheck layout and ownership together. |
| 9 | **Harmony prefix** | `CanopyProjectionShaders.cs` | `ShaderModuleUtils.FromFile(Device, string, out VkShaderStageFlags, CompileOptions?)` | `RenderCore/ShaderModuleUtils.cs:117` | Intercepts only three exact shader filenames, preserves compile options and original path as debug/include root, and falls back to stock compilation on failure. Parameter names/types are Harmony-binding dependencies. |
| 10 | **Harmony prefix** | `CanopyProjectionShaders.cs` | `KSA.Rendering.Utils.SetShaderFromMod(SimpleShaderStages, Device, string modId, bool useCustomOptions)` | `KSA.Rendering/Utils.cs:589` | Changes `useCustomOptions` to true only for the three projection shader ids. Without this seam, ordinary model pipelines reuse cached stock modules and Full Canopy renders identically to Replace. Parameter name `useCustomOptions` is load-bearing. |
| 11 | Shader text + assets | `CanopyProjectionShaders.cs` | `Model.vert`, `Model_Skinned.vert`, `ModelPbr.frag`, `TextureSet.glsl`, `MaterialSet.glsl` | `Content/Core/DefaultAssets.xml:78-80`; `Content/Core/Shaders/Mesh/Model{,_Skinned}.vert`; `Mesh/ModelPbr.frag`; `Common/{TextureSet,MaterialSet}.glsl` | Exact declaration/assignment/call anchors are prevalidated. Varying location 3 must be free and type-compatible in both vertex paths and the shared fragment. Descriptor sets 1/2 must remain texture/material; material storage buffer must retain vertex visibility. |
| 12 | Direct render API | `CanopyProjectionShaders.cs`; `CanopyMaterialController.cs` | `Program.RendererRebuildNeeded`; `GltfSystemSkinned.GetOrLoad("ParachuteCanopyGlb").Skeleton`; `ChuteCanopyBones.MeasureBindHemRadius` | `KSA/Program.cs:431,2096-2100`; `KSA/GltfPbrAssetRef.cs`; `KSA/ChuteCanopyBones.cs:48` | Shader arm/disarm rebuilds pipelines at the game's frame boundary. Full Canopy projection scale depends on the bind skeleton's X/Z hem radius and axis convention. |
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
7. Re-run Full Canopy's three shader anchor checks and compile/link both static+fragment and
   skinned+fragment pairs; verify varying location 3 and texture/material descriptor sets.
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
