# Iron Man

Iron Man lets an **existing EVA kitten** use the stock vehicle editor to attach tanks, engines and
RCS, then fly with ordinary vessel physics. It is bundled in **Unscience**. This standalone project
is a development host and does not produce a separate release.

## Use

1. Control a live EVA kitten. Open **F11 → Iron Man**.
2. Explicitly check **Enable Iron Man for this kitten**. Every kitten starts disabled, including
   after loading a save. Release any ladder first. Activation stops engines and selects manual
   vessel control; walking/swimming/ladder movement is replaced while enabled.
3. Choose **Edit this kitten**. The actual kitten stays visible in the stock editor, and its body
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
7. Choose **Return to flight**, completing the stock editor exit prompts. Use **Arm attached
   engines**, **Ignite**, normal vessel throttle/movement bindings and **RCS enabled**. The panel
   also offers manual attitude, rotation-rate hold and **Shut down and disarm**. Arming affects
   every engine module on this kitten. Iron Man supplies no free fuel or extra thrust.
8. Return to flight before clearing the enable checkbox. Disabling stops/disarms engines and
   restores the previous flight-computer modes and stock EVA movement. Equipment and nodes stay
   attached and visible; remove equipment in the editor when you no longer want its mass.

## Saves and limits

Node definitions persist inside the existing part-instance save data. Runtime part IDs and shared
stock templates remain unchanged. Deep-copy/serialization restores the nodes before resolving
connection indices. Activation is never serialized.

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
separate from walking/posed feet. Stock EVA HUD/picking remains specialized, so use this panel and
the vehicle editor for equipment. Normal vessel collisions, structural failure and G-load rules
apply while activated. Test balanced, low-thrust setups first; this is not a tuned flight assist.
Only stock `KittenBackPackPart` roots are supported. Shared templates/assets are not modified.

## Validation

Built against KSA **2026.9.7.5402**. Managed checks exercise real production connector persistence
and Harmony dispatch with small game fixtures. They do not run Vulkan/Bepu or simulate rocket
flight. **In-game acceptance is still required** for avatar alignment, snapping, engine fuel use,
flight handling and save reload. See [research](../plans/iron-man/RESEARCH.md),
[integration scope](../scope/iron-man.md) and [library details](../iron-man.lib/README.md).
