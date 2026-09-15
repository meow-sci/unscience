# Axle escape from collider bearings

Investigated 2026-09-13 using the local KSA **2026.9.7.5402** decompilation and its bundled BepuPhysics/BepuUtilities assemblies. This is an assessment and an isolated physics experiment, not a game/mod change or an in-game reproduction.

5438 reconciliation: this remains a historical 5402 experiment. Source links now target the
PREVIOUS tree; the updated collision/segmented-physics behavior has not been reproduced here.

## Finding

The reported failure is physically possible in this simulation without breaking or destroying a part. The strongest explanation is a **secondary collision missed after an impact changes velocity during the solve**. Contact softness, limited contact selection, friction and rotation can worsen it.

The user reports:

- Separate vehicles for independently simulated machine elements.
- Analytic cylinder axle, **0.10 m diameter**.
- Bearing assembled from box colliders, **0.08 m thick**, with substantial overlap at corners.
- Escape occurs during fast rotation or impacts, at **1x time warp**.
- Very high crash tolerance; parts survive.

An isolated experiment with the game's Bepu binaries reproduced lateral escape through this wall thickness, using a stationary axle struck by a third dynamic body. No damage system, triangle meshes, corner gaps, axial motion, or pose teleports were involved. The experiment establishes a mechanism consistent with the symptoms; it does not identify the exact failed frame in the user's machine.

## Source map and verified settings

All KSA/Bepu links below refer to `C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp`. Build metadata is in [version.json](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/version.json).

| Setting/path | Verified behavior | Evidence |
|---|---|---|
| Vehicle bodies | One dynamic Bepu body per vehicle; vehicle mass and inertia supplied separately from the shape | [ConstraintSim.cs:209](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/KSA/ConstraintSim.cs:209) |
| Authored collision geometry | All authored vehicle colliders become children of one `BigCompound` | [Vehicle.cs:2043](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/KSA/Vehicle.cs:2043) |
| Solver | `new SolveDescription(8, 1)`: eight velocity iterations, one solver substep | [ConstraintSim.cs:193](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/KSA/ConstraintSim.cs:193) |
| Constrained timestep | KSA subdivides constrained physics into calls of at most `1/60` second; calls may be shorter | [PhysicsBubble.cs:1958](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/KSA/PhysicsBubble.cs:1958) |
| Detection mode | The shape-only body argument implicitly becomes `CollidableDescription(shape)`: **Passive**, minimum speculative margin zero, maximum `float.MaxValue` | [ConstraintSim.cs:222](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/KSA/ConstraintSim.cs:222), [CollidableDescription.cs:45](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/BepuPhysics.Collidables/CollidableDescription.cs:45) |
| Material | Ordinary vehicle pairs use friction **0.95**, maximum penetration-recovery velocity **1.5 m/s**, contact spring frequency **30 Hz**, damping ratio **1** | [NarrowPhaseCallbacks.cs:153](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/KSA/NarrowPhaseCallbacks.cs:153) |
| Contact budget | Compound/compound contacts are reduced to at most **four points for the entire body pair** | [DefaultTypes.cs:119](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/BepuPhysics/DefaultTypes.cs:119), [NonconvexReduction.cs:157](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/BepuPhysics.CollisionDetection/NonconvexReduction.cs:157) |
| Damage | Crash tolerance is compared against accumulated contact pressure to flag part failure; it does not configure collision response | [PartFailure.cs:44](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/KSA/PartFailure.cs:44) |

The use of `AddForKinematic` when assembling compound children does **not** make the vehicle kinematic. KSA builds the shape without deriving mass from those children, then supplies dynamic vehicle mass/inertia in `AddVehicle`.

## How an intact axle can escape

### Collision prediction comes before impulses

The default timestep executes bounding-box prediction, collision detection, then constraint solving and integration. It does not rerun full collision detection after each contact impulse. See [DefaultTimestepper.cs:17](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/BepuPhysics/DefaultTimestepper.cs:17).

Passive detection is more capable than checking for overlap only. It creates speculative contacts based on predicted motion, including angular expansion. Bepu's bounding-box prediction also calls the force integrator, so it is inaccurate to say that ordinary external acceleration is entirely ignored. See [PoseIntegrator.cs:68](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/BepuPhysics/PoseIntegrator.cs:68).

