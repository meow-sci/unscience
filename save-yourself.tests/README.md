# Save Yourself managed checks

Run `dotnet run --project save-yourself.tests` from the repository root.

Links the production settings, naming, controller and save adapter into native-free fixtures; only
game access (`canSave`, `makeSave`, clock) is substituted. Checks: name building with default,
plain and separator-terminated prefixes and the 64-character fit; prefix sanitising, validation and
interval clamping; disabled idle, countdown restart on enable, exact-interval firing, waiting while
the game refuses and firing as soon as it allows, negative/NaN deltas, reconfiguration, refused and
throwing writes, manual saves and reset.

Settings-file checks exercise the real `AutoSaveSettingsStore` against a temporary folder: missing
file, TOML round-trip, normalisation on save, absent keys, wrong types, out-of-range and huge values,
missing section, malformed TOML and an unwritable location. Only the shared path helper is stubbed.

Not covered: KSA's own save writing, ImGui and native timing. Native acceptance: enable with a 5 s
interval, confirm a new `autosave_YYYYMMDDTHHMMSS` folder with `unscience.json` appears in GAME
SAVES every 5 s, open the vehicle editor and confirm saving pauses then resumes, load any save and
confirm auto saving continues unchanged, then restart the game and confirm the settings were kept.
