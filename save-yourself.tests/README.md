# Save Yourself managed checks

Run `dotnet run --project save-yourself.tests` from the repository root.

Links the production settings, naming, controller and save adapter into native-free fixtures; only
game access (`canSave`, `makeSave`, clock) is substituted. Checks: name building with default,
plain and separator-terminated prefixes and the 64-character fit; prefix sanitising, validation and
interval clamping; disabled idle, countdown restart on enable, exact-interval firing, waiting while
the game refuses and firing as soon as it allows, negative/NaN deltas, reconfiguration, refused and
throwing writes, manual saves and reset.

Save checks link the real adapter, JSON helpers and `SceneSaveCoordinator`: version-1 round-trip,
prepare/reset/restore gating, edit capture, A-B-A and repeated loads, record-less and vanilla loads,
partial records, invalid records retained with diagnostics, unsupported versions and recovery.

Not covered: KSA's own save writing, ImGui and native timing. Native acceptance: enable with a 5 s
interval, confirm a new `autosave_YYYYMMDDTHHMMSS` folder with `unscience.json` appears in GAME
SAVES every 5 s, open the vehicle editor and confirm saving pauses then resumes, load an auto save
and confirm auto saving continues with the saved settings, load a vanilla save and confirm it stops.
