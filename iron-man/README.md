# Iron Man

Iron Man lets an **existing EVA kitten** use the stock vehicle editor to attach tanks, engines and
RCS, then fly with ordinary vessel physics. It is bundled in **Unscience**. This standalone project
is a development host and does not produce a separate release.

## Use

1. Control a live EVA kitten. Open **F11 → Iron Man**.
2. Under **flight mode**, use the full-width button pair: **eva mode** on the left and **iron man**
   on the right. The current mode is highlighted. Every kitten starts in EVA mode, including after
   loading a save. EVA uses native walking/swimming/ladder/MMU controls; Iron Man uses vessel physics,
   headward rocket controls, vessel HUD and geometric backpack RCS. Both modes retain equipment.
3. Choose **Edit this kitten** in either mode. Release any ladder before editing or entering Iron
   Man. The kitten appears upright, with matching vertical camera scrolling.
   The actual kitten stays visible in the stock editor, and its body
   cannot be deleted, grabbed, copied, replaced as root or turned into a new vehicle.
4. Two body nodes are added automatically: **Up** at `(0,0,-0.43)` pointing toward `-Z`, and **Down**
   at `(0,0,-0.10)` pointing toward `+Z`, each with radius `0.12 m`. Coordinates use the kitten
   part frame: feet at origin, `-Z` up, `+X` forward. These are ordinary external mating nodes
   placed within the body; the game's unrelated `Internal` connector flag is not used.
5. In **Body attachment nodes**, select a node and edit its position, outward direction and radius,
   then **Apply node changes**. **Add node** uses the entered values (up to 16). Hover a node row
   to highlight it. A connected node is locked: detach its equipment before moving/removing it.
   Remove all nodes to make **Restore default up/down pair** available again.
6. Use the stock part browser to snap compatible small tanks/engines/RCS onto the body or attached
   equipment. Nodes carry bulk propellant, service fluid and electrical connections. Configure
   tank propellants and resource groups as for any vessel; arrange thrust around the center of mass.
7. Choose **Return to flight**, completing the stock editor exit prompts. Editing preserves your
   selected flight mode. Choose **iron man** to fly the rockets; entry stops/disarms all engines and
   selects manual attitude/burn/direct thrust. The normal vessel **Autopilot Settings** panel
   replaces the EVA-only controls in Iron Man mode. If hidden,
   open **HUD → Autopilot Settings**. Use its attitude targets, reference-frame holds, roll/RCS,
   profiles and applicable burn controls. Missing-target, engine and burn restrictions remain stock.
   Iron Man also offers **Arm attached engines**, **Ignite**, quick attitude/RCS controls and
   **Shut down and disarm**; arming affects every engine module on this kitten. Normal vessel
   throttle/movement bindings apply. Iron Man supplies no free fuel or extra thrust.
8. Choose **eva mode** to return to native kitten behavior. This stops/disarms engines and restores
   the pre-entry EVA flight-computer settings, valid control part/port and View/Direct preference
   (subject to the game's restrictions). Editor access and equipment remain available. Subsequent
   Iron Man entries start manual again; arm/ignite explicitly. The buttons are unavailable while
   an operation is pending or the editor is open. Switching does not rotate or teleport the kitten.

## Saves and limits

Node definitions persist inside the existing part-instance save data. Runtime part IDs and shared
stock templates remain unchanged. Deep-copy/serialization restores the nodes before resolving
connection indices. Activation is never serialized.

In **iron man** mode, physics-debug **Teleport To...** and its named surface destinations place the
kitten head-up with feet/equipment clearance calculated from the full vehicle bounds. This applies
only to the specific kitten in Iron Man mode when the teleport is requested. Wait for a pending
mode switch to finish before teleporting. EVA-mode kittens, ordinary vessels and other teleport
paths retain stock behavior. Existing attitude commands may subsequently turn the kitten normally.
See [surface teleport details](../plans/iron-man/SURFACE_TELEPORT.md).

**Saves with connected Iron Man nodes require Unscience/Iron Man to load correctly.** Keep the mod
installed. Old saved files are not rewritten on unload. Normal unload converts supported live
attachments into ordinary surface links before removing nodes and hooks; if a conversion cannot
preserve resource flow, it retains passive hooks and logs the reason. This avoids corrupting later
saves, but does not make an already saved marked file loadable without the mod.

Edit an existing EVA kitten. World saves preserve that kitten's type; launching an EVA blueprint
from an empty stock editor still creates an ordinary vehicle. The mod preserves Character
metadata when saving a blueprint of a live authored kitten, but does not replace stock spawning.
The current game editor has no undo/redo interface; node changes mark it dirty for normal saves.

Equipment is rigidly attached to the body, **not animated foot bones**. Rocket boots can visually
separate from walking/posed feet. Stock EVA part picking remains specialized, so configure equipment
in the vehicle editor. The native vessel flight-computer gauges are enabled only for activated
kittens. Normal vessel collisions, structural failure and G-load rules apply while activated.
Test balanced, low-thrust setups first; this is not a tuned flight assist.
Only stock `KittenBackPackPart` roots are supported. Shared templates/assets are not modified.

The panel's **Attitude control: X/Y/Z** readout shows the native actuator assigned on each axis:
`Rcs` needs fueled thrusters, `Tvc` needs thrust from gimballed engines, and `None` means no actuator
is currently assigned. Autopilot cannot steer without torque. While enabled, the default rocket
nose points from the feet toward the head: **Up** points the head away from the surface with boots
below it. Navball, attitude controls and backpack RCS share that frame. An explicitly selected
control part or docking port keeps its chosen orientation. Disabling restores the EVA frame.
Existing attachment positions and save coordinates are unchanged. The editor stays upright in
both modes. See the [mode-switch design](../plans/iron-man/FLIGHT_MODES.md), the
[orientation correction](../plans/iron-man/ORIENTATION.md) and the
[flight-computer investigation](../plans/iron-man/FLIGHT_COMPUTER.md).

## Validation

Built against KSA **2026.9.7.5402**. Managed checks exercise real production connector persistence
and Harmony dispatch, native HUD eligibility/button state, and control-settings restoration with
small game fixtures. They do not run Vulkan/Bepu or simulate rocket flight. **In-game acceptance
is still required**, especially for the new autopilot panel and physical attitude response.
See [research](../plans/iron-man/RESEARCH.md),
[integration scope](../scope/iron-man.md) and [library details](../iron-man.lib/README.md).
