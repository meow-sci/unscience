# War Paint library

War Paint owns kitten material coloring for Unscience. Every painted EVA kitten, including native
crew and DOH spawns, has a named preset assignment and independent private GPU materials.

## Using the panel

1. Select a preset using the full-width filterable combo. Type to filter; Enter selects the first
   match. **New Preset** opens a padded name dialog and creates a white preset. Names must be unique;
   Create enables as you type a valid name, and Enter remains a submission shortcut. Choose **(None)**
   in the combo to clear selection and discard unsaved previews without removing kitten assignments.
2. **Preset Colors** is a line header. Its controls stay disabled until a preset is selected.
   Pick an RGB color, then press **Apply to All Materials** to replace the preset's colors.
   Filter the material table and click a swatch to edit just that material. Materials are discovered
   from live EVA kittens without painting or allocating GPU slots. Partial results retry every half
   second, including while paused. Discovery retries native bridge initialization and merges late
   body/attachment materials without replacing edits. Tint-only presets remain usable when no
   kitten is on EVA. Each target retains its own textures.
3. Edits immediately preview on every kitten assigned to that preset. **Save Changes** enables only
   when the recipe differs from its saved colors and commits the global preset. Selecting another
   preset or creating a new one discards unsaved previews and restores the previous saved colors.
   Changing the RGB brush alone does not change the preset until **Apply to All Materials** is used.
4. **Apply to Kittens** opens a checkbox/name table. Select one or more live kittens and use **Apply**;
   **Cancel** changes nothing. Apply uses the current draft, including unsaved colors. Failed targets
   stay selected with an error for retry; successful targets are removed from the selection.
5. **Live War Paint** lists kitten and preset names. The red **X** restores one kitten's original
   material bindings. **Delete** opens confirmation; confirming removes the global preset, unsets
   every kitten assigned to it, and clears selection. A failed disk deletion leaves assignments intact.

Actions fill the available width and wrap at smaller panel sizes. Material swatches occupy a compact
column with no header label; long names clip within their table cells and remain available in
tooltips. The material table is at least 40% taller than its original viewport and fits normal
kitten material sets including row padding; larger custom layouts can still scroll. Dialog action
buttons each use half the width, with 20 px outer padding. Apply to Kittens is 660 px wide
(viewport permitting) and at least 400 px tall, with a table filling the available height.
Delete confirmation is 880 px wide (viewport permitting). Red X buttons have no tooltip.
Sections do not collapse. Visor visibility remains in Humble Arteest.

Presets live in `My Games/Kitten Space Agency/.unscience/war-paint-presets.json` in Documents,
separately from scene saves. File replacement is atomic. DOH reads the same library and carries the
preset name into each spawned kitten's assignment. Its prepared spawn recipe stays detached; reselect
in DOH to refresh that recipe after changing the library.

## Ownership and scene saves

`WarPaintService.Shared` applies named recipes, previews all matching assignments, deletes presets
with their assignments, unsets paint and returns detached snapshots. Legacy API callers without a
name import/reuse a named preset rather than creating anonymous paint. `PaintPresetEditor` owns
selection, detached draft, saved baseline, dirty comparison and preview rollback separately from ImGui.
`DescribeMaterials` reads original binding roles; it never binds/clones/writes native materials.

`Materials/` owns cloning, GPU uploads, pool accounting and release. Original renderable bindings
are retained. Unset restores still-owned bindings before freeing slots. Allocation preserves KSA's
64-slot material reserve; insufficient budgets leave original bindings intact. Missing/incompatible
source+slot roles report an error instead of coloring a different material. Legacy exact-name
recipes remain accepted.

The unchanged `war-paint` v1/order85 scene participant adds an optional `PresetName` to each existing
vehicle/character/recipe record. Effective live colors, including unsaved previews, remain scene
state. Replay preserves those colors without overwriting an existing global preset. Missing global
entries are recovered from the scene snapshot; earlier nameless v1 and DOH/Humble recipes import
named presets. Consequently an older save can recover a preset deleted after that save was written.
Malformed/unresolved records warn and retain their original payload. Repeated reconstruction reuses
only exact surviving owned assets and clears stale bindings. Vanilla/new scenes remove assignments.

Selection, brush, filters, dialog selection and the editor's saved baseline are transient and reset
on scene replacement. The global library remains outside sidecars. Only the Unscience host registers
scene persistence; the [development host](../war-paint/README.md) supplies UI/global storage.

## Validation

`war-paint.tests` links the production service, editor, library and scene adapter. Checks cover
preview/save/discard/delete across multiple kittens, write failures, named/legacy scene replay and
native ownership fixtures. Managed checks and compilation do not prove native Vulkan rendering or
pixel layout. See the [acceptance checklist](../plans/WAR_PAINT_PRESET_EDITOR.md) for in-game checks.