However, velocity delivered by another **contact constraint during the solve** is not necessarily represented in the earlier prediction. For an axle sitting inside a bearing with clearance:

1. The axle and housing start with little relative motion. A nearby wall may have no generated contact.
2. A moving wheel, lever, striker or other vehicle contacts the axle.
3. Solving that impact gives the axle a new lateral velocity or tilt.
4. The axle moves before the next full contact update.
5. It can penetrate deeply or pass through the retaining wall. Surviving the impact does not restore containment.

Bepu's own documentation describes speculative contacts, their limitations and velocity introduced during a timestep: [continuous collision detection](https://docs.bepuphysics.com/ContinuousCollisionDetection.html).

### Recovery can choose the outside

Contacts are spring constraints, not an infinitely rigid geometric barrier. The normal solver includes a softness term and computes a bounded penetration-correction velocity. The 1.5 m/s setting caps the **correction bias**, not incoming speed, total collision impulse, or the maximum collision speed that can be stopped. See [PenetrationLimit.cs:59](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/BepuPhysics.Constraints.Contact/PenetrationLimit.cs:59) and [SpringSettingsWide.cs:30](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/BepuPhysics.Constraints/SpringSettingsWide.cs:30).

If a cylinder penetrates past the middle of a box wall, separating it through the exterior can become the shorter geometric correction. There is no persistent rule saying that this axle belongs inside this bearing. This is why a wall that stops the axle from crossing completely in one step can still fail on subsequent steps.

At a 1/60 s update, lateral speeds of 3, 6 and 12 m/s correspond to 5, 10 and 20 cm of travel. The user's wall is 8 cm thick. These are distance comparisons, **not guaranteed tunneling thresholds**: speculative contacts can stop much faster known motion, while unexpected motion, tilt and contact geometry can fail sooner.

### Rotation adds complications, but the cylinder is a good choice

An ideal cylinder rotating about its own longitudinal axis occupies the same geometric volume. Its tangential surface speed must not be confused with radial movement through the bearing.

The risks are instead tilt, eccentricity, unbalanced mass, impact-transferred velocity and rotating noncircular/segmented collider geometry. At 600 RPM a rotor advances 60 degrees in 1/60 s; at 1,000 RPM it advances 100 degrees. A segmented ring can therefore present substantially different faces between contact updates even though the central cylinder remains round.

Friction 0.95 also means these contacts behave unlike a lubricated bearing. Friction can transfer substantial tangential impulses while a shaft is pressed against a wall. There is no per-collider friction or spring setting in the native `ColliderTemplate` schema: [ColliderTemplate.cs:7](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/KSA/ColliderTemplate.cs:7). Changing a rendered material or crash tolerance will not lower bearing friction.

### Extra colliders do not create unlimited constraints

KSA's `BigCompound`/`BigCompound` path uses `NonconvexReduction`. More than four candidate contacts are reduced by depth, position and normal distinctiveness. They are not simply the first four authored colliders. See [NonconvexReduction.cs:194](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/BepuPhysics.CollisionDetection/NonconvexReduction.cs:194).

Consequently, multiple sleeves, collars, inner-ring segments and other contacts between the same two vehicles compete for the same small contact set. Additional boxes can close a geometric gap, but do not each add an independently enforced barrier. The isolated escape below needed only one impact contact, so the four-point reduction is a separate possible aggravating factor, not a prerequisite for this failure.

## Isolated Bepu experiment

[Runnable source](C:/Users/Alex/.codex/visualizations/2026/09/13/01a09ccc-0167-7f51-9eab-803ab6a8543f/physics-probe/Program.cs), [project](C:/Users/Alex/.codex/visualizations/2026/09/13/01a09ccc-0167-7f51-9eab-803ab6a8543f/physics-probe/Probe.csproj), [all results](C:/Users/Alex/.codex/visualizations/2026/09/13/01a09ccc-0167-7f51-9eab-803ab6a8543f/physics-probe/results.csv).

