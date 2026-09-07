# Iron Man: upright surface teleport placement

Baseline **KSA 2026.9.7.5402**. The user requested the previously researched option 2, explicitly
isolated to kittens currently in Iron Man mode.

## Native path and scope

Physics debug map Apply and named surface destinations call
[Vehicle.TeleportToLocation](../../../ksa-game-assemblies/current/decomp/KSA/Vehicle.cs) (4170–4195).
It supplies mass-centered bounding-box limits to `GetInitialKinematicStateForLocation` (4086–4144),
which assumes body +X points skyward and calculates terrain clearance under its minimum-X face.
There is no kitten branch and no `Ctrl2Body` read. A kitten's chest is +X; headward is -Z.

`IronManTeleportPatches` installs one guarded transpiler on `TeleportToLocation`, replacing exactly
one call to that static placement helper. An appended receiver argument identifies the requested
vehicle. Only a non-disposed `KittenEva` with `IronManSubmod.IsEnabled == true` gets corrected.
Configured EVA kittens and ordinary vessels call the original helper with the original arguments.
Other callers of the helper (including ordinary launch placement) and all general `Vehicle.Teleport`
paths are untouched. The native teleport event construction, queue, burn-plan clearing and planner
reset remain intact.

## Frame and clearance

With `Q = IronManOrientation.RocketCtrl2Body`, express native body bounds in a proxy rocket frame:

```text
proxyMin    = (-max.Z, min.Y, min.X)
proxyMax    = (-min.Z, max.Y, max.X)
proxyCenter = (-center.Z, center.Y, center.X)
```

This exact signed permutation is the inverse of Q, keeping an exact axis-aligned bounding box.
Pass these to the native helper with unchanged celestial, time, latitude, longitude and orbit color.
It retains terrain sampling, launchpad elevation, planetary rotation and surface velocity, now using
the feet/equipment footprint and height. Convert its returned proxy-frame state:

```text
Body2Cce = Concatenate(Inverse(Q), native.Body2Cce)
BodyRates = Transform(native.BodyRates, Q)
Orbit = native.Orbit
```

Physical headward -Z therefore points away from the surface; world-space angular velocity is
preserved. Explicit control-part orientation does not affect this physical placement. Part geometry,
connector coordinates, current flight-computer commands and saved settings are not rewritten.

## Queue timing and validation

Eligibility is decided when the teleport request is generated. `Program.PrepareFrame` applies
native input events at 2115, before the mod's queued mode changes at its GetJobSimStep handoff
(2143). A pending mode change does not retroactively rewrite an existing teleport request; wait
for the selected mode to become active before using Teleport To.

Managed tests link the production Harmony patch, exercise asymmetric bounds/center and every
corner, frame and angular-rate conversion, unchanged native arguments/orbit, queued application,
request-time mode isolation, non-target paths, unpatch/reapply and changed-IL rejection. The inserted
receiver inherits branch labels/open exception boundaries; closing boundaries stay after the call.
Native acceptance still needs terrain/launchpad clearance with an equipped kitten. Subsequent
autopilot motion, balance and native four-corner terrain sampling remain game behavior.

Verified 2026-09-07: full solution `dotnet build ksa-mod-experiments.slnx --no-restore
--disable-build-servers -m:1 -p:UNSCIENCE_DIST_DIR=/private/tmp/iron-man-dist -v minimal`
passed with **0 warnings and 0 errors** against 5402. All three managed suites passed:
`iron-man-flight.tests`, `iron-man-mode.tests`, and `iron-man.tests`. Build output was staged
under `/private/tmp/iron-man-dist`; this verification did not deploy or run the native game.
