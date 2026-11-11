# DOH: click-to-spawn research

Status: research and proposed design; the feature is not implemented. Verified against the supplied
KSA **2026.11.10.5801** CURRENT sources, Content and DLLs. The in-repository `decomp/ksa` tree and
the modding skill's older 5554 examples were not used as the current API authority.

## Recommendation

Add **Spawn at...** as a one-shot viewport placement tool. Pick the nearest physical part surface
or terrain, show the prospective kitten capsule, then create the kitten only after a full-volume
collision check succeeds. Reuse DOH's creation, roster, naming, paint and native-save ownership.

This is feasible with the current public game/Bepu APIs. The substantial work is validating the
placement volume, matching coordinate frames/time and integrating safe input/physics lifecycles.
Adding a raycast and a fixed height offset alone does not establish collision-free placement.

## Proposed interaction

1. **Spawn at...** snapshots the name, character and paint recipe and arms placement. It does not
   require the reference-vehicle dropdown; the world click supplies the target.
2. Over the main flight viewport, show a hit marker and capsule outline: valid, blocked, or
   unavailable. Throttle expensive preview checks; always validate again when committing.
3. Left-click a valid surface to spawn one kitten and leave placement mode. A miss or blocked
   location keeps it armed and explains the failure. Esc/right-click cancels.
4. Ignore the arming button's own click and all clicks captured by ImGui. Reject editor/map views
   and secondary feeds initially. Closing the DOH panel can leave the picker active, as with the
   existing floating placement tools; scene replacement and unload must cancel it.

Initial recommendation: one kitten per click. The existing Count/Offset controls keep their current
vehicle-relative behavior. If click placement later supports a batch, independently ground and
validate every member, including pairwise capsule clearance. The existing offset-chain loop would
otherwise stack/overlap an absolute-position batch.

Default orientation recommendation: align the kitten's feet-to-head direction with the outward
surface normal and choose heading from camera forward projected onto that plane. This gives an
unambiguous “bottom of capsule at the clicked spot,” including on a vessel. A vertical-wall click
therefore produces a sideways initial pose; normal gravity/locomotion immediately takes over.
It does not attach the kitten. Keeping kittens gravity-upright instead is possible, but requires
the general support-point calculation below and a different preview.

## Existing code to reuse

| Existing implementation | Relevance |
|---|---|
| [DohSubmod.cs](../doh.lib/DohSubmod.cs), [Spawning UI](../doh.lib/DohSubmod.Spawning.cs) | Add the button and snapshot existing naming/model/paint controls |
| [KittenSpawner.cs](../doh.lib/Spawning/KittenSpawner.cs) | Keep material preflight, native constructor, roster transaction, naming, registration and failure cleanup |
| [KittenSpawner.Positioning.cs](../doh.lib/Spawning/KittenSpawner.Positioning.cs) | Current positioning is insufficient for an explicitly oriented, timestamped surface spawn |
| [Hot Pursuit placement](../hot-pursuit.lib/HotPursuitSubmod.Placement.cs) | One-shot arm/disarm, arming-frame guard, cursor hint, Esc/right-click and UI-click rejection |
| [Hot Pursuit picker](../hot-pursuit.lib/HotPursuitPicker.cs) | Vehicle/subpart hit identity and part-local coordinates |
| [Graffiti picker](../graffiti.lib/DecalPicker.cs) | EGO-to-CCF ray conversion and accurate terrain march/bisection |
| [PhysicsFrameHook.cs](../ksa-abstractions.lib/PhysicsFrameHook.cs) | Commit after worker results and before the next physics snapshots |
| [DohSubmod.Persistence.cs](../doh.lib/DohSubmod.Persistence.cs) | Existing `doh` v1/order60 capture, reset and native-object rebinding |

Keep DOH independent of the Graffiti/Hot Pursuit feature libraries. A small shared geometry helper
in `ksa-abstractions.lib` is reasonable if it has clear coordinate-frame contracts; otherwise a
DOH-specific picker avoids expanding this feature into a broad refactor.

