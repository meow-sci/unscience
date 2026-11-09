# War Paint preset editor refactor

The War Paint panel now edits named presets, with a full-width filterable combo and Apply to
Kittens / Save Changes / New Preset / Delete actions. Width-aware layout tables wrap actions into
two or one columns when necessary. Preset Colors and Live War Paint are non-collapsible line
headers. Color controls stay disabled without selection; materials use compact swatches and
clipped names with tooltips. Dialogs have 20 px outer padding and equal-width Apply/Create/Delete
and Cancel buttons. Apply to Kittens is 660 px wide (viewport permitting), at least 400 px tall
(and taller with large fonts), and uses the remaining content height for its scrollable table.
Delete confirmation is 880 px wide. The materials viewport grows by at least 40% and accounts for
actual row padding to fit normal complete kitten layouts. Its swatch header is blank; red X
removal buttons have no tooltip. None in the preset combo clears selection and discards previews.

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

107 War Paint checks include the production editor, service, atomic library writes and scene adapter.
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
  exercise keyboard Enter/Escape. Create must enable while typing/pasting a valid name and disable
  immediately when blank or duplicate, without requiring Enter or losing focus. Confirm outer
  padding on all three dialogs and the enlarged picker at normal font size. Filter the combo live
  while typing, then use Enter to choose the first match. Select None to clear the editor; colors
  revert to their saved values, assignments remain, and material controls disable.
- [ ] Open a fresh preset immediately after native EVA/avatar creation. If only CatFur is ready,
  wait for body/attachment rows to appear without reselecting, also while paused. Edit fur before
  discovery completes; its draft color and dirty state must survive, with no duplicate rows.
- [ ] Confirm the taller material table fits the normal full set without scrolling, has a blank
  swatch header, and that red X removal buttons have no tooltip.
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

## Dialog input and sizing follow-up

Native screenshots showed missing popup edge padding and Create remaining disabled until Enter.
All dialogs now capture explicit WindowPadding before BeginPopupModal, as the existing Skittles
confirmation dialog does. The picker width grows from 440 to 660 px and its minimum height is
400 px (or eight frame rows at large fonts). The table fills the content height after reserving
buttons, empty-state text and wrapped errors, rather than shrinking to the kitten count.

The 5801 InputTextWithHint wrapper refreshes ImInputString.Length only when its native return is
true. EnterReturnsTrue therefore left ToString stale between keystrokes. Explicit EvaluateLength
after the input preserves Enter submission and enables immediate validation/filtering in both
the name dialog and preset combo. A scratch diagnostic against the actual 5801 ImInputString
reproduced stale length and verified refresh after typing, paste, backspace, empty/whitespace and
UTF-8 edits. The 107 managed War Paint checks pass, including delayed-catalog and deselection regressions. No durable state or save formats changed.
Full solution build passed with zero warnings/errors; native padding/sizing acceptance is pending.

Initial discovery also stopped after any row appeared, so CatFur could prevent later body material
resolution. Discovery now retries every 500 ms using wall time, merges missing roles into both the
draft and saved baseline, retries native bridge initialization and skips expensive name lookups
for known roles. Existing edits and
live bindings are untouched. Managed regressions reproduce a Fur-only initial result, then resolve
body materials, check preserved edits/baselines and verify no duplicates or GPU allocations. None
deselection has real-editor coverage for rollback and preservation of active assignments.
