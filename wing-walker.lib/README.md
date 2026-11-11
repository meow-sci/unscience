# Wing Walker library

Wing Walker adds **External Seat** and **Standing Mount** as regular surface-mounted
vehicle parts in the editor's **External Crew** category. Both use the stock chair's
appearance; the stand displays its occupant upright. Assign a kitten through the part's native
Crew controls, or approach the entry point in flight (about one metre) and board; use the crew
portrait's EVA action to leave. The panel is informational and has no feature toggles.

Each part has a native `IVASeat` and an aligned, invisible `EVADoor` helper. Its small habitat,
oxygen and water tanks, food and LiOH are finite native life-support supplies. There is no radiation
shielding. These are passenger parts with no Control module. Their food and scrubber supplies are
local and isolated by default at the surface mount. Refill or connect appropriate life support for
longer missions; a surface mount is not a habitat passage, and passage and fluid connectivity are
separate concerns.

The saved native part template determines posture. KSA owns the part, seat assignment, roster,
tanks and life-support state, so this feature adds no save sidecar. Render scope and diagnostic
flags are temporary. The two IDs are `Unscience_WingWalker_Seated` and
`Unscience_WingWalker_Standing`; the latter inherits the former and patches the seat position.

Boarding scopes the exact aligned door and seat, so multiple copies of the same part do not resolve
to the wrong seat when native runtime IDs coincide. Native crew assignment, suit return, EVA
teardown and camera/control handoff still perform the actual boarding work.

`WingWalkerPatches` submits external-seat occupants for noncontrolled vehicles and editor focused
and unattached trees after `SuperMeshRenderSystem.ClearBuckets`; native code continues to render
the controlled vehicle and character portraits. During a standing seat's animation update only,
it temporarily clears the seated idle so native ground-idle fallback is used, restoring the field
in a finalizer. If ground idle is unavailable it logs once and retains the seated animation. A
standing model-origin correction uses KSA's 0.7552 m portrait eye height while retaining the seat
position for IVA cameras. The patch set rolls back on install failure and is removed on unload.

The shared XML creates new external model definitions referencing stock chair meshes and material;
it does not change the stock IVA props. Both the combined and standalone development host load the
same asset and install the same library patches. Use only one host at a time. **Unscience is the only
release**; the standalone host is for development. See the [integration inventory](../scope/wing-walker.md)
and [validation plan](../plans/WING_WALKER.md). Managed and native XML checks pass; in-game
graphics, physics and boarding acceptance remain pending.
