# save-yourself.lib

Reusable core of [Save Yourself](../save-yourself/README.md): timed auto saves for KSA.

- `SaveYourselfSubmod` (`ISubmod`): panel lifecycle, wall-clock ticking, the prefix/enable/interval
  panel; loads settings on init and writes them through on change via `ApplySettings`.
- `AutoSaveSettingsStore`: global `.unscience/save-yourself.toml` preference file (`Load`/`Save`,
  defaults for missing/invalid data). Not scene data; nothing is written to game saves.
- `AutoSaveController`: countdown engine with injected `canSave`/`makeSave`/clock delegates.
  `Configure`, `Tick(elapsedRealSeconds)`, `SaveNow`, `Reset`; exposes `SecondsUntilNextSave`,
  `IsWaitingForGame`, `LastSaveName`, `LastError` and `SaveCount`.
- `AutoSaveSettings`: DTO with `SanitizePrefix` (alphanumeric, `_`, `-`; max 48 chars),
  `ClampInterval` (5–300 s, default 30) and `EffectivePrefix` (`autosave_` when empty).
- `AutoSaveNaming.BuildName(prefix, timestamp)`: `<prefix>_YYYYMMDDTHHMMSS`, no doubled separator.

Other mods can call `SaveYourselfSubmod.Instance?.Controller` to configure or trigger saves, or use
`GameSaveProvider.TryMakeSave` from `ksa-abstractions.lib` directly.
