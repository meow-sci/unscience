# parts-now

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

Standalone StarMap wrapper for the **parts-now** runtime Part / SubPart loader.

All logic lives in [`parts-now.lib`](../parts-now.lib/README.md) — this project only owns the
StarMap lifecycle and the floating window.

- **Hotkey:** `F10` toggles the standalone window. Configurable via `hotkey` in
  `<mods>/parts-now/parts-now.toml` — any `ImGuiKey` member name (`F10`, `F12`, `Home`, …), resolved
  once at startup with a logged fallback to `F10` if the name is not recognised.
- **Entry assembly:** `MeowSci.PartsNow` (see `mod.toml`).
- **Patches:** only the mandatory `HotkeyGuard`, applied in `Patcher.Patch()` and removed in
  `Patcher.Unload()`. parts-now has heavy text input, so blocking game hotkeys while an ImGui field
  has focus is not optional. It patches nothing else.
- **Lifecycle:** `[StarMapAllModsLoaded]` calls `PartsNowSubmod.Initialize()`, which is what reserves
  the shared mesh-buffer headroom — that hook fires *before* `ModLibrary.Bind()` allocates, and the
  whole feature depends on that ordering. `[StarMapBeforeGui]` drives the load state machine;
  `[StarMapAfterGui]` draws the window.
- Also bundled into the [unscience](../unscience) supermod as the `Parts Now` submod, which calls
  the same `Initialize` / `Update` / `RenderContent` entry points.

See [`parts-now.lib/README.md`](../parts-now.lib/README.md) for features, the two workflows, the mod
id rules, the mesh headroom setting, the validation rules, the reload safety gate and the known
limitations.
