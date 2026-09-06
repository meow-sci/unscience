# graffiti — click-to-place PNG decals

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

Paint your own PNG images onto **vehicle hulls, deployed parachutes, and terrain** with a single click. Pick a decal,
press **Place at Click...**, click anywhere in the 3D world — a projected decal is painted onto
whatever surface is under the cursor and stays welded to it (part-local on vehicles, geodetic
lat/lon on terrain).

Two projects, following the repo's submod pattern:

- `graffiti/` — StarMap host (`Mod.cs`, `Patcher.cs`, `mod.toml`). F11 toggles the window.
- `graffiti.lib/` — all logic as `GraffitiSubmod : ISubmod` + `GraffitiPatches.Apply/Remove(Harmony)`,
  consumed by both the standalone host and the `unscience` supermod.

## Using it

**PNG library** — Graffiti and other image-using mods share one managed folder:
`My Games/Kitten Space Agency/.unscience/pngs/`:
- **Import PNG...** opens a built-in ImGui file browser (quick links for Home / Desktop /
  Pictures / Downloads, Windows drive buttons, name filter; double-click folders to navigate,
  double-click a PNG or press Import to pick it). Every import is copied into the shared folder,
  auto-uniquified (`cat.png` → `cat (2).png`), and never overwritten. The same shared browser and
  catalog are used by Free Fallin.
- The folder is created and scanned when the mod starts. Use **Rescan PNGs** after dropping
  files into it by hand; Rescan also hot-swaps changed files on already placed decals. Graffiti
  does not run a background filesystem watcher or polling loop.

**Placing**
1. Pick a **Decal** from the filterable dropdown.
2. Optionally open **Placement settings**: width/height (m), depth (m), roll (deg, relative to
   the "reads upright from here" default), pick range (m), alpha, brightness, a **max draw
   distance** (global for all decals, default 50 km — the camera distance beyond which decals
   stop rendering), and a debug-checker toggle. **Depth** is how far the image projects through the surface — the visible decal is the
   surface ∩ projection box. At 0 it is automatic: half the decal's larger side, floored at
   0.3 m on hulls / 2 m on terrain, so wide decals keep wrapping curved hulls instead of getting
   cropped to their centre (which reads as "zoomed in"). Raise it manually for extreme curvature;
   lower it if the image bleeds through to the far side of thin parts. Terrain decals also deepen
   their box automatically with camera distance (1% of distance): the rendered terrain is a LOD
   mesh whose surface drifts vertically from the true height as you zoom out, and a fixed-depth
   box would empty out and vanish long before the draw-distance cull.
3. Press **Place at Click...** — the mod arms a one-shot placement mode with a hint following the
   cursor. **Click** a vehicle, deployed parachute, kitten, or the ground to place; **Esc** (or the Cancel button)
   backs out.
   A miss ("nothing hit within range") keeps placement armed so a slightly-off click isn't a
   whole round trip; a successful placement returns to normal.
4. Repeat for as many decals as you like — there is no limit.

**Placed Decals** — a multi-select listbox of every placed decal
(`#id  name  →  vehicle/part` or `#id  name  →  body (lat°, lon°)`):
- Click selects, **Ctrl/Cmd+click** toggles, **Shift+click** selects a range.
- **Delete Selected** removes the selection; **Clear All** removes everything.
- Rows show `[anchor gone]` when the target vehicle despawned (the decal is dormant, not deleted
  — it comes back if the vehicle does) and `[image unavailable]` when the PNG is missing/broken.

Placed decals are session-scoped (not persisted across game restarts); imported PNGs remain in the
shared library for later runs and for other mods.
Decals render in the flight scene only (not in the VAB/editor).

## How it works

A near-verbatim port of the projected-decal system from the sibling gatOS repo's sticker feature,
re-hosted as an unscience submod with a point-and-click UX (gatOS aims from the camera centre via
RPC; graffiti raycasts through the clicked cursor position via `Cursor.GetEgoRay`).