## Picking the surface

### Parts

- Build the ray with `Cursor.GetEgoRay(Program.MainViewport)`. EGO has the camera as origin and
  ecliptic axes. Keep the same viewport/camera throughout a pick; check all components are finite.
- KSA 5801 exposes **`ColliderModule.RayCastEgo`** and **`Part.RayCastEgoColliders`**. The module
  method retains the exact collider/parent identity; the part method finds the nearest collider
  in its subtree. These query physical boxes, capsules, cylinders, spheres and convex hulls.
- For an individual module, the physical hit is `ray.Origin + distance * ray.Direction` in EGO.
  Its returned normal is in **vehicle assembly axes**, not the hit subpart's local axes. Rotate
  that normal by the vehicle orientation and normalize it.
- `Part.RayCastEgo` is the alternative for selecting the visible art surface. Its returned point
  and normal belong to **`closestSubPart`**, and its normal is a mesh vertex normal, not necessarily
  the geometric triangle normal. It cannot establish physical clearance. Use art picking for
  cursor feedback if desired, then reconcile/validate against physical colliders.
- Include nearby debris and other vehicles in obstruction tests. Honor live collider transforms
  and scale, including Godzilla's independent collider scaling. Do not use visual bounds as the
  final physics shape.

For this feature, physical hits are the recommended placement authority. Rendered art can differ
from collision geometry, so a small visible gap may be correct. If a clicked visible surface has
no physical support, either explicitly report that or mark the preview as a free-floating spawn;
do not silently promise a supported standing pose.

### Terrain and statics

Graffiti already evaluates the signed terrain clearance in CCF:

```text
F(x) = |x| - (body.MeanRadius + body.GetTerrainHeightFromDirCcf(x / |x|, accurate: true))
```

Convert the EGO ray relative to the body's EGO centre into CCF, bracket the first positive-to-negative
crossing, then bisect. For the local slope normal, finite-difference this same accurate height
function in a local tangent frame, or use the actual physical terrain triangle normal when available.
Radial up alone does not describe a slope. Bound range/work, reject a start below terrain, and handle
grazing rays and insufficient bracketing without declaring a valid hit. A fixed-step march can miss
narrow ridges; Graffiti's 64-step implementation is a useful starting point, not an exact ray oracle.

**Compare the terrain and part hit distances.** Graffiti currently short-circuits on any vehicle hit
(`TryPickVehicle(...) || TryPickTerrain(...)`), which can select a vehicle behind foreground terrain.
DOH should choose the nearest eligible surface across both tests.

Terrain height does not include launchpad structures or ground clutter. An active bubble's Bepu
raycast can find its actual static geometry. Where those are in the supported target area, include
them in picking and clearance tests. Do not label a CPU height-field-only result as clearance from
every physical object, or treat an ocean surface as solid walkable terrain.

## Correct capsule placement

The stock `KittenBackPackPart` contains this capsule in CURRENT
[PartGameData.xml](../../ksa-game-assemblies/current/Content/Core/PartGameData.xml), lines 826–838:

| Property | Authored value |
|---|---:|
| Assembly-space centre | `(0, 0, -0.59)` m |
| Collider rotation XYZ | `(-1.5708, 0, 0)` radians |
| Internal line-segment length (`LengthY`) | `0.45` m |
| Radius | `0.35` m |
| Total end-to-end height | `1.15` m |
| Approximate bottom in assembly coordinates | `(0, 0, -0.015)` m |

KSA's locomotion uses **local −Z as up, +X as forward**. The capsule's native axis is local Y,
rotated to approximately −Z by its collider orientation. The 0.45 m length excludes the spherical
ends; the centre-to-end distance is `0.45 / 2 + 0.35 = 0.575 m`.

These are evidence values, not constants to copy into production. Read the prepared/live capsule's
radius, length, orientation, position and scale. Multiple or unsupported added colliders need
validation too; fail explicitly rather than silently testing only one shape.

For a capsule aligned with outward hit normal `n`, the simple world-space solution is:

