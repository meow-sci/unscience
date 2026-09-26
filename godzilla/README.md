# Godzilla

Godzilla lives in the single Unscience package. Open **F11 → Godzilla**, choose a vessel
(or use the controlled vessel), set its size, and press **Apply**. The filter also finds EVA kittens.

Rendering always resizes. Two independent checkboxes control the other effects;
both default **on** and changing either converts all current Godzilla sessions at the next safe
physics handoff:

| Scale physics | Scale colliders | Result |
|---|---|---|
| Off | Off | Visual only; original physical properties and collision shapes |
| Off | On | Resize visuals and colliders; original mass, inertia, aero and nominal bubble bounds |
| On | Off | Resize visuals and physical properties; original collision size/layout |
| On | On | Original Godzilla behavior: resize visuals, physical properties and colliders |

**For collider-only resizing, turn Scale physics off and leave Scale colliders on.** Turning collider
scaling off keeps original colliders; it does not disable collisions. Contacts still apply real forces
and can move or damage the vessel. Keeping nominal bubble bounds small means contacts across
separate bubbles, or outside generated terrain collision patches, can be missed. Planet-sized
collision geometry can still overwhelm collision detection; these controls do not make it reliable.

Independent collider scaling requires authored `ColliderModule` shapes (including the stock EVA
kitten backpack capsule). Vessels using only KSA's fallback bounding-box collider reject mixed
settings with an explanatory status before mutations. Both-on/both-off remain supported.

- **Smart** uses a uniform multiplier (drag range 0.05–20; typed positive finite values may exceed
  it). Physical scaling multiplies authored full-part scales and spaces full parts about the captured
  center of mass; animated child transforms remain game-owned.
- With **Scale physics off**, visual XYZ multiplies the whole craft along assembly/body axes,
  including spacing. Collider centers follow that transform, but collider dimensions use KSA's
  largest-axis approximation; unequal axes are not exact collision fits.
- With **Scale physics on**, **Basic** sets raw XYZ scales on all parts/subparts without changing
  full-part spacing. Child inheritance can exaggerate dimensions. With collider scaling off, collider
  dimensions and centers use the original hierarchy while retaining animated child positions and
  rotations. Smart also retains live child scale animation.
- **Restore original**, each entry's **Restore**, and **Restore all** remove both scaling channels
  and recover captured part sizes/layout. Edits are relative to the first capture, never cumulative.
  Switching Basic→Smart removes Basic child overrides. Smart restore does not rewind child animation.
- Settings are runtime only; unload restores surviving vessels. Part or collider membership changes
  release independent collider overrides and restore surviving originals. Detached pieces retain their
  last shape size until their new owner refreshes/disposes them; they lose Godzilla's pose override.
- Garry's Torch and Godzilla cannot own the same source's scale concurrently. Unweld before scaling,
  or restore Godzilla before welding. A scaled vessel can still be a weld target.

Changes run through `PhysicsFrameHook.Enqueue` after worker results and before welding/new physics
snapshots. Physical edits refresh descendant transform caches, scale-aware modules, bounds, mass,
aero and attachment data. Collider-only edits use native `ColliderModule.SetScale` shape ownership
and a collision-compound rebuild, preserve nominal physical bounds, and flag the next worker to
refresh the broad phase. The readonly scale input is restored before other modules can consume it.
Kittens retain their original avatar scalar when Scale physics is off; contact forces still apply.

`godzilla.lib` implements `ISubmod` and exposes `RequestApply(Vehicle,bool,float3)`,
`RequestRestore(Vehicle)`, `ScalePhysics` / `SetScalePhysics(bool)` and
`ScaleColliders` / `SetScaleColliders(bool)`. The compatibility `SetVisualOnly(true)` turns both
channels off; false turns both on. `VisualOnly` reports that both are off. Hosts install the shared
handoff, `KittenScalePatches`, `VisualScalePatches` and `ColliderScalePatches`. The standalone host
is a compile-checked development reference; only Unscience is distributed.

Visual-only rendering covers the part-tree draw transform and EVA avatar/attachments. Separate
world-space effects such as exhaust and simulated parachute cloth retain physical placement.
Picking/camera targeting follow the physical channel, not the visible surface. Visual pixel culling
uses rendered size independently of the physical radius. KSA 5482 moved the vessel's pixel cull into
`Vehicle.IsLargeEnoughToRender(Camera)`. `VisualScalePatches` now redirects that single call in
`Vehicle.UpdateRenderData` to a mirror that uses the render radius. Without that change the old
radius transpiler matched no reads and all visual patches rolled back, which left visual-only and
collider-only modes unavailable.

Validation: full solution build, `dotnet run --project godzilla.tests` and Garry's Torch regressions.
Managed fixtures exercise production Harmony hooks, physical/visual/collider transitions, all four
channel combinations, XYZ/COM centers, animation, readonly inputs, nominal bounds, broad-phase
flags, exceptions, restoration and reload. Native terrain/vessel contacts, bubble boundaries,
planet-scale clipping/shadows/LOD and Iron Man equipment still need an in-game acceptance pass.
See [integration scope](../scope/vehicle-physics.md).

Normal KSA saves made with Unscience preserve active sizing and original geometry, so repeated
loads do not compound scale and Restore remains accurate. See [save details](../godzilla.lib/README.md#scene-saves).