- **Pick** — `Cursor.GetEgoRay` (the mouse cursor's ego-space picking ray) is swept against every
  vehicle with `Part.RayCastEgo` — KSA's own watertight triangle raycast over the *art* mesh, the
  same call flight-mode hover picking makes — and, failing that, marched + bisected against the
  CPU terrain height field of `Camera.NearbyCelestial` (`GetTerrainHeightFromDirCcf`, always
  `accurate: true` — the only mode that evaluates the procedural terrain modifiers the rendered
  surface includes) in body-fixed coordinates. Vehicle hits anchor to the hit **sub-part's**
  `InstanceId` with the part-local position/normal; terrain hits anchor to geodetic lat/lon with
  a compass heading that makes the PNG read upright from where the player is standing.
  **KittenEva** kittens have no raycastable part mesh (they render through `CharacterAvatar`), so
  they get the game's own hover-pick treatment: a bounding-sphere raycast anchored to the root
  part, with the box depth floored at the sphere diameter so the projected decal reaches the fur
  inside. (Kitten decals ride the kitten's body frame, not its animated limbs.)
  Deployed canopies are separate skinned cloth renderables and are absent from the part view-mesh
  hierarchy, so they are tested against the current 8-ring × 16-spoke cloth surface. A canopy hit
  stores its triangle's cloth-node indices and barycentric coordinates; the decal consequently
  follows inflation, reefing, and flutter instead of remaining fixed to the parachute housing.
- **Anchor → matrices** — every frame, each decal composes a `[-0.5,0.5]³` decal-space cube
  (S·R·T·parent, row-vector convention) into ego space in double precision:
  part anchors through `Part.MatrixAsmb2Ego` (includes part scale and the sub-part chain),
  terrain anchors through the ENU basis at (lat, lon). Composed in `Update()` (StarMap
  BeforeGui), which runs after the game's camera update and before the render — same-frame, no
  swim.
- **Render** — a Harmony **postfix on `RenderTarget.ResolveAttachments`** (main viewport only,
  not in the editor), the same post-resolve window KSA's own `GridPass` draws in: the resolved
  single-sample scene depth and colour are both current and unbound. One draw per live decal of a
  unit cube (CullFront, no depth test); the fragment shader reconstructs the scene position under
  each pixel from the reverse-Z depth, projects it into decal space, discards outside the box or
  past the grazing-angle cutoff, samples the PNG from KSA's **bindless texture table**, and
  shades with a single sun term + planetshine. Because it projects onto reconstructed depth, the
  decal conforms to hull curvature and tessellated terrain.
- **Textures** — PNGs are decoded with the game's own `TextureLoader` (stb forced to RGBA8),
  uploaded as mip-mapped `SimpleVkTexture`s (longest edge capped at 2048), and given slots via
  `BindlessTextureLibrary.AddTexture`. Freed slots revert to the engine's empty texture
  immediately; the images themselves wait out `MaxFramesInFlight + 1` frames in a retire queue.
- **Lifecycle** — the GPU pipeline (two runtime-compiled GLSL shaders against KSA's shipped
  `Common/*.glsl` headers, unit-cube mesh, per-frame depth-descriptor ring) is built lazily on
  the first live decal and torn down when the registry empties or on unload (after a queue
  drain). Any render fault self-disables the draw path with a single console line.
- **No reflection** — every game API used is public; a game update breaks this mod loudly at
  compile time, not silently at runtime.

## Public API (`MeowSci.GraffitiLib`)

- `GraffitiSubmod.Instance` — singleton; `Decals` (read-only list of `DecalEntry`)
- `PlaceAtCursor(imageName, range, width, height, rollDeg, alpha, brightness, depth?)` — raycast
  + place in one call; `Arm(name)` / `Disarm()` — the one-shot click mode the UI uses
- `RemoveDecals(entries)`, `ClearDecals()`, `RefreshLibrary()`, `DebugBox`
- Shared `PngLibrary` / `PngFileBrowser` from `ksa-abstractions.lib` provide `PngsDir`, `Scan()`,
  `Import(path, out error)`, `FullPath(name)`, and the common filesystem picker.

## Game integration scope

See [`scope/decals.md`](../scope/decals.md).
