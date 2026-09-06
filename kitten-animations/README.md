# Kitten Animations — Avatar Animation Controller

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

Plays any animation the game has loaded for a selected EVA kitten, triggers facial expressions, and
exposes the blend weights and locomotion tuning that decide how hard each animation lands. The target
can follow the controlled kitten automatically or be pinned to any live EVA kitten in the system.

## Overview

Kitten Animations lets you:

- **Target any EVA kitten** — a filterable dropdown follows the controlled kitten by default or pins
  the panel to a named live `KittenEva` without taking control of it
- **Play every loaded clip** — the full ground/EVA locomotion set (idle, walk, run, jump, jump land,
  tumble, ladder, moon walk, moon run, swim, swim idle, seated idle + seated idle actions), the full
  MMU set (idle default, idle actions, six directional loops, arm retract), the live blend samplers,
  and the overlay poses (blink, ear/helmet mask)
- **Hold a clip against the game** — the game re-picks the body clip every frame from the locomotion
  state; a Harmony prefix lets the mod win
- **Scrub playback** — blend time, playback-rate multiplier, freeze, restart
- **Trigger expressions** — Angry / Awe / Happy / Sad / Scared, any authored variant or a random one,
  with configurable strength, ease-in, hold and ease-out, or latched indefinitely
- **Tune animation strength** — ear-motion weight, eye look angle, eye pitch offset, personality mood
  face weight, and a cap on the game's acceleration-driven reactive face
- **Tune locomotion animation** — the animation-facing slice of `KittenLocomotionTuning.Current`
  (blend time, playback-rate clamps, nominal clip speeds, moonwalk/swim blend ramps, jump-land timing)
- **Watch the live state** — locomotion mode, control mode, ground speed, gravity, jump-chain stage,
  the game's own playback rate, and the live blend weights

## Why a Harmony patch is required

`KittenRenderable.UpdateRenderData` runs every frame and calls
`AnimatedRenderable.SetAnimation(...)` unconditionally for almost every locomotion mode — grounded,
airborne, tumbling, on a ladder, swimming — and drives the MMU directional blend sampler when
jetpacking. A clip set from a StarMap callback is therefore overwritten before it is ever sampled.

The mod prefixes `AnimatedRenderable.UpdateAnimation(double dt)`. That is the call the game makes
immediately after it has finished choosing a clip and immediately before the pose is evaluated, so it
is the last point in the frame where an override still lands. The prefix runs for every animated
renderable in the scene and returns immediately unless the instance is the kitten's body model.

The same hook re-applies the animation-processor knobs, because the game rewrites several of them per
frame (`CatEyeAnim.LookPitchOffsetDeg` and the reactive `CatExpressionAnim.ExpressionWeight`).

## Architecture

### Core classes (`kitten-animations.lib`)

#### `KittenAvatarAccessor`

Discovers live EVA kittens and resolves the selected kitten's avatar.

- `GetControlledKitten()` — controlled vehicle as `KittenEva`
- `GetAllKittens()` — all live `KittenEva` vehicles in the current system, sorted by id
- `FindKitten(id)` — stable-id lookup for an explicitly selected kitten
- `GetKittenRenderable()` — `KittenEva.Renderable` (public property; no reflection)
- `GetAvatar(KittenRenderable)` / `GetKittenAvatar()` — `KittenRenderable._characterAvatar`
  (private field, reflection)

#### `KittenAnimationCatalog`

Discovers every animation loaded for the kitten and groups it for the UI.

The ground locomotion set is **not** reachable through `CharacterAvatar.Animations` —
`KittenRenderable` loads it from `CharacterGroundAnimationsReference` into private fields
(`_groundIdleAnim`, `_groundWalkAnim`, `_groundRunAnim`, `_ladderAnim`, `_jumpIntroAnim`,
`_flailAnim`, `_jumpLandAnim`, `_moonWalkAnim`, `_moonRunAnim`, `_swimAnim`, `_swimIdleAnim`,
`_seatedIdleAnim`, `_seatedIdleActionAnims`) and the blend samplers into `_walkPairSampler`,
`_runPairSampler`, `_swimPairSampler`, `_blendSampler`. Those are read by reflection; anything that
fails to resolve is collected in `UnresolvedFields` and surfaced in the UI as a game-update warning.

Clips with a zero loop period are filtered out — `BoneAnimRuntime.SampleCurrentAnimation` divides by
it.

> `CharacterAvatar.Animations.WalkingAnimations` is superseded and deliberately not used. The current
> game build only ever assigns `WalkingAnim` (a duplicate of the ground walk clip) and never assigns
> `RunningAnim`, so the old "Running" button in this mod was a no-op. Run now comes from
> `CharacterGroundAnimations.AnimRun` via `KittenRenderable._groundRunAnim`.

#### `KittenAnimProcessors`

Typed handles on the four `IAnimProcessor` instances `KittenRenderable` installs, read by name rather
than by scanning `AnimProcessors` by type — two of them are the same `CatExpressionAnim` type with
very different roles:

| Field | Type | Role |
|---|---|---|
| `_catPersonalityExpressionAnim` | `CatExpressionAnim` | permanent mood face from `CharacterAvatar.Personality`; absent for Neutral kittens |
| `_catExpressionAnim` | `CatExpressionAnim` | reactive scared face; weight rewritten every frame from linear + angular acceleration |
| `_catEyeAnim` | `CatEyeAnim` | blink, saccades, look-at |
| `_catEarAnim` | `CatEarAnim` | ear/helmet mask pose |

#### `KittenExpressionController`

Owns a `CatExpressionAnim` the **mod** creates and appends to `AnimatedRenderable.AnimProcessors`.

