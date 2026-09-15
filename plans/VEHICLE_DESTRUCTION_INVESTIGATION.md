# Why high CrashTolerance does not make a multi-vehicle machine indestructible

Investigated 2026-09-13 against KSA **2026.9.7.5402**, using
`../../ksa-game-assemblies_prev/current/decomp/KSA/`. The older `decomp/ksa` copy was
not used. This is a source investigation; no mod behavior was changed.

5438 reconciliation: historical source links now target PREVIOUS. The structural-failure detector
is unchanged in CURRENT; the [reconciliation record](KSA_5438_RECONCILIATION.md) covers the later
G-load protection implementation and its managed checks. Native cart acceptance remains open.

**Finding:** KSA has separate part-pressure and whole-vehicle structural-failure
systems. An arbitrarily high authored `CrashTolerance` protects against the first
system but does not raise the second system's G-load or dynamic-pressure limits.
The whole-vehicle G-load check is a strong explanation for an axle/cradle machine
breaking despite very strong individual parts. Confirmation for the particular
cart requires a destruction log entry or reproduction telemetry.

## The independent failure systems

| System | Measurement | Failure condition | Authored CrashTolerance applies? |
|---|---|---|---|
| Part contact failure | Accumulated contact impulse per area, converted to pressure | Pressure >= the contacted structural part's tolerance | Yes |
| Vehicle G-load failure | Peak of briefly filtered non-gravitational linear acceleration, in Earth g | Peak G >= a size-dependent limit | No |
| Vehicle fluid-load failure | Peak dynamic pressure, `0.5 * density * speed^2` | Pressure >= 200,000 Pa | No |

There is no material-strength or part-count contribution to the vehicle G limit.
The vehicle detector has no lookup of part templates or crash tolerances.

Sources: [PartFailure.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/PartFailure.cs)
47-83; [PhysicsBubble.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/PhysicsBubble.cs)
782-821; [VehicleStructuralLimits.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/VehicleStructuralLimits.cs).

## How an axle collision becomes a whole-vehicle destruction event

1. Bepu resolves contact between the axle vehicle and cradle vehicle.
2. `ConstraintSim.HandleManifoldForVehiclePair` sets
   `HadVehicleContactThisFrame` when a contact manifold has contacts. The pair is
   processed in both directions, so both vehicles receive their own contact flag.
3. `ConstraintSim.AccumulateKinematicMeasurements` measures each body's change
   in linear velocity and subtracts `dt * Disturbances[0].AccelPhys`. Those
   disturbances contain gravity and frame accelerations; contact response remains.
4. `PhysicsBubble.FullPhysicsConstrainedStep` divides that velocity change by the
   measurement interval, producing acceleration.
5. `IngestStepKinematicMeasurements` converts its magnitude to g, filters it,
   and retains the maximum filtered value for the frame.
6. `FullPhysicsEndFrame` calls `PartFailure.Detect`, then `DetectStructuralFailure`.
7. The latter can enqueue a `VehicleDestructionEvent` even if no part failed.
8. `ApplyRenderEventsToVehicles` applies the event. `Universe.DestroyVehicleFromEvent`
   logs the reason, attempts to shed debris, hands off cameras, and calls
   `DestroyVehicle(vehicle, CrewDisposition.Kill)`, which disposes the vehicle.

Each vehicle is checked independently. There is no combined six-vehicle machine
strength or shared acceleration calculation here. The code supports several
vehicles independently exceeding their limits in the same contact sequence;
it does not demonstrate that all of this cart's vehicles actually did so.

Sources: [ConstraintSim.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/ConstraintSim.cs)
885-897 and 934-963; [PhysicsStates.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/PhysicsStates.cs)
842-874; [PhysicsBubble.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/PhysicsBubble.cs)
702-753, 1433-1461, 2035-2040, 2184-2208;
[Universe.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/Universe.cs) 1695-1776.

## Actual G limits and smoothing

For ordinary vehicles, the limit is:

```text
G_limit = max(5, 50 * min(1, 5 / max(radius_metres, 0.001)))
```

`radius_metres` is the length of the vehicle assembly bounding box's half-extents
vector: half the box diagonal. It is not simply the axle's cylinder radius.
An exactly zero computed radius falls back to 1 metre.