```text
desiredCapsuleCentre = hitPoint + n * (halfSegmentLength + radius + skin)
kittenComPosition    = desiredCapsuleCentre - rotate(Q, colliderCentreAsmb - kittenComAsmb)
```

Here `Q` maps kitten assembly axes to the chosen world/CCI frame. A proposed initial skin is
**0.02 m**, subject to native terrain/contact-margin acceptance. The result puts the bottom of
the capsule 2 cm outside the clicked surface. **Orbit position is the vehicle's centre of mass.**
The assembly origin, capsule centre and centre of mass are different locations; omitting the COM
correction produces the wrong spawn height. Obtain the actual `kitten.CenterOfMassAsmb` after its
native construction/derived-value initialization rather than assuming a fixed mass offset.

For arbitrary orientation, including gravity-upright placement, use the capsule support point.
All values in this pseudocode are in one common frame:

```text
a          = rotated unit capsule axis
offset     = rotated(colliderCentreAsmb - kittenComAsmb)
supportRel = offset - sign(dot(a, n)) * halfSegmentLength * a - radius * n
kittenCom  = hitPoint + skin * n - supportRel
```

For a perpendicular axis, either endpoint or the segment midpoint is a valid support choice.
This puts a support point against the surface plane. It still does not prove clearance from
curvature, edges, overhangs or surrounding objects.

## Collision validation is a separate step

