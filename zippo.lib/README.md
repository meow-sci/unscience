# Zippo Library

Shared implementation for the standalone **Zippo** mod and the Zippo feature inside **Unscience**. It provides ordinary light appearance controls, queued color/intensity transitions, the Disco party-light engine, and a reusable public API.

## Disco capability

Disco targets one selected light part or every light part on the selected vehicle. A recipe can independently animate:

- an ordered 1-32 color palette or deterministic per-light random rainbow hues;
- a matching light assembly's normalized keyframe actuation range; and
- spotlight inner/outer cone half-angles.

Color, actuation, and spread each have their own transition, hold, and easing settings. A configurable phase jitter assigns an independent, stable random time offset to every channel on every active light, so vehicle-wide shows do not animate in lockstep; zero jitter restores synchronized playback. Each running light owns a deep copy of the recipe, so authoring changes do not alter live effects.

`DiscoLight` replaces each runtime `LightModule.Template` with a complete module-local copy. It gives color and cone-angle channels private reference objects, leaving the shared `PartTemplate` untouched. A matching `KeyframeAnimationModule` is claimed by only one Disco light at a time. Stop, target disappearance, and unload restore the original template and restore actuator/switch values only while Zippo still owns the value it wrote.

Ordinary appearance changes and queued transitions remain intentionally compatible with the existing Zippo behavior. Animation queues use runtime `Part.InstanceId` keys so duplicate part names cannot compete. Starting Disco cancels the ordinary queue for that exact light; applying ordinary settings or queuing an ordinary transition stops Disco first, so the two engines never compete.

## Key files

- `LightController.cs` — light discovery and ordinary shared-template reads/writes.
- `LightAnimation.cs` / `LightAnimationManager.cs` — bounded per-part transition queues.
- `DiscoRecipe.cs` / `DiscoTiming.cs` — validated detached recipe and repeating channel sampling.
- `DiscoLight.cs` — per-instance template ownership, animation update, and restoration.
- `ZippoSubmod.cs` / `ZippoSubmod.Disco.cs` — lifecycle, UI, public API, and conflict coordination.

Build from the repository root with `dotnet build ksa-mod-experiments.slnx`. See [`../scope/celestial-and-lights.md`](../scope/celestial-and-lights.md) for the exact KSA integration surface and the required in-game checks.
