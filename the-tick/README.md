# The Tick

Make chosen vessels indestructible by forces. Available in Unscience's **The Tick** panel.
Only Unscience is shipped; this standalone project is a development host with an F11 window.

## Usage

1. Open **The Tick** and pick a vessel from the filterable **Vessel** dropdown, then click
   **Make Indestructible**. Or click **Protect Controlled Vessel** for the craft you are flying.
2. Repeat for other vessels. The active table lists every protected vessel with its part count.
3. Click a row's **Delete** to make that vessel destructible again. This never deletes the vessel.

## What protection does

KSA destroys vehicles through two independent systems, and The Tick exempts the exact registered
vehicle instances from both while leaving physics, contacts and telemetry native
(see [the destruction investigation](../plans/VEHICLE_DESTRUCTION_INVESTIGATION.md)):

| KSA system | Native behaviour | With The Tick |
|---|---|---|
| Part contact failure (`PartFailure.Detect`) | Accumulated contact pressure is compared against each part's `Part.CrashTolerancePascals`, resolved from the shared `PartTemplate.CrashTolerance` XML attribute or a mass/volume fallback | Every part and sub-part owned by a protected vehicle reports `double.MaxValue` Pa, so no practical force trips it. Debris breakup and the fragment guard therefore never run. |
| Whole-vehicle structural failure (`PhysicsBubble.DetectStructuralFailure`) | Peak filtered G-load against the size-dependent 5–50 g limit, and peak dynamic pressure against 200 kPa, each queue a destruction event | The event the detector creates for a protected vehicle is discarded. G-load and dynamic-pressure telemetry, the flight-computer limit and part damage on other vehicles stay native. |

Why not edit the XML part definitions in memory? `Part.Template` is one shared `PartTemplate`
per part type, so raising its `CrashTolerance` would harden every vehicle built from that part.
The Tick overrides the *resolved* tolerance for parts whose owning vehicle is registered, which
is per-vessel and needs nothing restored on delete or unload.

Side effects, by design: protected vessels no longer dent (KSA scales dent depth by pressure
divided by tolerance, which becomes effectively zero), and **View > Debug > Show Part Contact
Load** reports the huge rated value. Debris shed by *other* vessels, EVA kittens' native limits
and vessels that merely share a name are not protected. Collisions still push and spin the vessel.

## Scene saves

A version-1 `the-tick` record stores the protected vehicle IDs. Reset clears registrations and
picker state before native reconstruction; replay resolves exact, unambiguous IDs through the
shared `VehicleProvider.FindVehicle` resolver before simulation resumes. Missing, disposed or
ambiguous vessels are reported and the record is retained; if the patches could not be installed,
restoration fails visibly instead of pretending. Vanilla saves and saves without the record restore
nothing. The picker selection and filter are transient. Patch state is process-global, not scene data.

## Implementation and checks

- `the-tick.lib/TheTickSubmod.cs`: submod lifecycle; `Update` prunes disposed/removed vessels.
- `the-tick.lib/TheTickSubmod.Ui.cs`: filtered picker, add buttons and active table.
- `the-tick.lib/TickProtection.cs`: concurrent registry keyed by vehicle reference identity.
- `the-tick.lib/TheTickPatches.cs`: Harmony prefix on the `Part.CrashTolerancePascals` getter
  (owner via `Part.Tree.OwningVehicle`, falling back to the full part's tree) and a prefix/postfix
  pair on `PhysicsBubble.DetectStructuralFailure(VehicleUpdateState)` that drops only the event
  the detector just created. Both `unscience/Patcher.cs` and the standalone `Patcher.cs` install them.
- `the-tick.lib/TheTickSubmod.Saves.cs`: `the-tick` target capture, validation, reset and replay.

Build with `dotnet build`. Run `dotnet run --project the-tick.tests` for the managed fixtures:
per-part and sub-part tolerance override, identity isolation, part-failure and fragment-guard
suppression, G/pressure threshold matrix with preserved telemetry, pre-existing event preservation,
removal/prune/unpatch and real-adapter save round-trips. Native collision, ImGui and load acceptance
remain in-game. Kitchen Sink's G-load Invincibility is independent and may be combined.
See [game integration](../scope/vehicle-physics.md#the-tick-per-vessel-indestructibility).