Use a **real capsule overlap/contact query at the candidate pose**. Bepu's official
[CollisionQueryDemo](https://github.com/bepu/bepuphysics2/blob/master/Demos/Demos/CollisionQueryDemo.cs)
shows broad-phase AABB candidate collection followed by `CollisionBatcher<TCallbacks>` narrow-phase
contact generation. This query mechanism and the capsule/box pair path were checked against the
actual supplied Bepu DLL, rather than assuming upstream API compatibility.

Reject positive penetration, with an explicit numerical tolerance. A nonzero speculative contact
is not necessarily penetration. Preserve the intended skin; do not ignore the clicked vessel in
the overlap filter. Ignore only the provisional kitten itself if it has already entered a query's
object enumeration. Test other kittens and all nearby physical obstacles as well.

A downward/inward `Simulation.Sweep<Capsule, ...>` from a known clear pose can refine placement,
but does not replace the final overlap check. Zero-motion sweeps are particularly unsuitable as
a substitute: CURRENT's convenience overload derives tolerances from motion, and initial overlap
is separately reported through `ISweepHitHandler.OnHitAtZeroT`.

Run Bepu queries at the physics handoff, using nearby float coordinates in the **correct bubble
frame**, never absolute ECL coordinates converted directly to floats. `Vehicle.TryGetClutterStatics`
publicly exposes its bubble's `ConstraintSim` alongside clutter; that sim may be null. A bubble query
only covers geometry actually present there. Check region/terrain coverage and freshness; sleeping,
on-rails or distant targets must not turn “nothing queried” into “clear.” Direct queries against
nearby `ColliderModule.Handle` shapes can supplement missing vessel representation. Missing terrain
or static coverage must be resolved or reported as unavailable. Mesh backfaces and deep underground
starts also need explicit rejection; a triangle contact query alone is not a solid containment test.

For an obstructed click, reject with a red preview. Optionally search a small, bounded distance
outward along `n`, but preview that adjusted position. Do not teleport the kitten metres away from
the selected point to conceal a failed placement.

## Time, motion and safe creation

Retain the clicked point in the owning part's local frame, or in terrain CCF, until commit.
At the next `PhysicsFrameHook.Enqueue` handoff, validate the original scene/target identity and
recompute the point from current transforms. This prevents a moving vessel or floating origin
from invalidating a cached EGO point. Use shared stable vehicle/part resolvers for detached targets;
never substitute the controlled vessel or a name-ambiguous replacement.

- **Ground:** use `VehicleReferenceFrameEx.CcfStatesToCci(parent, time, positionCcf, Zero, ...)`.
  Zero body-fixed velocity becomes the correct inertial velocity of the rotating surface.
- **Vessel:** sample one coherent state via `GetPhysicsStates().GetStatesCci(...)` and its `Time`.
  For the kitten COM, use `vKitten = vVessel + cross(omegaCci, pKittenCom - pVesselCom)`.
  Convert host angular rates into the same inertial frame; if matching initial angular motion,
  convert them back into the new kitten's body axes. Copying host body-coordinate rates after
  changing orientation is incorrect. Articulating subparts may need additional point velocity;
  their motion must be covered in acceptance rather than assumed from rigid craft rates.
- Use the same parent, epoch, orientation and state for `Orbit.CreateFromStateCci`. In particular,
  do not combine yesterday's click pose with a later `Universe.GetElapsedTime()`.

The current absolute `SpawnRequest` path fixes orientation to identity, rates to zero and epoch
to the current universe clock. The reference path copies vehicle attitude and linearly chains
offsets. Add a surface-target mode or a dedicated internal surface request/pose solver; do not
route this through the existing absolute path without extending those contracts.

The commit must run **before any native Part/shape construction**, at the shared physics boundary.
The existing spawner's `WaitForVehicleSolverIdle()` is below `CreateBackpackPart()`; copying that
ordering is not a sufficient boundary for new physics queries/construction. The shared queue
also joins the nearest-orbit reader before queued mutations. Retain the existing native roster,
material budget, parent-child registration and failed-constructor rollback behavior.

Compute authoritative COM placement inside the creation transaction, before the next solver can
see the kitten. `Vehicle.Teleport` returns void and can log a failed trajectory computation without
throwing. Verify that the resulting orbit/pose matches the requested placement before declaring
success; otherwise use the existing provisional-object/roster rollback path.

## Input and hosting

`RenderFloatingWindows()` is the appropriate preview/UI hook. Unscience already invokes it with
its main window hidden. The standalone DOH host currently does not call it or install
`PhysicsFrameHook`; update its lifecycle/Patcher if that development host must support this tool.
Keep existing HotkeyGuard apply/unapply calls.

The Hot Pursuit ImGui-click pattern provides the UI behavior but does **not** consume native mouse
actions. CURRENT `Program.OnMouseButton` handles native vehicle, controller and orbit clicks during
event polling, before a later ImGui placement callback. Add scoped input ownership if the spawn
click must not also operate those tools. A narrow Harmony input gate is one option; match consumed
press/release pairs, respect existing native drag/viewport latches and allow ImGui to receive the
events. Do not blanket-disable all mouse input or assume `WantCaptureMouse` suppresses world clicks
while the cursor is over the viewport. This requires native input acceptance.

## Save ownership and cancellation

Reviewed [scope/saves.md](../scope/saves.md), [saves acceptance](saves-acceptance.md) and the production
[DOH adapter](../doh.lib/DohSubmod.Persistence.cs).

- Native KSA owns spawned vehicles, positions/orientations/velocities, names and roster/career.
  DOH v1 owns registry identities and existing spawn controls; War Paint owns active paint.
- The armed gesture, hover hit/preview and unexecuted click request are transient user input.
  Reset/dispose must cancel them; loading must never replay an uncommitted spawn or respawn a
  kitten already reconstructed by native saves.
- Give each request a DOH generation/cancellation token and check exact current object identity
  at execution. Increment/reset it on scene reset/unload. Do not globally clear the shared queue
  just to cancel DOH work, because other features own entries there.
- There is no new durable state if range/skin/orientation policy are fixed implementation choices.
  Any new reusable user settings must be captured/validated/restored through an additive compatible
  payload or explicit migration. Do not reclassify them as transient merely because they are UI fields.

## Implementation work and acceptance

Suggested files: `DohSubmod.Placement.cs` for interaction/cancellation; `Spawning/SurfacePicker.cs`
for typed hits; `Spawning/SurfacePlacement.cs` for capsule/COM/motion resolution; a small Bepu query
helper for collision checks. Extend the existing spawn transaction instead of duplicating it.

When implementing, update DOH project READMEs, root README, REPOSITORY_INDEX, `plans/saves-acceptance.md`
and the owning scope area/master/ToC in the same change. Catalog any new collider, terrain, physics
or input integration. This research note does not add a live integration or mark the feature shipped.

Meaningful managed checks should cover exact support-point/COM transforms under rotations and
scale; floor/slope/wall/ceiling/edge clearance; terrain-vs-part nearest selection; epoch and rotating
surface velocity; multiple spawned capsules; no native allocations on invalid preflight; failed pose
rollback; and pending-request cancellation across reset, vanilla loads, repeated loads and unload.
Extend real-adapter coverage if durable settings change. Keep existing DOH persistence/roster tests.

Native acceptance must include varied slopes and poles, curved/scaled vessel parts, interior walls,
low headroom, launchpads/clutter, neighbouring kittens, moving/rotating craft, pause/warp, UI and map
clicks, scene replacement between click/commit, and save/load of successfully placed kittens without
duplicates. Visually compare the preview to the actual physics capsule with KSA's collider debug view.

## Research validation and source trail

- Full solution build against CURRENT 5801: **PASS, 0 warnings/errors**, with `--no-restore -m:1
  -nodeReuse:false -p:UseSharedCompilation=false -p:NuGetAudit=false`; distribution redirected to
  `/private/tmp/unscience-doh-spawn-research-dist`. Existing unrelated worktree edits were retained.
- Existing production-linked DOH suite: **118 checks PASS**.
- A temporary probe compiled the cursor/part/collider queries, capsule template/transform fields,
  physics-state getters, bubble/sim access and CCF-to-CCI conversion against CURRENT KSA DLLs.
- The probe executed **six checks against CURRENT Bepu DLLs**: 0.575 m half-height, approximately
  −0.015 m authored bottom, a clear 2 cm floor gap, detected 2 cm floor penetration, detected adjacent
  wall penetration and detected low-ceiling penetration. This verifies primitive query feasibility;
  it is not a native-game placement/terrain/input acceptance result. Probe: `/private/tmp/doh-spawn-research-probe`.
- Build log: `/private/tmp/unscience-doh-spawn-research-build-direct.log`. No native game was run.

Primary current-game sources, relative to `../ksa-game-assemblies/current/decomp/KSA/`:

| Source | Verified contract |
|---|---|
| `Cursor.cs:17–29` | Viewport-local cursor and EGO ray |
| `Part.cs:2772–2876` | Art subpart coordinates/normals versus collider-ray API |
| `ColliderModule.cs:36–53,86–89` | Live collider position/orientation/scale and ray dispatch |
| `ColliderRaycast.cs:11–80` | Supported physical shapes and vehicle-assembly hit normal |
| `CapsuleColliderTemplate.cs:29–33` | Bepu radius/internal-length construction |
| `PoseIntegratorCallbacks.cs:15–17` | Kitten −Z up and +X forward |
| `Vehicle.cs:1486–1520,1555–1567,2460–2507` | COM transform, public sim access, teleport failure behavior |
| `ReadOnlyPhysicsStates.cs:7–16,65–68` | Coherent state epoch and CCI extraction |
| `VehicleReferenceFrameEx.cs:419–450` | Rotating-frame state conversions |
| `Program.cs:2237–2330,2408–2460` | Mouse latch/dispatch and input-to-physics ordering |
| `ConstraintSim.cs:46–90` | Public Simulation/BufferPool and shared shapes |

Bepu sources in the same CURRENT tree: `BepuPhysics/Simulation.cs:372–418`,
`BepuPhysics/ISweepHitHandler.cs`, `BepuPhysics.CollisionDetection/CollisionBatcher.cs:166–237`,
`BepuPhysics.CollisionDetection/BroadPhase.cs:478–497` and `BepuPhysics.Collidables/Capsule.cs:9–59`.
The official [Capsule API](https://docs.bepuphysics.com/api/BepuPhysics.Collidables.Capsule.html)
also confirms that length is the internal Y-axis segment, not total capsule height.
