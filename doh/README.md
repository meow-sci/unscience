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
6. Use **Smite** or **Smite All** to request KSA's built-in kill action on the next input pass.

**Spawn Kitten(s)** and **Refresh Characters** share an equal-width action row with the standard
12 px gap. **Spawned Kittens** uses a striped **Name / Model / unlabeled action** table, with a red
**Smite** button per row and a full-width red **Smite All** below. Action confirmations and errors
use KSA's logger; the panel has no persistent status/count labels or War Paint helper text.

KSA handles death and vehicle removal. Its native action records **Abandoned** where a roster
entry exists; custom reason strings such as "smote" are unsupported. DOH kittens normally have no
roster entry. War Paint releases each dead kitten's private GPU materials after native destruction;
saved presets and other kittens' paint remain available. Unloading DOH leaves vehicles to KSA.

The selected reference vehicle remains stable when vehicles appear or disappear. Removing it
clears the selection. Each painted kitten has private materials, including in batches. Pool budget
checks refuse a painted batch that would consume the game's reserved GPU slots.

DOH contains no tint picker, random-color spawn button, material table or live recoloring controls.
War Paint owns all kitten paint, including kittens created by native KSA and other mods.

## Built-in kitten roster

Spawned kittens appear in KSA's native roster as active EVA crew with the chosen name/model.
Mission time, health/radiation and travel statistics use KSA's normal tracking. Boarding, recovery
and native death update that same entry; Smite leaves the native KIA record instead of erasing it.
Older tracked DOH kittens are integrated on load or during live maintenance. Existing native crew
history is preserved. Failed spawns leave no new roster row.

## Saves and compatibility

Native KSA saves own the actual kittens, names and full roster/career data in `universe.xml`. The unchanged **`doh` v1** sidecar record restores
registry ownership and spawn controls without spawning duplicates. The preset selector includes a
detached recipe so scenes remain spawnable if the global preset changes or disappears.

Older v1 records retain `UseCustomColor`, `TintColor` and `UniquePerKitten` compatibility fields.
An enabled old spawn tint becomes a deterministically named saved War Paint preset; its detached
recipe survives even if saving the global preset fails. Old kitten `Tint` and named material colors
migrate through War Paint's normal ownership API. Legacy shared `MaterialGroup` recipes become
independent, identically painted kittens: editing one no longer recolors its former batch peers.
New scene saves store active kitten paint in War Paint's own record.

Explicit saved offsets survive. Fresh, vanilla and missing-offset records use **(1, 1, -2) m**.
Native kill inputs retain the exact object, so a pending request cannot delete a same-name kitten
reconstructed by a scene load. Native physics/GPU acceptance remains required; see
[kill-action evidence](../plans/DOH_NATIVE_KILL_5801.md) and [managed checks](../doh.tests/README.md).

## Architecture

`doh.lib` owns creation, native kill requests and spawned-kitten tracking; `war-paint.lib` owns paint,
presets and GPU clones. This host supplies StarMap lifecycle and hotkey integration.
