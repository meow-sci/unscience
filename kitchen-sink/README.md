# Kitchen Sink

A collection of small KSA fixes, available in Unscience's Kitchen Sink panel.
Only Unscience is shipped; this standalone project is a development host with an F11 window.

## G-load Invincibility

1. Open Kitchen Sink and find **G-load Invincibility**.
2. Open the **Vehicle** dropdown and type to filter vehicle names.
3. Select a vehicle and click **Add G-load Invincible**.
4. Repeat for other vehicles. The active table lists every protected vehicle.
5. Click a row's **Delete** button to remove its protection; this does not delete the vehicle.

Protection disables only KSA's whole-vehicle G-load destruction decision for those exact
vehicle instances, including loads during vehicle, ground and ocean contact. Bepu collisions,
measured G-load telemetry, flight-computer limits, individual part crash tolerances, and
atmospheric/ocean dynamic-pressure destruction continue normally. For collision-based machines,
set suitable part crash tolerances separately.

The list supports multiple vehicles and rejects duplicate entries. Removed or disposed vehicles
are pruned; an unrelated replacement with the same name does not inherit live protection.
Normal Unscience Save/Load saves the selected IDs and explicitly rebinds them to reconstructed
vehicles. Unload, vanilla saves and older saves without this record leave the list empty.
If the game patch cannot be installed, the panel reports protection as unavailable and disables
the add button; save restoration reports the failure and retains the saved record.

## Fix Invisible Subparts

Open the vehicle editor and click **Refresh Vehicle** to call
`PartTree.ReinitializeDerivedValues` on the editor's parts. This is a workaround for invisible
subparts after editing.

## Force IVA Rendering

Enable **Always Render IVA Interiors** to show interiors outside IVA camera mode.
`IvaForceRender` changes loaded model templates and catches newly created models with a
constructor postfix. Its `PartModel.AddInstance` postfix also keeps internal meshes visible in
the editor preview. Disabling the switch or unloading restores the changed template flags.
On KSA 5438, the shared helper targets the common dent-aware submission overload and keeps
the instance and dent lists aligned, including editor-only interior submissions.

## Scene saves

The existing `kitchen-sink` boolean record still stores the Force IVA Rendering switch.
A separate version-1 `kitchen-sink-g-load` record stores the protected vehicle IDs. Reset clears
old references and picker state before native reconstruction; replay resolves exact, unambiguous
IDs through VehicleProvider and restores protection before simulation resumes. Missing/disposed
or ambiguous vehicles are reported, and the original record is retained for recovery.
Older boolean-only saves remain compatible and restore with no G-load registrations. The picker
filter and one-shot editor refresh are transient. The defunct Flexo panels and solver hook are removed.

## Implementation and checks

- `kitchen-sink.lib/KitchenSinkLib.cs`: submod lifecycle and existing fix panels.
- `KitchenSinkSubmod.GLoadProtection.cs`: filtered picker and active table.
- `GLoadProtection.cs`: concurrent registry keyed by vehicle reference identity.
- `GLoadProtectionPatches.cs`: guarded Harmony transpiler on
  `PhysicsBubble.DetectStructuralFailure(VehicleUpdateState)`; gates only the
  `StructuralLoad.GLoadFraction` value consumed by the destruction comparison.
- Both `unscience/Patcher.cs` and the standalone `Patcher.cs` install/remove this patch.
- `KitchenSinkSubmod.Saves.cs`: backward-compatible IVA record and G-load target capture/reset/replay.

Build the solution with `dotnet build`. Run `dotnet run --project kitchen-sink.tests` for the
production patch's managed fixtures. These cover isolation, removal, telemetry, damage thresholds,
contact causes, cleanup and unpatching. Native ImGui/Bepu cart acceptance remains an in-game check.
The G-load detector and end-frame caller are unchanged from 5402 to 5438; protection and both
version-1 save records need no migration. See the [reconciliation record](../plans/KSA_5438_RECONCILIATION.md).
See [game integration](../scope/ui-customization.md#kitchen-sink) and
[destruction investigation](../plans/VEHICLE_DESTRUCTION_INVESTIGATION.md).
