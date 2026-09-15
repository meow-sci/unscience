# repository maintenance

- `REPOSITORY_INDEX.md` - MUST be maintained with high level information about every csharp project and what it does to act as a discovery guide for ai coding agents
- a README.md file MUST be maintained in each csharp project folder with more detailed information about the mod, its features, and how to use it. Always refer to these README.md files for in-depth understanding of each mod's capabilities and implementation details.
- MUST when creating or modifying mods and mod.lib libraries, ensure that `REPOSITORY_INDEX.md` is updated and the repositories README.md is updated accordingly

# game integration scope (scope/)

`scope/` is the authoritative map of how every unscience feature integrates with the KSA game (Harmony patches, reflection, game types, shaders, assets), used to detect game-update breakage. See `AGENTS.md` → "scope/ maintenance" for the full rules.

- MUST update the relevant `scope/` file in the SAME change whenever you add/remove/modify any game integration point (Harmony patch, reflection lookup, game type/member reference, render-pass/shader/byte-offset dependency, game asset, or StarMap/ISubmod surface)
- MUST add new mods/features to the correct `scope/` area file, the master index `scope/game-integration-surface.md`, and the ToC + status summary in `scope/FULL_SCOPE.md`
- MUST start from `scope/FULL_SCOPE.md` before changing anything that touches the game, and follow its game-update workflow when a new KSA build lands
- MUST keep `scope/FULL_SCOPE.md` concise (entrypoint/ToC + high-level status); push depth into the adjacent area files

# scene save/restore maintenance (required for every feature)

Unscience game saves MUST account for every feature's state. A feature is not complete until
its save/restore behavior is implemented or its genuinely transient/native/global state is
explicitly accounted for. "Runtime state" does not mean "session-only."

- MUST review `scope/saves.md`, `plans/saves-acceptance.md`, and the relevant existing `*.Saves.cs` / `*.Persistence.cs` adapter whenever implementing, modifying, or removing a feature.
- MUST persist durable user-configured scene state by default, including per-vehicle registrations, toggles, relationships, owned objects, baselines, and reusable playback settings, through the existing `ISaveParticipantSource` / `ISaveParticipant` lifecycle and native-save `unscience.json` sidecar. Do not silently omit it or classify it as transient merely because it lives in memory.
- MUST explicitly distinguish sidecar-owned state from state already saved by KSA, global libraries/presets, and transient UI/solver state. Document intentional exclusions and their reason; honor explicit user requests for session-only behavior.
- MUST implement detached capture, validation, old-world cleanup/reset, and replay after native reconstruction at the existing safe lifecycle boundary. Clear stale references and pending work on vanilla saves, new scenes, repeated loads, and unload; restore through the feature's normal ownership APIs without duplicating objects or compounding transforms.
- MUST use the shared stable vehicle/part identity resolvers. Never serialize live game objects, raw pointers, GPU handles, or reflection caches; never substitute the controlled vehicle or a similar/name-ambiguous target. Report missing targets and dependencies through the save diagnostics.
- MUST preserve existing save IDs, versions, and payload compatibility. Add a separate record or an explicit migration when changing a payload; handle absent legacy records and unsupported/malformed data without silently losing earlier state.
- MUST add or update meaningful persistence checks when saved behavior changes: round-trip through the real adapter, reset/rebind, repeated or cross-scene load, legacy/missing records, and invalid or unresolved targets as applicable. Run the relevant checks and full `dotnet build`; distinguish managed validation from native in-game acceptance.
- MUST update the feature/project README, root README and `REPOSITORY_INDEX.md`, `plans/saves-acceptance.md`, and relevant `scope/` documents in the same change. Keep this section consistent in `AGENTS.md` and `CLAUDE.md`.

# existing funcionality discovery

Use `REPOSITORY_INDEX.md` as an initial place to discover existing mods and their functionality

Each csharp project folder contains a `README.md` with more detailed information about the mod, its features, and how to use it. Always refer to these README files for in-depth understanding of each mod's capabilities and implementation details.

# glossary

- `KSA` - Kitten Space Agency (a game)
- `mod` - modification (a user-created add-on for a game)

# technology

- KSA game mods are written in dotnet C# 10
- KSA game mods use ImGui for user interface
- KSA ImGui bindings are provided by a custom ImGui wrapper via Brutal.ImGuiApi.ImGui
- KSA game mods can optionally use HarmonyLib for runtime method patching
- KSA game mods use StarMap library to load into the game lifecycle with C# attributes

# code conventions

- use `Console.WriteLine` for logging

# hotkey guard (required for every mod)

Every top-level mod project MUST apply `HotkeyGuard` from `MeowSci.KsaAbstractions` in its `Patcher.cs`. This blocks game hotkeys while the player is typing in any ImGui text input.

- MUST add `using MeowSci.KsaAbstractions;` to `Patcher.cs`
- MUST call `HotkeyGuard.Patch(_harmony)` inside `Patch()` after the harmony instance is created
- MUST call `HotkeyGuard.Unpatch(_harmony)` inside `Unload()` before nulling the harmony instance (or inside the existing null-check block)
- The mod's `.csproj` must reference `ksa-abstractions.lib` either directly or transitively through its `.lib` project
- See `fixme-mod-name/Patcher.cs` for a canonical example of a minimal mod applying this pattern

# decompiled sources

KSA game decompiled sources for reference can be found in the `decomp/ksa` directory. These sources are decompiled from the game assemblies and may not be perfectly accurate, but they can be useful for understanding the game's internal workings and for mod development.

DO NOT attempt to load them all blindy, many are quite large.  Make strategic reads into the code base as needed to answer questions, or ask me to tell you which files are relevant for a particular task.

# mod dev instructions

- MUST compile solution with `dotnet build`
- MUST pass compilation before a task is complete
- MUST prefer good code hygiene and readability over cleverness
- MUST write code that is maintainable
- MUST attempt to keep files relatively small (target 300 lines max, but this is a soft limit and can be exceeded if it makes sense for the code).  prefer splitting code into multiple files if it helps keep file sizes down AND if it improves readability and maintainability
