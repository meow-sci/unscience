# War Paint

> Distribution: only `unscience` ships. This compile-checked development host stays in `bin/`.
> See [distribution](../README.md#distribution).

The standalone StarMap host for [war-paint.lib](../war-paint.lib/README.md). Press F11 to open the
preset editor: filter/select or create a preset, preview RGBA material colors, save changes, and apply
to multiple EVA kittens. Live assignments have red X removal buttons; preset deletion confirms and
removes all matching assignments. DOH uses the same global library and named assignments.

Only the Unscience host registers scene sidecar persistence. This development host provides the
shared UI and global preset storage. The Kitten Visors section has a checked-by-default global
Show visor glass checkbox. `Patcher` applies/removes `HotkeyGuard` and the per-view visor gate;
paint ownership and GPU cleanup live in the library. Action feedback uses the game logger.
