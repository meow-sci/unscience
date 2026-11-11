# DOH click-to-spawn — KSA 5801 integration

**Spawn at...** sits immediately right of **Spawn Kitten(s)**; refresh moves below. The one-shot
tool snapshots name/model/paint, previews a green/red physical capsule and creates one kitten on
a left world click. Esc/right-click cancels. Relative count/offset behavior remains unchanged.

## Game surface

| Integration | Use / contract |
|---|---|
| `Program.OnMouseButton(GlfwWindow,GlfwMouseButton,GlfwButtonAction,GlfwModifier)` prefix | Consume eligible placement presses and matching releases before vehicle/controller/orbit dispatch; retain release ownership after success/cancel |
| `Program.OnKey(GlfwWindow,GlfwKey,int,GlfwKeyAction,GlfwModifier)` prefix | Consume Escape while placement owns input |
| `Program.MainViewport/HoveredViewport/HasMouseInputLatch/DrawUI/EditorFlag/ConsoleWindow`, `IViewport.Mode` | Main flight viewport only; preserve ImGui, console and pre-existing native drag input; cancel on hidden HUD/map/editor |
| `Cursor.GetPosition(IViewport)`, `Camera.ScreenToEgoRay/EgoToScreen/GetPositionEgo` | Capture the clicked viewport pixel; query at the handoff; draw surface-relative capsule via ImGui foreground lines |
| `PhysicsFrameHook.Enqueue/BeforePhysics` | Query and commit after applied worker results, before next solver snapshots; join orbit readers; no UI-thread Bepu access |
| `VehicleProvider.GetAllVehicles(includeDebris:true)`, `Vehicle.Parts.Modules.Get<ColliderModule>()` | Exact current objects, including debris/other kittens, with no controlled-vehicle substitution |
| `ColliderModule.RayCastEgo/Handle/PositionVehicleAsmb/Collider2VehicleAsmb` | Physical surface point and vehicle-assembly normal; clearance uses actual shared shapes/transforms, including collider scaling |
| `Vehicle.GetPhysicsStates`, `VehicleProperties.BoundingBoxAsmb/MassToGeometryAsmb` | Native box fallback for vehicles with no authored colliders |
| `Vehicle.TryGetClutterStatics`, `ConstraintSim.Simulation/BufferPool/ReadOnlyShapes` | Unique active sims expose native static/terrain/launchpad/clutter geometry; borrow shapes, never mutate/retire them |
| `Simulation.RayCast`, `BroadPhase.GetOverlaps`, `Simulation.Statics` | Static ray/clearance candidates; transform through the anchor's matching bubble origin/kinematic state |
| `BubbleOrigin.GetBub2Cce`, `KinematicStates.PositionPhys` | Keep Bepu floats near the bubble origin; all world/parent conversions stay double precision |
| `Camera.NearbyCelestial`, `Celestial.MeanRadius/GetTerrainHeightFromDirCcf(accurate:true)/GetCcf2Cce/GetCce2Ccf` | Accurate CPU terrain crossing, slope normal and local 10 cm contact mesh; compare terrain distance with nearer part/static hits |
| `CapsuleColliderTemplate.GetShape/ComputeAuthoredLocationPartAsmb/Collider2Asmb`, `ModLibrary.AllParts` (existing reflection) | Preview from the native `KittenBackPackPart` template, not an assumed avatar height |
| `KittenEva.Parts.Modules`, `Vehicle.CenterOfMassAsmb` | Authoritative one-capsule dimensions/orientation/offset after construction; reject unsupported collider layouts |
| `ReadOnlyPhysicsStates.GetStatesCci/GetBodyRates/Time`, `IParentBody.GetCce2Cci/GetAngularVelocityCci` | Coherent epoch, inertial angular point velocity; transform rates into the new kitten's body axes |
| `VehicleReferenceFrameEx.CcfStatesToCci` | Rotating-ground inertial position/velocity rather than zero inertial velocity |
| `Orbit.CreateFromStateCci`, `Vehicle.Teleport`, `Orbit.StateVectors` | Actual COM placement, pose/velocity/epoch verification before successful registration |
| `SavedPartReference.Capture/Resolve`, exact scene/vehicle identity | Topology/ownership validation; no same-name replacement target |

No shader, GPU offset or custom render-pass integration is added. The preview draws ImGui lines
using public projection methods. Existing backpack/character/paint/roster dependencies remain.
Both Unscience's consolidated Harmony and the standalone host apply/remove the input hooks; the
standalone now also installs the existing physics handoff and dispatches floating windows.

## Geometry, transaction and ownership

Kitten body −Z is up and +X forward. The capsule support point against the hit normal is placed
2 cm outside the surface. Its offset from the actual native COM determines orbit position. No
collision disabling, rigid attachment or fixed visual-height correction is used. The initial pose
follows the outward surface normal; native gravity/locomotion take over after creation.

`CapsuleCollisionQuery` uses detached Bepu `CollisionBatcher` contact generation. Primitive,
compound and mesh contacts with more than 0.1 mm positive depth reject placement. Both windings
of local terrain triangles are tested; the entire capsule footprint is sampled, including nearby
cliffs. Active native terrain/statics are tested separately. These queries describe currently
available geometry; native terrain representation, statics loading and sub-grid procedural relief
require playtesting. Oversized terrain footprints (>64 cells/axis at 10 cm) are explicitly refused.

The existing creation transaction joins workers before any Part creation, performs budget/naming
preflight, constructs the native kitten, checks its real capsule and COM, verifies teleport and then
registers its roster/paint/DOH ownership. Constructor/placement failures remove only the provisional
object/row. Smite remains a native input event; this placement queue is not used for live deletion.

The unchanged `doh` v1/order60 record captures existing settings and exact tracked IDs; native KSA
captures completed pose/motion/crew, and War Paint captures paint. Gesture, preview, query buffers,
input callbacks and pending click are transient. Reset/unload invalidates the generation and clears
references/input ownership. Reconstructed kittens are rebound without respawning. No new setting,
sidecar field or migration is needed for the fixed 2 km range, 2 cm gap and orientation policy.

## Verification

The production-linked DOH suite covers capsule/COM/heading math, positive-depth Bepu floor/wall/
ceiling/slope/kitten/compound/mesh contacts, ray bracketing/occlusion bounds, real-adapter reset and
production Harmony mouse/key input ownership. Full solution build and distribution validation are
recorded in [implementation evidence](../plans/DOH_CLICK_TO_SPAWN_RESEARCH.md#implementation).
Managed checks do not replace native input/rendering/terrain/moving-surface/save-load acceptance.
