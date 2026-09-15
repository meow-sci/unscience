# Dent Wizard

Fire an existing vessel — including any EVA kitten or debris fragment — from the main camera
at a clicked vessel or terrain surface. Included in the **Unscience** window (F11).
The standalone StarMap host is a development project; only Unscience is distributed.

## Use

1. Expand **Dent Wizard** and choose **Source** from the filterable dropdown. EVA kittens and
   debris are labelled. You do not have to control the source.
2. Set **Speed (m/s)**. Default: **5**. Drag between **0.1 and 100**; Ctrl-click to type any finite
   value **0.001 or higher**, including values above 100. Invalid input disables firing without
   silently replacing your value.
3. Position the main camera, press **Aim and fire**, then left-click a vessel or terrain within
   **10 km**. The source's centre of mass moves to the camera and receives the launch velocity.
4. A successful click fires once. A miss stays armed. **Esc**, **right-click**, or **Cancel**
   cancels. UI clicks and secondary viewports do not fire; collapsing the panel does not cancel.

## Velocity and physics

Speed is relative to the clicked target. For a vessel, Dent Wizard adds its inertial velocity
and the hit point's rigid-body rotational velocity to the camera-to-hit direction times speed.
For example, a target travelling at `(0, 7500, 0)` m/s and a perpendicular 10 m/s shot produce
`(10, 7500, 0)` m/s. Terrain shots include the body's surface rotation. The source may start
around a different body: KSA's teleport moves it into the target's orbital frame.

Launches run at the shared physics handoff after completed results are applied and before new
snapshots. Click geometry is retained relative to the target, so an intervening render frame
of orbital motion does not introduce aim drift. Source orientation and spin are preserved.
KSA's flight-plan rejection is reported instead of claiming a successful launch.

This is an initial ballistic launch. Gravity differences, drag, target acceleration/rotation,
engines, EVA controls, and other mods continue to act afterwards; long/slow shots are not guided
intercepts. Disable any source weld before firing, since the weld will reposition it again.
Collision damage and very fast impacts use the game's normal collision/failure behavior.

Picking uses vessel art meshes and an approximate sphere for EVA avatars, as Graffiti does.
Terrain uses the CPU heightfield; very narrow terrain features may evade its finite sampling.
Separate deployed parachute cloth, imported static props and clutter are not pick targets.

## Saves and validation

The launched vessel's physical state is native KSA save data. Selection, speed, armed gesture,
and queued shot are transient and reset on scene load. Loading never fires an old pending shot.
No persistent forces, custom assets, or collision modifications are added.

Build: `dotnet build ksa-mod-experiments.slnx`.
Managed checks: `dotnet run --project dent-wizard.tests`.
Native in-game acceptance remains required; see [integration scope](../scope/vehicle-physics.md#dent-wizard).