| Assembly bounding radius | G limit | Filter time constant |
|---|---:|---:|
| 0.1 m | 50 g | 0.5 ms |
| 1 m | 50 g | 5 ms |
| 5 m | 50 g | 25 ms |
| 10 m | 25 g | 50 ms |
| 25 m | 10 g | 125 ms |
| 50 m or greater | 5 g | 250 ms or greater |

The filter is an exponential moving average:

```text
raw_G = length(acceleration_body) / 9.80665
tau = bounding_radius / 200
alpha = 1 - exp(-dt / tau)
filtered_G += (raw_G - filtered_G) * alpha
frame_peak_G = max(frame_peak_G, filtered_G)
```

`PrepareFromWorker` resets the frame peaks and vehicle-contact flag, but does not
reset `FilteredGLoad`. Repeated loading therefore feeds an ongoing filter.
Small components have especially short smoothing times. There is no requirement
to stay above the limit for a separate minimum duration.

Illustrative calculation, not a measurement from the cart: at a 1 m bounding
radius, a disturbance-corrected 5 m/s velocity change over a 5 ms measurement
interval is 101.97 g raw. Starting from zero filtered load gives 64.46 g after
filtering, exceeding the 50 g limit. The relevant interval is the KSA measurement
substep, not necessarily the display frame or a Bepu internal solver substep.

This check consumes linear acceleration magnitude. Although angular acceleration
is also measured, the detector does not compare angular acceleration or spin rate
against a separate threshold here. Rotation matters indirectly when contacts
produce translational impulses.

Sources: [VehicleStructuralLimits.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/VehicleStructuralLimits.cs)
7-25; [VehicleProperties.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/VehicleProperties.cs)
48-55; [VehicleUpdateState.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/VehicleUpdateState.cs)
326-344; [PhysicsBubble.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/PhysicsBubble.cs)
2184-2208.

## Strong parts can leave the whole-vehicle kill active

The vehicle detector contains this exception to G-load destruction:

```text
if G_limit_exceeded
   and (terrain_contact or ocean_contact or vehicle_contact_this_frame)
   and PartFailureEvent exists:
    suppress the G-load destruction condition
```

Because part failure detection runs first, a contact-induced part failure can
take precedence over the G-load destruction path. If every part survives due to
high tolerances, there is no `PartFailureEvent`, and this exception does not apply.
This does not make weak parts a solution: their failure still destroys parts,
and failure of the only part ultimately destroys its vehicle.

Debris vehicles also suppress the G-load condition, but not the dynamic-pressure
condition. Kittens get a 2.5 multiplier to the G limit. Neither is an ordinary
machine-vehicle immunity setting.

Source: [PhysicsBubble.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/PhysicsBubble.cs)
788-810; [PartFailure.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/PartFailure.cs)
474-535.

## Other causes and authoring checks

- **Dynamic pressure:** the 200 kPa check is independent of collision pressure.
  It uses body-relative fluid speed and the greater of atmospheric density and
  ocean density when submerged volume is positive. It is less plausible for a
  slowly moving cart in air, but remains a separate destruction path.
- **Positive authored tolerances are not capped:** `ResolveCrashTolerance`
  returns a positive authored value directly. Its fallback for NaN or nonpositive
  values derives strength from mass and bounding volume. Zero does not disable
  damage, and the units are Pa, not impact speed.
- **Check the actual template:** `Part.CrashTolerancePascals` reads
  `Template.CrashTolerance`. In this build, `PartTemplate.ApplyGameData` does not
  copy the `CrashTolerance` field from a `PartGameDataReference`. Put the value on
  the actual `<Part ... CrashTolerance="...">` asset, or update the actual runtime
  template. An attribute only on a separate `<PartGameData>` entry is not enough
  through that merge path. This is a secondary possibility, not an observation
  about the user's unidentified XML assets.
- **Collider ownership:** contact loads map through `Part.StructuralPart`, normally
  the full part owning a collider/subpart; internally attached parts can resolve
  upward to their host. Verify the rated tolerance on the structural owner.
- **Multiple failed parts:** the part system also has a fragment guard at
  `failedCount >= max(2, floor(partCount * 0.5))`. A genuinely one-full-part vehicle
  cannot trigger that guard with only one failed part. It can still disappear
  because its sole part failed, or because of the independent vessel detector.
- **The 30-vehicle constant:** `PartFailure` uses it to budget breakup fragments
  and evict eligible debris when making room. It is not a six-vehicle collision
  destruction threshold.

