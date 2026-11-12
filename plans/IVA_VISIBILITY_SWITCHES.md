# IVA visibility follows explicit Kitchen Sink switches

Implemented on KSA **2026.11.10.5801**, 2026-11-11.

## Behavior

Unscience previously revealed internal models whenever the vehicle editor rendered a part view,
even with **Always Render IVA Interiors** and **See Inside Capsule (Experimental)** both off.
The legacy editor hook originated with the Space Tape editor integration (`63aff62`); the 5482
upgrade (`db3fc9d`) moved it to `PartTreeRenderData.Compose` while retaining its unconditional policy.

Forced visibility now requires either explicit switch in editor and flight views. With both off,
KSA's native interior gates apply. Native IVA and inside-vehicle/crew portrait views continue to
show interiors normally. Capsule-glass window filtering retains its existing ownership.

## Implementation and source evidence

`ksa-abstractions.lib/IvaForceRender.cs` removes the Compose prefix/finalizer and editor template
cache. It retains the constructor postfix, loaded-template mutation when `EffectiveEnabled`
becomes true, shadow-proxy exclusion and restoration when the final visibility owner releases.
Both Unscience and the standalone Kitchen Sink development host use this shared implementation.

CURRENT `KSA/PartTreeRenderData.cs:1386` reads `model.Template.Internal` directly during native
composition; `KSA/PartModel.cs:490` does the same at individual submission. Effective visibility
already clears that flag on existing models and the constructor postfix covers new models.
Neither path needs an additional editor hook. The helper's direct `Program.Editor` and viewport
reads are retired; PartRenderFilter's independent Compose patches remain installed as before.

The `kitchen-sink` and `kitchen-sink-capsule-glass` boolean v1/order20 records keep their IDs,
payloads, reset and replay APIs. Editor entry owns no saved visibility state. Tracked template
references and constructor patch state are transient; native camera visibility remains KSA-owned.

## Validation

- The new off-in-editor regression fails against the prior unconditional hook, then passes after
  removal. `ksa-upgrade.tests` passes all **30** capsule render/ownership checks, including all four
  editor switch combinations, final-owner release while editing, new models and native IVA/portraits.
  Other upgrade regressions pass; the unrelated Free Fallin live shader check skips because the
  test invocation did not supply its Content lookup environment variable.
- `kitchen-sink.tests` passes **26** general save/restore, **17** capsule-glass save/restore,
  **47** IVA camera ownership/persistence and **108** G-load checks, including real KSA.dll metadata.
- `dotnet build ksa-mod-experiments.slnx --no-restore -m:1 -p:UseSharedCompilation=false` succeeds
  with **0 warnings and 0 errors**, using CURRENT `dll/` via explicit `KSAFolder`; distribution
  output is redirected to `/private/tmp/unscience-iva-visibility-dist`.
- `git diff --check` passes.

## Native acceptance pending

- Enter and leave the editor with both switches off; internal models follow native visibility.
- Enable each switch alone and both together; release the last enabled switch while editing.
  Confirm forced interiors stop and capsule exterior windows return when the experiment turns off.
- Repeat in flight, native IVA and crew portraits, including new models and shadow proxies.
- Save/load every switch combination, repeat A → B → A and load vanilla/legacy scenes. Visibility
  follows restored switches without editor entry acquiring ownership. Unload restores baselines.

No native game was launched on this macOS host. See [integration scope](../scope/00-architecture-and-abstractions.md#ivaforcerendercs)
and [save acceptance](saves-acceptance.md#capsule-glass-native-acceptance-pending).
