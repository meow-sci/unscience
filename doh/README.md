# DOH — Dynamically Originating Hominids

DOH creates and manages EVA kittens. Choose a saved **War Paint** preset when spawning; create
presets and edit any live kitten in the central **War Paint** panel. A selected preset keeps its
colors until you reselect it; deleted presets appear as **saved colors**.

Only the `unscience` mod is distributed. This project is a compile-checked development host.

## Usage

1. Open Unscience → **DOH - Clone Kittens** (standalone development host: **F8**).
2. Select the reference vehicle and optionally a character model; the default character is random.
3. Enter a vehicle name (one kitten) or name prefix (batch). Blank input uses automatic
   `Kitten_doh_` numbering. Existing live or rostered kitten names are reserved, case-insensitively.
4. Set the body-frame offset (right, up, forward), default **(1, 1, -2) m**, and count (1–20).
5. Choose a saved **War Paint preset**, or **stock appearance**, then click **Spawn Kitten(s)**.
6. Use individual **Despawn** or **Despawn All** to queue removal at the next physics handoff.

The selected reference vehicle remains stable when vehicles appear or disappear. Removing it
clears the selection. Each painted kitten has private materials, including in batches. Pool budget
checks refuse a painted batch that would consume the game's reserved GPU slots.

DOH contains no tint picker, random-color spawn button, material table or live recoloring controls.
War Paint owns all kitten paint, including kittens created by native KSA and other mods.

## Saves and compatibility

Native KSA saves own the actual kittens and names. The unchanged **`doh` v1** sidecar record restores
registry ownership and spawn controls without spawning duplicates. The preset selector includes a
detached recipe so scenes remain spawnable if the global preset changes or disappears.

Older v1 records retain `UseCustomColor`, `TintColor` and `UniquePerKitten` compatibility fields.
An enabled old spawn tint becomes a deterministically named saved War Paint preset; its detached
recipe survives even if saving the global preset fails. Old kitten `Tint` and named material colors
migrate through War Paint's normal ownership API. Legacy shared `MaterialGroup` recipes become
independent, identically painted kittens: editing one no longer recolors its former batch peers.
New scene saves store active kitten paint in War Paint's own record.

Explicit saved offsets survive. Fresh, vanilla and missing-offset records use **(1, 1, -2) m**.
Queued despawns retain the exact registration, so a pending request cannot delete a same-name kitten
reconstructed by a scene load. Native physics/GPU acceptance remains required; see
[despawn evidence](../plans/DOH_DESPAWN_5801_FOLLOWUP.md) and [managed checks](../doh.tests/README.md).

## Architecture

`doh.lib` owns creation, native-safe despawn and spawned-kitten tracking; `war-paint.lib` owns paint,
presets and GPU clones. This host supplies StarMap lifecycle and hotkey integration.