Sources: [PhysicsBubble.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/PhysicsBubble.cs)
2196-2204; [PartStructuralLimits.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/PartStructuralLimits.cs)
30-40, 123-136, 173-176; [Part.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/Part.cs)
837-854 and 1083-1123; [PartTemplate.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/PartTemplate.cs)
255-340; [BepuHandles.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/BepuHandles.cs)
100-119; [PartFailure.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/PartFailure.cs)
78-89 and 538-577.

## Confirming the exact cause

The native log distinguishes these paths. Example message formats below are
illustrative, not entries observed from the cart:

```text
Vehicle 'Axle' destroyed by Collision (64.5 g)
Vehicle 'Cart' destroyed by GroundImpact (64.5 g)
Vehicle 'Axle' destroyed by ExcessiveGForce (64.5 g)
Part 'AxlePart' destroyed - exceeded its crash tolerance of ...
Vehicle 'Cart' destroyed - N parts exceeded their crash tolerance at once
```

Terrain contact takes precedence when assigning the vessel event's cause label.
An axle/cradle interaction can therefore be reported as `GroundImpact` if the
affected vehicle also has terrain contact. The label is a classification from
situation flags, not identification of the particular contact that supplied the
largest impulse.

Use **View > Debug > Show Part Contact Load** to compare **Peak kPa** against
**Rated kPa** and confirm the actual authored tolerance reached the game. This
window measures the part system, not the independent G-load kill.

The local log checked was
`C:/Users/Alex/Documents/My Games/Kitten Space Agency/logs/KittenSpaceAgency.log`,
last written 2026-09-12 at 23:15:51. It reports the game was current at 5402 and
contains no matching destruction/crash-tolerance entry. No cart failure was
reproduced in this investigation.

For a diagnostic mod, capture at `DetectStructuralFailure` for each selected
vehicle: identity, bounding radius, `PeakGLoad`, `FilteredGLoad`, computed G limit,
`PeakDynamicPressure`, `Situation`, `HadVehicleContactThisFrame`, whether a
`PartFailureEvent` exists, and the resulting `DestructionEvent.Cause`.

Sources: [Universe.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/Universe.cs)
1695-1717; [PartFailureEvent.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/PartFailureEvent.cs)
21-58; [Program.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/Program.cs)
3534, 3716, 3747; [PartContactLoadDebug.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/PartContactLoadDebug.cs)
213-222; [Constants.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/Constants.cs) 270-272.

## Implications for a future machine mode

The targeted change would be to gate creation of structural destruction events
in `PhysicsBubble.DetectStructuralFailure` for explicitly selected machine
vehicles while keeping the load measurements and Bepu contacts. It could exempt
G-load destruction alone or both G-load and fluid-pressure destruction. Part
protection remains a separate choice: retain verified high tolerances or add a
selected-vehicle exemption to `PartFailure.Detect` for an explicit immunity mode.

Gating the event at detection avoids marking the vehicle as doomed during render
event processing. Globally blocking `Universe.DestroyVehicle` would be too broad:
it is also used for deletion/recovery and part-breakup cleanup, and the structural
event handler sheds debris before calling it. Changing only
`VehicleStructuralLimits.EffectiveMaxGLoad` would also affect the flight computer,
which calls the same helper, and would leave the fluid-pressure kill unchanged.

Physics stability is separate from destruction immunity. Ordinary vehicle pairs
use friction **0.95**, maximum penetration recovery velocity **1.5 m/s**, and
`SpringSettings(30, 1)`. High sliding friction may contribute to an axle binding
inside a cradle, and interference or large mass ratios are plausible sources of
large responses. These are hypotheses requiring the actual geometry and motion
to assess. A pair-specific bearing-friction adjustment could be considered
separately from damage protection so wheel/ground traction is retained.

Sources: [NarrowPhaseCallbacks.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/NarrowPhaseCallbacks.cs)
153-178; [FlightComputer.cs](../../ksa-game-assemblies_prev/current/decomp/KSA/FlightComputer.cs)
327-338; event lifecycle sources above.

Validation performed: traced the detector, measurement, event, disposal, template,
and contact-owner paths; inspected the local game log; recalculated the limit
table and illustrative filtered impulse. No C# or integration surface was changed,
so this documentation-only investigation did not require a mod build.
