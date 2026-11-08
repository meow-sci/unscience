# War Paint

> Distribution: only the `unscience` mod is shipped. This project is a compile-checked development
> host; its output stays in `bin/`. See [distribution](../README.md#distribution).

The standalone StarMap host for [war-paint.lib](../war-paint.lib/README.md). Press F11 to open its
window. Select any live EVA kitten, paint it independently, edit each material, and save reusable
presets. DOH can select these presets for new spawns.

The Unscience host registers the shared War Paint scene-save participant. This development host
provides UI and global preset storage; native scene sidecar saves are supplied by Unscience.

`Patcher` applies the shared `HotkeyGuard`; paint ownership and GPU cleanup live in the library.
