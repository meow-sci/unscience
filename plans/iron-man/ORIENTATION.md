# Iron Man: upright editor and rocket control frame

KSA baseline: **2026.9.7.5402**. The user observed a sideways editor kitten and a flight-computer
Up target displaced by 90 degrees. This corrects the limitation recorded in the earlier
[flight-computer investigation](FLIGHT_COMPUTER.md).

## Flight frame

[Vehicle.Ctrl2Body](../../../ksa-game-assemblies/current/decomp/KSA/Vehicle.cs) (584) defaults to
identity. Rockets point along assembly +X, but the kitten's headward axis is -Z and its face is +X.
`IronManControlFramePatches` postprocesses that getter for enabled kittens without an explicit
control part/port. `RotY(+pi/2)` maps control X to body -Z, Y to Y, and Z to X. The basis is
right-handed; body transforms, orbit, physical pose, part coordinates and saves are untouched.
Selecting a control part/port still uses its native frame. Disable restores the ordinary EVA frame.

The shared getter feeds `Vehicle.CtrlRates` (602), `GetCtrl2Cce/GetCtrl2Cci` (3100–3107), all three
worker navigation construction paths in [PhysicsBubble](../../../ksa-game-assemblies/current/decomp/KSA/PhysicsBubble.cs)
(1078/1206/1562), and module updates in `VehicleUpdateState.UpdateModules` (379). The flight computer
therefore points the headward rocket nose toward its target; Up means head away from the surface.
This enables appropriate commands; it does not add steering torque.

The [flight-mode selector](FLIGHT_MODES.md) subsequently separates editor authorization from active
rocket mode: the editor stays upright in either mode, while flight-frame/RCS changes apply only in
Iron Man mode. The orientation implementation below is otherwise unchanged.

## Backpack RCS

The stock backpack has explicit `ManualControlMap` assignments authored for the original EVA
frame (`Content/Core/PartGameData.xml`). [ThrusterController.RecomputeDynamicData](../../../ksa-game-assemblies/current/decomp/KSA/ThrusterController.cs)
(90–154) otherwise keeps those assignments even when its frame changes. A guarded transpiler
replaces exactly one field read: enabled kittens' actual root-backpack controllers return null,
selecting native geometry-based mapping. Authored fields and attached equipment maps stay intact.
Ownership follows `Parent.FullPart.Tree.OwningVehicle` and the exact live root reference.

`ThrusterControllerGlobalState.IsCacheValid` (53) detects changed control quaternions. Enable,
disable and disposal additionally reset native thruster authority at the joined physics handoff,
covering membership changes with an unchanged explicitly selected frame. Activation membership is
published as immutable arrays so worker frame/map reads do not race mutable dictionary pruning.

## Editor view

[OrbitController.GetFrame2Ecl](../../../ksa-game-assemblies/current/decomp/KSA/OrbitController.cs)
(233–293) maps camera-frame +Z onto assembly +X for rockets. Changing `EditingSpace.Asmb2Ecl`
alone would rotate both the camera frame and the scene, leaving the same mismatch. Rotating only
the avatar would instead misalign equipment and picking.

`IronManEditorOrientationPatches` changes only the enabled kitten editor's camera basis to
`Concatenate(RotX(pi), EditingSpace.Asmb2Ecl)`, giving the camera headward up (-Z in assembly).
The entire assembly appears upright together. It also replaces exactly two +X pan-axis reads and
two `CameraOffset.X` bounds in private `EditorOnScroll` (433–485), using headward pan and projection
onto that same world-space axis. Zoom behavior stays native. Ordinary editors are unchanged.

## Validation

Managed tests exercise the production Harmony patches and actual Brutal quaternion/matrix math:
rocket axes, explicit controls, per-kitten gating, disable/unload, whole-scene camera orientation,
scroll direction/bounds, authored-versus-geometric RCS maps, cache reset and changed-IL guards.
Full solution compilation is required. Native acceptance still needs upright editor/picking,
Up attitude tracking, navball/manual axes and backpack RCS checks on an equipped kitten.

Completed: full `ksa-mod-experiments.slnx` build against 5402 (zero warnings/errors), connector
suite, and Debug/Release flight-orientation suites. Release also exercises warmed getter calls
without forcing the fixture getter not to inline. Pan/map IL checks preserve labels and exception
blocks and reject changed layouts. Build deployment was directed to `/private/tmp/iron-man-dist`.
