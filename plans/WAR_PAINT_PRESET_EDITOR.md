# War Paint preset editor refactor

The War Paint panel now edits named presets, with a full-width filterable combo and Apply to
Kittens / Save Changes / New Preset / Delete actions. Width-aware layout tables wrap actions into
two or one columns when necessary. Preset Colors and Live War Paint are non-collapsible line
headers. Color controls stay disabled without selection; materials use compact swatches and
clipped names with tooltips. Dialogs have equal-width Apply/Create/Delete and Cancel buttons.

`PaintPresetEditor` separates selection, detached draft, saved baseline and dirty comparison from
ImGui. Draft edits preview immediately on every matching assignment. Save persists; selecting or
creating another preset rolls back unsaved preview colors. Apply accepts several exact live kitten
objects. Cancel has no side effects. Delete persists removal before unsetting all matching targets
and clearing the editor. Failed file writes preserve the working draft and existing assignments.
DOH passes its selected preset name into central ownership. Old API recipes import named presets.

The existing `war-paint` v1/order85 payload gains optional `PresetName`. Old records remain readable.
Scenes capture effective per-kitten preview colors and names, while global preset recipes remain
separate. Replay does not overwrite existing library recipes; missing entries recover from the
scene snapshot, so loading an older save may recover a deleted preset. Missing/invalid targets
retain their incoming records. UI state and pending dialog selections clear on scene replacement.
Material catalog discovery uses the existing original-binding reflection paths and does not paint
or allocate. No shader, GPU ABI, Harmony target or StarMap lifecycle changes.

## Managed validation

93 War Paint checks include the production editor, service, atomic library writes and scene adapter.
74 DOH checks cover compatibility, migration and teardown. All pass. Full solution compilation
passed against the provided CURRENT 5801 DLLs with zero warnings/errors; distribution validation
passed with one Unscience package and 32 referenced feature assemblies, with output under `/tmp`.
`git diff --check` passed. Native acceptance below is
pending on the game host; this macOS workspace cannot run KSA/Vulkan.

## In-game acceptance

- [ ] Compare the panel to the mockup at normal and reduced toolbox widths. There should be no
  horizontal scrollbar, overflowing inline labels or oversized color column. At narrow widths
  actions and the whole-material color row wrap. Try long kitten, preset and material names.
- [ ] With no preset selected, verify disabled color/material/filter controls and Apply/Delete/Save;
  New Preset remains available. Create a unique name, reject blank/duplicate names, cancel, and
  exercise keyboard Enter/Escape. Filter the combo and use Enter to choose the first match.
- [ ] Edit a swatch; Save Changes enables. Restore its saved color; Save disables. Change RGB and
  click Apply to All Materials; every material matches. Restart after Save to verify persistence.
- [ ] Apply one preset to several native and DOH EVA kittens. Unselected kittens remain unchanged.
  Verify Apply stays disabled with no checkmarks, Cancel does nothing, errors remain retryable,
  and each kitten appears once in Live War Paint with the correct preset name.
- [ ] Preview one material on all matching kittens. Save, preview again, then switch presets or
  create another; unsaved previews roll back to the last saved colors. Another preset is unaffected.
- [ ] Cancel deletion; nothing changes. Confirm it; all matching kittens return to their original
  bindings, the entry disappears, selection clears and controls disable. Red X removes only one
  kitten without deleting its preset or changing other kittens.
- [ ] Save/load unsaved live previews, twice and A-B-A; effective colors and preset names survive.
  Load an earlier nameless v1 record. Remove a global preset then load its scene snapshot; recover
  the named recipe. Vanilla/new scenes clear live rows, editor state and pending dialogs.
- [ ] Spawn with a DOH preset, edit/preview that preset in War Paint, and delete it. Confirm DOH
  kittens participate in preview and removal. Verify all fur/attachment materials, boarding/native
  deletion, GPU slot recovery and orderly unload remain correct.
