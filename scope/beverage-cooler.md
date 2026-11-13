# Beverage Cooler

Cataloged against KSA **2026.11.10.5801**. Asset-only content loaded by Unscience's `mod.toml`;
there is no new C# project, Harmony patch, ISubmod, participant or runtime reflection lookup.
[Usage](../beverage-cooler/README.md) · [save acceptance](../plans/saves-acceptance.md#beverage-cooler).

| Integration | Source / native contract | Ownership and update risk |
|---|---|---|
| `BeverageCooler.xml`; StarMap `assets` | `unscience/mod.toml`; `unscience.csproj` copies XML, mesh, animation and 9 PNG maps | One bundled release; Art sources excluded. `scripts/check-dist.py` verifies byte-identical runtime payloads. |
| `PartTemplate.VariantRefs`; `VariantFamilyCheck.Run` | `KSA/PartTemplate.cs`; `KSA/VariantFamilyCheck.cs` | Head `Unscience_Cooler_Red`; child `Unscience_Cooler_StainlessSteel` inherits all attributes/gameplay and differs only in render subparts. Native validator rejects gameplay/DisplayName drift. |
| `EditorTagDef` / `EditorTags` | Native tags `Life Support`, `Structural`; custom `Unscience Supplies` | Both variants inherit all three editor filters from the head. |
| `SubParts` placement keys/order | `BeverageCooler.xml`: `Body`, `Lid`, `LidCollider` | Stable immediate-child slots bind native animation and saved module matching. Both variants share exactly the same collider-only `Unscience_Cooler_LidCollision` Ref; unique cosmetic subparts contain PartModel/MeshView only. |
| `MeshAtlas`, `PartModel`, `MeshView`, `PbrMaterial` | `Meshes/BeverageCooler.glb`; `Unscience_Cooler_{BodyShell,BodyTrim,BodyFoil,LidShell,LidTrim,LidFoil,Body_VM,Lid_VM}`; `BeverageCooler_{Red,Steel,Common}_{Diffuse,Normal,ORM}.png` | Six visible meshes and two picking proxies; KSA +X forward/−Z up. Body origin at base, lid geometry relative to hinge. Picking follows animated render subparts. |
| `CoreSurfacesB_Material` / `TrussFFoilA` atlas patch | `Content/Core/CoreSurfacesBAssets.xml`; native diffuse/normal/PBR maps | BodyFoil/LidFoil line the tub and lid underside. UVs stay inside U=.495–.605/V=.39–.49, with approximately .16 UV/metre per-face mapping. Only references ship; no Core texture payload is copied. |
| `PartModelRenderer` backface culling | `KSA/PartModelRenderer.cs:197–198,673–674`: BackBit/CCW | Triangle winding determines visibility. Default two-sided Blender display hid the reversed underside; author previews now use backface culling and exported cross-product/ray/seam checks are required. |
| `TextureReference.BindImage`, `BucketType`, `SimpleVkTexture` | `KSA/TextureReference.cs:207–232`; `KSA/BucketType.cs:29–34`; `RenderCore/SimpleVkTexture.cs:260–328` | Nine 128px PNG sources retain native dimensions. BucketType groups summary statistics; allocation uses the source descriptor and only downsizes over-limit textures. Texture references bind once and are shared across cooler instances. |
| `PartModel.Get`, `ModuleBase.TemplateDataBase.Hash` | `KSA/PartModel.cs:413–445`; `KSA/ModuleBase.cs:12–26` | Process-wide render-model cache uses component Key/hash alone, ignoring owning row, mesh and material. All twelve models use globally unique `Unscience_Cooler_{Red,Steel}{Body,Lid}{Shell,Trim,Foil}` keys. The original eight stay intact; four stateless foil keys are added. |
| `KeyframeAnimationModule.Template` / `KeyframeAnimationData.Template` | `KSA/KeyframeAnimationModule.cs`; `KSA/KeyframeAnimationData.cs`; `Animations/BeverageCooler_Lid.glb` | Head module `LidAnimation`; ShowDeployRetract=false exposes native Actuate slider. Loader selects the first GLB clip (`Actuation`), not by name. Animated Hinge ancestor at (−.19,0,−.409), unanimated Lid and LidCollider children; directly animated named child would fail binding. +100°Y over 1.2s raises the front. |
| `HabitatTemplate`, `Habitat.ComputeFullLoad`, habitat passages | `KSA/HabitatTemplate.cs`; `KSA/Habitat.cs`; `KSA/HabitatZone.Build.cs` | Head module `Supplies`: 0.03m³ storage, zero habitable volume; native 12 meals +2 scrubbers, 7.8kg full consumables. PassesSurfaceAttachments=true permits this side, but both ends/intervening parts must pass. No custom resource, beverage simulation, water or oxygen tank. |
| `ColliderModule.Template`, `Box`, `CustomMass`, surface connector | `KSA/ColliderModule.cs`; `KSA/BoxColliderTemplate.cs`; `KSA/Part.cs` | Root hollow body: base+four walls. Shared lid box moves with animation, same mechanics across variants. SurfaceMount rotates local +X to model +Z. Empty mass 5.5kg; loaded consumable mass native. No pressure/crew passage implied by collider geometry. |
| `Part.GetSaveData`, native module saves | `KSA/Part.cs`; `KSA/KeyframeAnimationModule.cs:383–427`; `KSA/Habitat.cs:383–417` | KSA owns selected template ID, placement, supplies/debts and animation current/goal. No sidecar/state duplication. Blueprint contents follow native blueprint policy. |

## Validation and remaining native checks

The foil-lining correction passes the full solution build with zero warnings/errors, source and
packaged surface/budget checks, distribution validation, the native twelve-model cache/variant/
animation/save harness and the managed Wing Walker suite (including 32 crew checks). Exported
underside rays face the viewer, and the welded shell/foil lid has 210 paired, oppositely directed
edges. Core maps are referenced rather than shipped. In-game visual confirmation remains pending.

### Runtime asset budgets

| Exported workload | Original | Low-poly replacement |
|---|---:|---:|
| Visible triangles | 9,260 | 710 |
| Visible vertices, including attribute splits | 18,657 | 1,360 |
| Picking triangles | 9,260 | 112 |
| Picking vertices | 20,557 | 192 |
| Model GLB bytes | 1,505,588 | 82,236 |
| Texture maps | 9 × 512px | 9 × 128px |

The replacement uses low-poly visible geometry and separate coarse open-tub/lid picking proxies.
Smooth bevels and detailed hardware are omitted from the proxies; coarse exterior extent planes
preserve native part bounds. Physical collider XML, part IDs, the earlier six mesh IDs/eight render
keys, placement/module keys, native supplies and actuation are unchanged. Two foil mesh IDs and four
stateless render keys are added. Older craft resolve the new
art under the same asset references.

`scripts/check-cooler-assets.py` reads actual GLB JSON accessors and binary data, validates index
ranges/attribute counts, and enforces **≤1,000 visible triangles / 2,000 exported vertices** and
**≤120 picking triangles / 192 exported vertices**. It also checks eight meshes, one indexed
triangle primitive each, baked identity nodes, visible normals/UVs/tangents, no unused accessors,
local proxy bounds enclosing each part's visible geometry, twelve globally unique cache keys
and exactly nine stable PNG names with dimensions ≤128px. `scripts/check-dist.py` runs it on the
package after verifying byte equality with source. Native cache checks now cover all twelve models
and exact Core foil material bindings; variant/animation/save behavior is unchanged.

The low-poly source and packaged exports pass the budget/bounds checker and distribution validation.
The full solution builds with zero warnings/errors; the native 5801 cache, variant, animation,
supplies and save-data harness passes against the optimized assets. Blender checks cover both
finishes, the open interior and closed/half/open wireframes. In-game frame-time profiling is pending.

Native `Part.RayCastEgoSubPart` (`KSA/Part.cs:2816–2849`) passes MeshView `PositionCompare` data to
`Ray.RaycastWatertight` (`KSA/Ray.cs:207–281`), which loops over every triangle for CPU tests;
much smaller proxies reduce that scan's work.
This does not establish a particular FPS improvement or
the cause of the reported slowdown. No baseline frame-time trace established that cause. The 128px
maps have **16× fewer source texels** than the former 512px maps, with no native minimum bucket
upscaling; texture references are shared across placed coolers. Compare a fixed scene/camera/settings
before and after, including multiple cooler instances, in the linked native acceptance checks.

### Native behavior

The next in-game report exposed a see-through underside after simplification. The old lid's
underside triangles and exported normals both pointed −Z; native CCW backface culling therefore
rejected them from below. Merely checking normals or a two-sided preview could not catch it.
The repaired tub/underside use native foil meshes with explicit outward winding. The lid foil
outer loop exactly matches the shell, closing the former annular gap; no global normal-recalculation
heuristic is used to decide the winding of these nested surfaces.

`check-cooler-surfaces.py` uses actual binary positions/indices to intersect rays and compute face
normals by cross product. It requires nearest front-facing coverage, agreeing exported normals,
expected foil bindings/heights, nine floor samples, seven underside/edge samples, the exterior
bottom and four inward-facing tub walls. A one-micrometre positional weld also requires every
LidShell/LidFoil edge to have two opposite directed uses, protecting the complete seam beyond the
ray samples. This is visual geometry closure, with no change to native pressure or collider behavior.
Controlled temporary-asset checks reject a central cap whose indices and normals are flipped
together (normal agreement still passes, but the underside ray is a backface), a removed rim
triangle (open welded seam), and a foil XML binding changed to the common material.

The first in-game test confirmed actuation, but exposed an entire body rendered as the lid and
both variants red. Reused Shell/Trim keys made `PartModel.Get` return the first red body's cached
mesh/material templates for the other render definitions. The current XML uses twelve globally
unique render keys. A real-DLL regression reproduces the old cache alias, then checks exact returned
templates, model identity and expected body/lid mesh plus red/steel/common/Core foil refs for all
twelve components. The earlier parser/GLB/serialization checks did not exercise this cache.
Restart KSA after updating; in-game visual retest remains pending. Static render-key changes add no
saved state or migration: existing template IDs, placement slots, supplies and animation keys stay
stable, and PartModelModule has no native save payload.

The full solution builds against CURRENT 5801 DLLs with **0 warnings and 0 errors**; the fresh
Unscience distribution passes `scripts/check-dist.py`, including byte-identical XML, GLB and PNG
payloads and exclusion of editable Art. This host used existing restored dependencies with
`dotnet build ksa-mod-experiments.slnx --no-restore --disable-build-servers -m:1
-p:UseSharedCompilation=false`, explicit CURRENT `KSAFolder` and a temporary `SelectedDistModDir`.
Static GLB inspection confirms six one-primitive meshes, identity node transforms, finite bounds
and visible mesh normals/UVs/tangents.

Supply access also follows `Vehicle.TransferSupplies` (shared zone plus an able crew carrier)
and `KittenEva.CheckCarry` (life support on, suitable EVA cargo, healthy kitten, storage within
2 m). Exterior storage can be retrieved by EVA without a cabin passage; no custom access bypass
is added.

`wing-walker.native-tests` uses the actual 5801 prepass, strict content deserializer and native
VariantFamilyCheck. It verifies inherited mechanics/stable slot order, rejects a changed variant
DisplayName, a unique mechanical subpart or different Habitat capacity, computes 12 meals+2
scrubbers, and round-trips both variant IDs, distinct depleted
supplies, fractional consumption and partially opening/closing lid data through native PartInstance
XML overrides. It imports the actual animation GLB and samples both lid slots closed, halfway and
fully open, checking rotation and a fixed hinge. These CPU checks do not initialize a renderer, build a live habitat zone, reconstruct
a whole world or exercise contact solving. Native editor appearance, material thumbnails/variants,
lid picking/collisions, accessible supplies and repeated save/load/blueprints remain pending.

On an upstream update, rerun the native parser/variant checks, inspect module serialization and
animation node-binding semantics, then perform the linked save and physical acceptance checks.