The fixture uses actual bundled Bepu assemblies, the stock contact settings, and separate dynamic compound bodies. Assumptions beyond the supplied dimensions:

- Axle length 0.8 m, bearing length 0.3 m, **5 mm radial clearance**.
- Axle mass 10 kg; housing and striker 100 kg each.
- A sphere strikes the exposed axle outside the housing's axial extent.
- Rotation is locked by zero inverse rotational inertia to isolate lateral passage. Translation remains dynamic for every body, including the housing.
- No gravity, destruction, game callbacks, damping force, positional overrides, or collision suppression. No native KSA runtime is loaded.
- Run duration up to one second; stop once the entire cylinder cross-section passes the exterior wall. Recorded relative axial displacement remains zero.

| Scenario | Result |
|---|---|
| Axle initially moving sideways at 2–80 m/s, stock settings | Contained in all six sampled cases; speculative contacts catch the known motion |
| Stationary axle struck at 20 m/s, stock settings | Escapes in the first step |
| Same strike, 32 solver iterations | Escapes identically |
| Same strike, four internal solver substeps | Escapes; still no new wall contact in time |
| Same strike, full updates at 120 Hz | Escapes |
| Same strike, full updates at 240 or 480 Hz | Contained during the experiment, although with penetration |
| Same strike, wall thickness 0.12, 0.16 or 0.24 m | Escapes |
| Same strike, wall thickness 0.40 m | Contained during the experiment, with substantial initial penetration |
| Same strike, smaller/zero clearance, larger axle mass, or positive minimum speculative margin | None of the sampled variations reliably fixes this impact |

At stock settings, the [first-step trace](C:/Users/Alex/.codex/visualizations/2026/09/13/01a09ccc-0167-7f51-9eab-803ab6a8543f/physics-probe/trace-60hz.txt) records an axle displacement of **0.225437 m**, velocity **13.526242 m/s**, and only a striker/axle contact. The bearing does not move and has no axle contact before escape. Its outside face is at x = 0.135 m; the whole axle is outside when its center exceeds 0.185 m.

At 240 Hz the [trace](C:/Users/Alex/.codex/visualizations/2026/09/13/01a09ccc-0167-7f51-9eab-803ab6a8543f/physics-probe/trace-240hz.txt) shows a smaller first movement and generation of the bearing contacts on the next full update. Peak relative displacement is about 0.0565 m. At 40 m/s even 240 Hz fails in this fixture. No tested rate or thickness is a universal guarantee.

A sampled swept-CCD configuration was **not** validated as a remedy: at 10 m/s it still allowed escape, and at higher speeds some primary striker contacts were absent entirely. Those rows must not be counted as successful containment merely because the axle stayed still. Diagnosing that separate sweep behavior is outside this investigation.

The minimum-margin experiments are also a warning against assuming a single setting solves the problem: compound child overlap searches expand their local bounds from relative motion. The inspected path does not impose the body's minimum speculative margin as a minimum child-query padding. See [CompoundPairOverlapFinder.cs:77](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/BepuPhysics.CollisionDetection.CollisionTasks/CompoundPairOverlapFinder.cs:77).

## Practical collider and machine design changes

These are engineering recommendations inferred from the source and experiment; the user's specific machine has not been tested.