Writing to the game's own expression processor does not work: `KittenRenderable.UpdateRenderData`
damps its `ExpressionWeight` toward an acceleration-derived target every frame, right before the pose
is sampled, so a mod-set weight is gone by the time it would be rendered — leaving the permanent
personality face on screen. That is the mechanism behind the long-standing *"kitten animations don't
properly play each one, always the same"* report. Appending our own processor puts the mod last in
the list, mixing over everything, with a weight nothing else touches.

Envelope: quadratic ease-in → hold at `PeakWeight` → linear ease-out, or `Latch` to hold until
cleared. `CatExpressionAnim` caches its sampled pose in the private `_expressionPose` field, so that
cache is nulled whenever `ExpressionAnim` changes.

#### `KittenAnimationDriver`

Holds the override state and stamps it onto the model from the Harmony prefix
(`ApplyBeforePose(model, ref dt)`):

- forced clip via `SetAnimation` (idempotent) or `PlayAnimation` on restart
- `FreezeAnimation` while paused
- `dt *= PlaybackRateScale`, on top of the game's own `_groundAnimPlaybackRate`
- processor knobs: ear weight, eye look angle, eye pitch, personality weight, reactive-face cap

Never throws out of the prefix — any exception logs and resets the driver.

#### `KittenAnimationPatches`

`Apply(Harmony)` / `Remove(Harmony)` around a prefix on
`AnimatedRenderable.UpdateAnimation(double)`. Applied from `kitten-animations/Patcher.cs` when
standalone and from `unscience/Patcher.cs` when embedded.

#### `KittenAnimationsSubmod`

`ISubmod` implementation. `Update(dt)` resolves either the controlled kitten or the explicitly
selected kitten id, rebinds when the kitten/avatar changes
(rebuilding the catalog, re-reading the processors, re-attaching the expression processor), refreshes
the driver target, and advances the expression envelope. Switching targets clears the target-specific
clip/expression and restores persistent processor values on the old kitten. `RenderContent()` renders
the sections below without any window framing.

### UI sections (`kitten-animations.lib/Ui`)

| Section | Contents |
|---|---|
| `TargetSection` | filterable live-EVA-kitten selector; automatic controlled-kitten mode or stable explicit id |
| `PlaybackSection` | live locomotion readout; override on/off, restart, freeze, clear; blend time and playback-rate multiplier |
| `AnimationLibrarySection` | one collapsible group per catalog group, one button per clip, active clip highlighted, tooltip shows asset id + length |
| `ExpressionSection` | variant selector, five triggers + clear, strength/ease-in/hold/ease-out drag fields (type-to-exceed), latch, live status |
| `StrengthSection` | per-processor override checkbox + slider, plus a live weight readout |
| `TuningSection` | animation-facing `KittenLocomotionTuning.Current` fields, scoped reset, live and derived blend weights |

## UI (`Mod.cs`)

Standalone: **F11** toggles a `Kitten Animations` window that hosts the submod content. Embedded in
unscience under its own collapsible header. Selecting a target never changes game control or moves
the camera. If an explicit target boards or despawns, its id remains selected and is shown as
unavailable until it returns to EVA or another target is chosen.

## Configuration

| Setting | Widget | Range | Notes |
|---|---|---|---|
| Blend Time | drag | 0 – 2 s | cross-fade into the forced clip |
| Playback Rate | drag | 0 – 5 x | multiplies animation delta time; 0 freezes |
| Expression Strength | drag | 0 – 1 | how strongly the expression pose is mixed |
| Expression Ease In / Out | drag | 0 – 3 s | ramp up / down |
| Expression Hold | drag | 0 – 30 s | time at full strength |
| Ear Motion | slider | 0 – 1 | `CatEarAnim.ExpressionWeight` |
| Eye Look Angle | drag | 0 – 90° | `CatEyeAnim.MaxLookAtAngle` (game default 30) |
| Eye Pitch Offset | drag | -90 – 90° | `CatEyeAnim.LookPitchOffsetDeg` |
| Personality Face | slider | 0 – 1 | weight of the permanent mood face |
| Reactive Face Cap | slider | 0 – 1 | upper bound on the acceleration-driven face |

**Ranges are drag limits, not hard limits.** Every **drag** field above is an ImGui
`DragFloat`: the listed range bounds what the mouse can reach, but **double-click or
ctrl+click** opens a text box whose input is *not* clamped — type `120` into
*Expression Hold* and you get a two-minute hold. This is the whole reason the timers are
drag widgets: `SliderFloat` clamps typed input unconditionally, so a slider can never go
past its range. The three weight caps marked **slider** stay clamped on purpose — a
processor weight outside 0 – 1 is not meaningful.

Nothing is persisted; every setting resets on unload.

## Notes

- The game ships its own full locomotion tuning window at **Debug → Kitten Tuning** in the menu bar.
  This mod exposes only the animation-facing subset, and its reset restores just those fields.
- `KittenLocomotionTuning.Current` is global, so tuning edits affect every kitten, not only the
  controlled one.
- If both this mod and unscience are installed, whichever initialises last owns the shared driver
  reference in `KittenAnimationPatches`. Install one or the other.

## Dependencies

- **MeowSci.KsaAbstractions** — `VehicleProvider`, `ReflectionHelpers`, `ISubmod`, `SubmodUI`,
  `HotkeyGuard`
- **Lib.Harmony** — the `UpdateAnimation` prefix
- **KSA game** — `KittenEva`, `KittenRenderable`, `CharacterAvatar`, `AnimatedRenderable`,
  `CatExpressionAnim` / `CatEyeAnim` / `CatEarAnim`, `KittenLocomotionTuning`, `KittenLocomotion`
