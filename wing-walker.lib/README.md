# Wing Walker library

Wing Walker adds **External Seat** and **Standing Mount** as regular surface-mounted
vehicle parts in the editor's **External Crew** category and **Capsules** filter. External Seat is a
worn rainbow webbed beach chair with a square gold-foil frame and textured white plastic armrests.
Standing Mount uses two simple stitched brown leather straps over the boots. The stand is
intended to display its occupant upright and both parts display helmets.
Assign a kitten through the part's native
Crew controls, or approach the entry point in flight (about one metre) and board; use the crew
portrait's EVA action to leave. The panel is informational and has no feature toggles.

Selecting or picking up an occupied mount in the editor unassigns its kitten through native crew
controls; starting a gizmo move does the same. Selecting a parent also clears external mounts
below it. Symmetry and part copying clear these assignments before capturing clone data, including
crew assigned after selection. Place the parts and set symmetry first, then assign each kitten.
Stock seats and unrelated mounts retain their occupants. Hovering and opening Crew controls do
not clear assignments. Native editor history records the change; KSA owns the saved result.

Each part has a native `IVASeat` and an aligned, invisible `EVADoor` helper. Its small habitat,
oxygen and water tanks, food and LiOH are finite native life-support supplies. There is no radiation
shielding. These are passenger parts with no Control module. Their food and scrubber supplies are
local and isolated by default at the surface mount. Refill or connect appropriate life support for
longer missions; a surface mount is not a habitat passage, and passage and fluid connectivity are
separate concerns.

The saved native part template determines posture. KSA owns the part, seat assignment, roster,
tanks and life-support state, so this feature adds no save sidecar. Render scope and diagnostic
flags are temporary. The two IDs are `Unscience_WingWalker_Seated` and
`Unscience_WingWalker_Standing`; the latter inherits the former and patches the seat position,
visual references, surface connector and collider. All five legacy subpart keys retain their
original order: the Chair slot holds the straps, Headrest/Runners/Base use empty templates, and
EvaDoor retains its native module. This preserves the positional layout used for saved modules.

`WingWalkerPatches.Editor.cs` watches actual selection/grab/gizmo transitions in
`VehicleEditor.OnMouseButton` and guards the private `PartCloner.Gather(Part,uint)` before module
save data is captured. It uses exact subtree references, active focused/unattached editor trees,
`Vehicle.SetSeatCrew(..., KeyHash.Zero, ...)` and native history APIs. No per-frame unassignment,
new saved flags or selection caches are introduced. `Brutal.Localization` is a compile-only
reference for the native history label's optional `LString` parameter.

Boarding scopes the exact aligned door and seat, so multiple copies of the same part do not resolve
to the wrong seat when native runtime IDs coincide. Native crew assignment, suit return, EVA
teardown and camera/control handoff still perform the actual boarding work.

`WingWalkerPatches` submits external-seat occupants for noncontrolled vehicles and editor focused
and unattached trees after `SuperMeshRenderSystem.ClearBuckets`; native code continues to render
the controlled vehicle and character portraits. The scoped `KittenRenderable.UpdateRenderData`
prefix enables the helmet and helmet-compatible ear
pose for both parts; native seat ownership keeps the MMU disabled. In the occupant's own IVA view,
`HideHead` temporarily hides helmet and visor meshes while preserving native head masking and War
Paint's native visor gate. A finalizer restores appearance and ear/condition state.

For a standing occupant with a ground idle, the same scope clears the seated clip throughout the
public render body and temporarily unfreezes animation. The first renderable/model/clip sample uses
`PlayAnimation` with zero blend; later samples use `SetAnimation` with zero blend. This avoids a
paused timed blend retaining the seated pose and suppresses seated idle actions even if the selector
is inlined. The finalizer restores the seated clip and freeze flag; weak initialization markers are
cleared on unload. Missing ground idle logs once and retains the native seated animation. A standing
origin correction uses KSA's 0.7552 m portrait eye height while retaining the seat position for IVA
cameras. Patches roll back on installation failure and are removed on unload.

The shared XML loads custom GLB atlases and diffuse/normal/ORM textures for both mounts. The
seated chair has separate models for webbing/plastic and frame, plus one coarse picking mesh.
Only the frame references `CoreSurfacesB_Material`, reusing the wrinkled foil region from
`CoreStructuralA_Prefab_StrutOcto2HB` / `CoreSurfacesB_Subpart_TrussFFoilA` at GLB UV
U=0.495–0.605, V=0.39–0.49. No Core texture payload is shipped. The original seated part ID,
five subpart IDs/keys/order, CrewSeat and EvaDoor remain; Headrest/Runners/Base are empty aliases.
The seat eye point and ground connector remain, with collider envelopes fitted to the new chair.
The strap tabs and boot soles align with the surface connector at Z=0.2901232; a small bounding
box replaces the chair collider. The inherited simulation mass and life support are retained.
Editable art and the Blender rebuild script live in [`wing-walker/Art`](../wing-walker/Art/README.md).
The chair's low-poly update uses 1,028 visible/96 picking triangles, four-sided frame beams,
simplified woven strips and three external
256px maps with cloth wear. Its coarse picking panels/envelopes replace the duplicated detailed
mesh; physics, native crew hooks, all part/model IDs and saved-state ownership remain unchanged.
`scripts/check-chair-assets.py` verifies exported budgets, foil UV bounds, flat frame normals,
native-visible streams, external maps, native-scale UV density and picking coverage during distribution validation.
Both the combined and standalone development host load the
same asset and install the same library patches. Use only one host at a time. **Unscience is the only
release**; the standalone host is for development. See the [integration inventory](../scope/wing-walker.md)
and [validation plan](../plans/WING_WALKER.md). Focused and full builds, packaging and the native
CPU pose probe and updated production fixture pass; the in-game visual retest remains pending.
