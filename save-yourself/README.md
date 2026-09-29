# Save Yourself

Timed auto saves through KSA's built-in save path. Available in Unscience's **Save Yourself** panel.
Only Unscience is shipped; this standalone project is a development host with an F11 window.

## Usage

1. Open **Save Yourself**. Optionally type a **Prefix** (letters, digits, `_` and `-` only; anything
   else is dropped as you type). Leave it empty to use `autosave_`.
2. Set **Interval (s)**: real seconds between saves, 5 to 300, default 30. Ctrl+click to type a value.
3. Tick **Enable auto save**. Every N seconds a new KSA save named `<prefix>_YYYYMMDDTHHMMSS`
   (local time) is written, e.g. `autosave_20260928T203000`. **Next name** previews it.
4. **Save Now** writes one save immediately with the same naming; the countdown is unaffected.

## Behaviour

- Saves go through the same code path as the GAME SAVES window's **New** entry and the terminal
  `save` command (`SaveName.TryAccept` then `UncompressedSave.Make`), so every native save hook,
  the save list refresh and the Unscience scene sidecar behave exactly as for a manual save.
- The countdown is wall-clock time from a `Stopwatch`, so time warp, pause and frame hitches do not
  change the cadence. It keeps running while the toolbox window is closed or the HUD is hidden.
- A prefix that already ends in `_` or `-` is used as-is; otherwise an underscore is inserted before
  the timestamp. The prefix is capped at 48 characters so the full name fits KSA's 64-character limit.
- While no world is loaded or the vehicle editor is open, KSA refuses saves. A due save waits
  silently (the status line says so) and fires as soon as saving is allowed again.
- A refused or failed write is reported in the panel and the game log, and the countdown restarts.
- Every save produces a new folder; nothing is pruned. Delete old auto saves from the GAME SAVES window.

## Scene saves

The version-1 `save-yourself` record stores the prefix, the enabled flag and the interval, so loading
an auto save resumes auto saving with the same settings. Scene teardown (any load, including vanilla
saves and saves without the record) turns auto save off and restarts the countdown when a record
re-enables it. The countdown position, last-save name and counters are transient. Invalid records
(bad prefix, interval outside 5–300, wrong types) are rejected and retained with a diagnostic.

## Implementation and checks

- `save-yourself.lib/AutoSaveSettings.cs`: settings DTO, prefix sanitising and interval clamping.
- `save-yourself.lib/AutoSaveNaming.cs`: `<prefix>_YYYYMMDDTHHMMSS` name builder.
- `save-yourself.lib/AutoSaveController.cs`: countdown, wait-for-game and save execution with
  injected game access, so it runs without KSA in the tests.
- `save-yourself.lib/SaveYourselfSubmod*.cs`: submod lifecycle, panel and `save-yourself` record.
- `ksa-abstractions.lib/GameSaveProvider.cs`: `CanSaveNow` and `TryMakeSave` around KSA's save entry point.

Build with `dotnet build`. Run `dotnet run --project save-yourself.tests` for the managed checks:
naming, sanitising, countdown/wait/failure behaviour and real-adapter save round-trips. Native
acceptance (a save appears in GAME SAVES every N seconds and loads with its sidecar) remains in-game.
See [game integration](../scope/saves.md#save-yourself-auto-saves).