1. **Reduce abrupt loading and speed first.** Ramp the drive, avoid hard impacts/stops, and balance rotating masses about the axle. Reduce overhung loads and place supports near where wheel/gear forces enter the shaft. For imbalance, force rises with angular speed squared. This directly reduces the unexpected motion that defeated the experiment.
2. **Keep the analytic cylinder axle.** A many-box or faceted approximation adds changing contact features without making the shaft intrinsically stronger. The existing overlapping-box bearing is a sensible starting shape; the evidence does not suggest a triangle backface problem.
3. **Add wall thickness outward while retaining the intended bore.** Prefer one thicker box over coincident duplicate boxes or thin layered walls. Trial 0.16–0.24 m as an A/B improvement, but do not treat it as a fix: those thicknesses still failed in the severe fixture. Thickness supplies geometric room to recover, not a larger contact-force limit.
4. **Control tilt and retain axial position separately.** A longer sleeve or two aligned, separated supports can reduce lever-arm loading; collars/thrust faces can prevent legitimate axial withdrawal. Neither is a guarantee against radial tunneling. Avoid requiring many supports and collars to engage simultaneously between one body pair, given its four-point contact reduction.
5. **Use small positive running clearance and accurate alignment.** Avoid a press fit or intentional overlap between the axle and housing. Overlap between the housing's own corner boxes is fine because they belong to the same rigid body. Enlarging the gap can increase free movement and impact speed; shrinking it to zero did not cure the reproduced failure.
6. **Keep hollow rings simple and their contact profile smooth.** Overlap adjacent ring segments enough to prevent openings, but do not assume a large segment count adds retention strength. A single solid cylinder cannot substitute for a ring that must have a physical axle hole. Flexo currently authors the four analytic primitives; KSA also supports convex hulls, but a single convex hull cannot preserve a hollow bore.
7. **Review actual vehicle masses and inertia, not only collider dimensions.** A tiny light shaft loaded by a much heavier rotor can be hard to solve; long overhangs amplify rotational effects. Thickening a collider does not automatically fix the authored mass distribution. No mass-ratio cutoff is established here. Bepu's [stability guidance](https://docs.bepuphysics.com/StabilityTips.html) specifically discusses mass ratios, opposing constraints and contact complexity.

## If pursuing a physics mod afterward

The highest-value first prototype would run **full collision timesteps more frequently for machine-containing bubbles**, initially comparing 60, 120 and 240 Hz under the same physical loading. KSA's constrained-step loop currently caps each Bepu call at 1/60 s. Increasing render FPS or solver iterations is not equivalent to a fresh collision-detection pass.

Do **not** simply change `SolveDescription(8, 1)` to `SolveDescription(8, 4)`. There are two distinct issues:

- Solver substeps update existing contacts approximately and do not discover all new contacts. Bepu documents the distinction in [substepping](https://docs.bepuphysics.com/Substepping.html).
- KSA's [PoseIntegratorCallbacks.cs:71](C:/Users/Alex/repos/meow-sci/ksa-game-assemblies_prev/current/decomp/KSA/PoseIntegratorCallbacks.cs:71) computes normal force/gravity and torque increments with `Sim.SimStep.DeltaTime`, instead of the per-lane `dt` provided to the callback. With more solver substeps, constrained bodies can receive a full-step force increment repeatedly. This is a source-level compatibility concern for a future change; it is not an explanation for the current one-substep failure. Terrain damping and kitten special cases would also need an audit.

A second useful prototype would give bearing body pairs lower friction while keeping wheel/ground traction. This requires game code integration; the native part collider XML cannot select it. It may reduce binding and impulse transfer but cannot guarantee containment.

Swept CCD deserves controlled experiments with compound shapes, finite speculative margins and verified primary/secondary contacts; merely enabling its enum is insufficient. Raising recovery velocity or spring frequency blindly is also inappropriate: stronger/faster correction can destabilize contacts or accelerate ejection once the axle is on the wrong side.

For strict retention under severe loads, an explicit hinge/revolute constraint supplies the missing persistent relationship between bodies. It needs suitable solver settings too, but represents an actual joint instead of relying on collision geometry alone. This would be a separate implementation request, not a collider-authoring setting.

## Limits and next diagnostic evidence

No exported machine, precise clearance, masses, angular velocities, or in-game contact trace was supplied. The tested scenario intentionally isolates one cause. It does not recreate the user's entire machine, KSA's world-frame handoff, terrain, or force integrator.

If exact diagnosis is needed later, capture actual Bepu step duration; both bodies' pose and linear/angular velocities; contact count, depths and normals before reduction and after reduction; vehicle masses/inertia; and collider transforms around the failing frame. Verify fresh native parts are being simulated without an animation, weld or teleport imposing motion. Newly decoupled bodies also have separate avoidance-pair filtering, but there is no evidence that this explains a machine that already makes sustained contacts.

The investigation's runnable C# experiment compiled and completed successfully using `dotnet run --project Probe.csproj -c Release`. No production C# or mod behavior was changed; no native in-game acceptance is claimed.
