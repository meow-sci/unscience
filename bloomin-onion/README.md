# bloomin-onion — define new planetary rings at runtime

> Distribution: only the `unscience` mod is shipped. This project is retained as a development
> boundary; build artifacts stay in `bin/`. See [distribution](../README.md#distribution).

KSA only knows about rings that the celestial XML data declares (`<Rings>` on Saturn). Bloomin'
Onion lets you author a **complete ring definition in-game** — every value the XML can set — and
put it on **any celestial body** (a moon, Earth, Jupiter, a ringless planet) from an ImGui panel.
Rings appear immediately; nothing in the game's data files is touched.

It is the sibling of [rocky-mcrock-face](../rocky-mcrock-face) (which swaps the meshes/textures of
rings that already exist): bloomin-onion *creates* rings, and reuses rocky's asset catalog and
mesh-conversion pipeline for the rock field.

## What you can set

| Section | Parameters |
|---|---|
| **Geometry** | definition frame (equatorial / ecliptic), inclination, longitude of ascending node, inner/outer radius (km), shader detail scale; a *Fit to Body* helper sizes the ring from the body's radius |
| **Ring band** | **Painted**: base color, any number of colored stripes (start/end/softness/color+opacity), ringlet noise (amount/scale/seed), the opacity threshold above which rocks draw — with a live preview strip. **Texture**: any game texture as the band plus a control strip (filtered to uncompressed RGBA textures, since the game CPU-samples it) |
| **Volumetric dust** | min/max thickness, min/max render distance, raymarch step min/max/scale, fade-to-meshes |
| **Rock field** | rock size, field thickness, draw distance, density (per km³, with a per-chunk instance-count estimate), 1–5 LODs with per-LOD screen-size threshold and mesh (any mesh in the game incl. part/kitten meshes, via rocky's converter), rock material diffuse/normal/AoRoughMetal |

Empty asset slots fall back to the stock Saturn assets (band/control textures, Luna rock meshes and
material), resolved from whatever ring the current system already has, else by their known ids.

## Using it

1. Load a save. Pick a **Body** (bodies are tagged `[stock rings]` / `[custom: name]`).
2. Build the ring: start from **New Ring** (a Saturn-like painted ring with the stock LOD ladder),
   from a saved **Preset**, from **Copy <body>'s Ring** (loads Saturn's definition into the editor),
   or **Edit Applied** to tweak a ring already on this body.
3. Press **Fit to Body**, adjust stripes/colors while watching the preview, tune the rest.
4. **Apply to <body>**. The renderer rebuilds (brief hitch) and the ring is there. Applying to a
   body that already has a ring (stock or custom) replaces it until you **Remove**.
5. **Save Preset** keeps the definition in
   `My Games/Kitten Space Agency/.unscience/bloomin-onion-rings.toml`. Presets are the authored
   work; *which body wears which ring is session-only by design* — a game restart is back to
   stock (within a session a ring stays on its body across save reloads, since the body template
   carries it).

Notes:
- *Planetary Rings* (and *Ring Meshes* for the rock field) must be on in graphics settings; the
  panel warns when they are off.
- The inner radius must be outside the body; the panel refuses definitions that would not build
  and reports why, leaving the game untouched.
- Density × draw-distance³ sizes the GPU instance buffers — the panel shows the resulting
  rocks-per-chunk against the stock value and warns when it gets heavy. Same for triangle counts
  of custom LOD meshes (keep LOD 2+ light).
- The ecliptic frame needs a parent body; it is disabled for the root body.
- The ring's shadow on the planet follows automatically (the planet shader reads the ring
  reference every frame). The far-away "distant sphere" ring shadow is patched in best-effort.

### Rings on vessels / kittens?

