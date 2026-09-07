# Iron Man: enabling the native flight computer

Follow-up to the user's report that Iron Man editing/equipment works but the flight computer is
unavailable. Researched against KSA **2026.9.7.5402**, from the adjacent current assembly sources.

## Root cause

The first implementation switched the physics worker and keyboard paths into vessel mode but
left **two independent EVA HUD restrictions** in place:

1. [GaugeCanvas.IsContextVisible](../../../ksa-game-assemblies/current/decomp/KSA/GaugeCanvas.cs),
   lines 238–279, considers every CLR `KittenEva` to satisfy `EVA` and fail `Vehicle`. Stock
   [Gauges.xml](../../../ksa-game-assemblies/current/Content/Core/Gauges.xml) assigns `Vehicle` to
   `AutopilotSettings` (1326) and `EVA` to `KittenFlightControl` (995). Thus the native autopilot
   panel never appears for the enabled kitten.
2. [KittenEva.IsFlightComputerDisabled<T>](../../../ksa-game-assemblies/current/decomp/KSA/KittenEva.cs),
   lines 281–300, disables every action other than `KittenEvaAction`. Simply revealing the native
   panel would therefore still leave it inoperable.

The flight computer **exists and executes already**. `KittenEva.IsControllable` returns true
(line 63); [PhysicsBubble.cs](../../../ksa-game-assemblies/current/decomp/KSA/PhysicsBubble.cs),
lines 1208/1563, calls native `ComputeControl`. [FlightComputer.ReadUpdatedVehicleConfiguration](../../../ksa-game-assemblies/current/decomp/KSA/FlightComputer.cs),
lines 255–271, gathers attached gimbals and active RCS without requiring a command pod or a
kitten-specific computer module. No synthetic controllability, electricity or actuator is needed.

## Targeted fix

`IronManFlightComputerPatches` installs three validated Harmony transpilers:

- In `GaugeCanvas.IsContextVisible`, replace exactly two `isinst KittenEva` expressions with a
  helper that classifies **enabled Iron Man kittens** as vessels. Every other context condition,
  AND combination, empty list, null vehicle and user visibility setting remains stock. No canvas
  or saved settings are edited. EVA-only controls disappear during vessel mode and return on disable.
- In [GaugeButtonFlightComputer.IsDisabled and PackData](../../../ksa-game-assemblies/current/decomp/KSA/GaugeButtonFlightComputer.cs),
  lines 151–175, replace each single call to `Vehicle.IsFlightComputerDisabled<Enum>` with the
  same adapter. Enabled kittens use the original **base Vehicle policy**, including missing-target,
  engine, burn and control restrictions. Other vehicles retain normal virtual dispatch.

The base method is invoked by a generated **nonvirtual call**. This avoids recursion through
KittenEva's override and avoids patching generic JIT implementations shared across types.
The two callers already use `T=Enum`. Generic Harmony patch composition differs from the existing
nongeneric equipment-render adapter: a fixture experiment showed a separate Harmony postfix on
the closed generic base method was bypassed. Alternate delegate bindings failed. Consequently this
adapter promises the game's base policy, not composition with other mods patching that generic
method. No other current repository mod patches it.
Both GPU button disabled-state packing and click eligibility must be changed together.
The transpilers retain instruction labels/exception blocks and reject unexpected match counts;
installation rolls back instead of silently leaving half of the UI correction active.

No separate action executor is needed. `GaugeButtonFlightComputer.OnReleased` calls the patched
`IsDisabled` before queuing `FlightComputerInputData`. [InputEvents.cs](../../../ksa-game-assemblies/current/decomp/KSA/InputEvents.cs),
lines 582–625, applies that command through `Vehicle.ToggleEnum/SetEnum` and schedules the native
control-input reset. [Vehicle.cs](../../../ksa-game-assemblies/current/decomp/KSA/Vehicle.cs),
lines 6096–6200, already applies target/frame/roll/RCS/burn choices and updates the navball frame.
`KittenEva.IsSet<T>` delegates ordinary enums to base, so selected-button state needs no patch.

`IronManFlightSettings` now also captures/restores attitude frame, tracking/custom target, roll,
deadband and rate limit on disable/unload, alongside the existing four modes. It restores user
settings only: burn plans, completed burns, fuel, telemetry and live solver results are not rewound.
The panel shows native per-axis `ActiveControlSystem` assignments to distinguish an available
computer from a craft without steering authority.

## Why no additional physics patch

The existing worker `IsKitten=false` already excludes both interfering character behaviors in
[PhysicsBubble.cs](../../../ksa-game-assemblies/current/decomp/KSA/PhysicsBubble.cs):

- `FlightComputerInputsFor`, 1591–1598, would otherwise discard RCS commands outside MMU mode.
- `AdvanceKittenLocomotion`, 1738–1789, would otherwise write/reset camera-follow MMU attitude
  targets. Its first guard skips this entire path for enabled Iron Man workers.

The normal worker copies the vehicle flight computer, computes control and copies results back.
`KittenEva.UpdateFromTaskResultsUnsynchronized` only adds locomotion-state transfer after the
normal base application; it does not overwrite the flight computer.

## Player behavior and remaining limits

Enable Iron Man and use **HUD → Autopilot Settings** if that canvas was manually hidden. The
normal attitude targets, reference-frame holds, roll/RCS/profile and applicable burn controls
become available. Stock missing-target/burn/engine restrictions still apply.

The computer needs torque to steer: `FlightComputer.UpdateActiveControlSystems`, 446–505, selects
`Rcs`, `Tvc` or `None` per axis from actual propellant and torque authority. Gimbals require firing
engines; RCS requires working fueled thrusters with suitable placement. The panel displays these
native assignments without granting extra authority.

Control orientation remains stock. [Vehicle.Ctrl2Body](../../../ksa-game-assemblies/current/decomp/KSA/Vehicle.cs),
line 584, uses the selected control part/connector or identity. For a default kitten, +X is its
forward direction and -Z is body-up. An autopilot nose-pointing target does not automatically mean
"boots downward." This fix does not silently rotate controls or add a new control-frame feature.

## Validation

Managed tests link the production patch and snapshot classes. They exercise actual Harmony
installation, both button call sites, context combinations, default-off/per-kitten gating,
control switching, stock restrictions, deferred clicks, disabled/selected button bits, restoration,
unpatch/reapply and rejection of changed IL layouts. Existing flight/render and connector tests
remain required, along with full solution compilation.

Completed: full `ksa-mod-experiments.slnx` build against 5402 with zero warnings/errors;
`iron-man.tests` and `iron-man-flight.tests` both pass. Build deployment was directed to
`/private/tmp/iron-man-dist`.

Native acceptance still requires confirming panel appearance, click feedback, attitude/RCS
response and a valid maneuver/burn on an equipped kitten. The user's working-equipment report
establishes the earlier basic feature behavior, not these new flight-computer interactions.
