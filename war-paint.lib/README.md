# War Paint library

War Paint owns kitten tinting and per-material color editing for Unscience. It works with every
live `KittenEva` in the current system, including native crew and DOH spawns. Each painted kitten
receives isolated GPU materials so changing it does not recolor another kitten or a shared asset.

## Using the panel

1. Open **War Paint** in the Unscience Toolbox and explicitly select an EVA kitten. The picker
   includes all live kittens and has a name filter.
2. Pick **Tint all materials**, then click **Apply to kitten**. The material table now exposes the
   kitten's body, head, fur, eyes, clothing and attachment materials available on that character.
3. Adjust the tint or individual colors. Edits update the selected painted kitten immediately.
   They remain usable without saving a preset. Material alpha affects the opaque model path;
   visor glass visibility remains in Humble Arteest.
4. Enter a preset name and click **Save preset** to reuse the current colors. Saving an existing
   name replaces it. Select a preset and click **Apply to kitten** to copy it to another target.
5. The **Active Kitten Paints** list shows every owned target. Select a row to edit it or click
   **Unset** to restore its original material bindings and release its private material slots.

Deleting a preset leaves paint already applied to kittens intact. DOH's spawn controls read the
same saved library; their preset selection applies a detached recipe to each new kitten.
Choosing a preset before selecting a target keeps that draft until you apply it. Presets map colors
to the same body or attachment material slot on compatible characters; each target keeps its own
textures. Missing roles or slots report an incompatibility instead of painting a different material.
The library is stored in `My Games/Kitten Space Agency/.unscience/war-paint-presets.json` under the
user's Documents directory, separately from scene sidecars.
Writes replace that file atomically, and callers receive detached recipe copies.
**Load kitten colors** discards an unapplied draft and resumes editing the selected kitten's
current paint. A failed live upload keeps the draft available for an explicit Apply retry.

## Ownership and scene saves

`WarPaintService.Shared` is the reusable owner behind `WarPaintSubmod`. Its API applies/unsets
paint, updates tint or exact material colors, returns detached snapshots, and enumerates active
targets. `Materials/` contains the extracted cloning, GPU upload, slot accounting and release
helpers formerly owned by DOH. The native backend retains original per-renderable bindings so
Unset restores the target without writing shared global materials.
Allocation preserves the game's 64-slot GPU material reserve. A failed complete allocation leaves
the original bindings intact; legacy migration reports targets that cannot fit in the private-set
budget through save diagnostics.

The `war-paint` version-1 scene participant captures active recipes by stable vehicle and character
identity. It resets old bindings before native replacement and restores only exact reconstructed
targets. Missing, ambiguous, incompatible or malformed records produce save diagnostics. UI target
selection, filter text and unapplied drafts are transient; reusable presets are global disk data,
separate from scene paint. DOH and Humble Arteest keep their earlier save fields readable and hand
legacy material recipes to War Paint; explicit new War Paint records replay after those migrations.

The development StarMap host is [war-paint](../war-paint/README.md). Only the Unscience host ships
and registers the scene-save participant.

## Validation

Managed checks cover core ownership and persistence. Compilation and managed fixtures do not prove
native GPU rendering. In-game acceptance still requires distinct native/DOH kitten colors, every
attachment material, Unset, repeated scene loads, native deletion/boarding, GPU pool recovery, and
shutdown before native renderer disposal.