Not directly: the ring renderer is bound to `Celestial` at every level — camera-relative position,
orbital scrolling from the body's mass, the ring plane from the body's rotation frame, and the
per-body render data keyed by celestial hash. There is no seam to hang a ring on a `Vehicle`.
The workable trick is to apply a ring to a small moon and **weld that moon to the vessel with
kiwis-marbles** — the ring rides along with the moon.

## How it works

1. **Definition → game data.** `RingReferenceBuilder` turns a `RingDefinition` into a fully populated
   `PlanetaryRingsReference` tree (`PlanetaryRingsVolumeReference`, `RingRaymarchingStepReference`,
   `RingObjectsReference`, `RingLodReference` + `MeshFileReference`, `PbrMaterialReference`) — the
   exact object shape `PlanetaryRingsRenderData` reads at construction. Angles are normalized the
   same way the game's XML loader does. All assets are resolved first, so a bad definition never
   reaches the game.
2. **Painted textures.** `RingBandPainter` rasterizes the stripes/noise into a 2048×1 RGBA8 band and
   a matching control strip (R = rocks allowed, G = dust thickness). `PaintedTextureReference` is a
   `TextureReference` subclass fed from memory (`GenericTexture` → `TextureAsset`) that runs the
   game's own `Bind` (SimpleVkTexture upload + bindless handle), so the renderer sees a normal
   texture. The CPU copy stays alive because the game samples the control strip every frame.
   Textures are cached by paint-content hash and freed after a rebuild once unreferenced.
3. **Apply.** The body template's `RingsReference` is swapped (original snapshotted for Remove).
   Then `RingRendererRebuilder`: `Device.WaitIdle()`, dispose the existing `PlanetaryRingsRenderer`
   and clear `_ringRendererCreated`, call the public `PlanetTransparenciesRenderer.PopulatePlanets()`
   (refreshes which bodies have rings) and write its result into the private `_anyRings`, then
   `Program.RebuildRenderer()` — the game's own settings-apply path — whose `CreateRingsRenderer`
   branch rebuilds all ring render data from the current references. Removing the last ring in a
   system without stock rings correctly ends with no rings renderer at all.
4. **Rock meshes** go through rocky-mcrock-face's `RingAssetCatalog` / `RingMeshFactory` (Simple
   meshes as-is; interleaved part meshes cloned to a `SimpleVkMesh`; glTF-file meshes imported).

## Public API (MeowSci.BloominOnionLib)

- `BloominOnionSubmod` — the `ISubmod` (also hosted standalone, F11 window); exposes `Controller`, `Presets`
- `RingDefinitionController` — `Apply(celestial, definition, out message)`, `Remove(celestial, out message)`,
  `RemoveAll`, `Applied`, `HasStockRings`, `TryGetApplied`, `RefreshAssets`; owns `Catalog`, `MeshFactory`,
  `TextureFactory`, `Stock`, `Builder`
- `RingDefinition` (+ `RingStripe`, `RingLodDefinition`, `RingBandSource`) — the editable model; `CreateDefault()`
- `RingReferenceBuilder` — `Validate`, `Build`, `IsCpuSampleable`
- `RingBandPainter` — `PaintBand`, `PaintControl`, `Evaluate`, `BandId`/`ControlId`
- `PaintedTextureReference` / `RingTextureFactory` — runtime textures
- `RingRendererRebuilder` — `Rebuild`, `IsRingsRendererCreated`, `SyncDistantSphereShadow`
- `RingPresetStore` / `RingDefinitionSerializer` — TOML presets, `FromReference` import
- `StockRingAssets` — stock fallback resolution

## Game integration scope

See [`../scope/rings.md`](../scope/rings.md) (bloomin-onion section). No Harmony patches. Reflection:
`Program._planetTransparenciesRenderer` → `{_ringsRenderer, _ringRendererCreated, _anyRings}`,
`TextureReference.<TextureAsset>k__BackingField`, and the cosmetic
`StaticCelestial._distantRenderer` → `DistantSphereRenderer._data` sync — plus rocky's catalog
reflection. Everything else is typed public API.
