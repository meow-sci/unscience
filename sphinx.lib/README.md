# SphinxLib

Reusable `SphinxSubmod : ISubmod`, hosted by Unscience and the thin Sphinx development host.
See [Sphinx usage](../sphinx/README.md) for controls, limits and supported GLBs.

- `SphinxSubmod` owns placements, pending UI actions, shared-library discovery and cleanup.
  `Ui` and `Editing` partials own the controls; queued uploads execute during Update, outside UI
  rendering. Transform widgets queue live edits on change; PNG and UV edits are queued separately
  so failed texture replacements retain the previous texture without blocking transforms. Each
  queued action captures its target and values, and ignores removed entries.
- `GroundPlacement` samples accurate celestial terrain, estimates slope from one-metre samples,
  ray-marches/bisects main-view ground picks and builds a body-fixed CCF-to-camera frame.
- `PlacementMath` computes pure glTF Y-up scale/rotation/grounding/offset transforms from original
  bounds, with finite/range/overflow checks. `SphinxEntry` combines that with the celestial frame.
- `StaticGeometry` adapts Pebbles' host GLB mesh/material streams to the native static-object
  vertex and material layouts. It duplicates backfaces for double-sided materials.
- `TextureMapping` applies per-entry UV scale then offset to immutable original vertices, leaving
  positions/normals intact. Defaults/reset are identity; duplicate copies the mapping. All maps use
  the same coordinates and repeat sampler. Invalid/nonfinite/overflowing edits are rejected.
- `StaticModelResources` owns private vertex/index/material buffers, sampler, descriptors and
  mapped per-frame/per-viewport matrices. It borrows cached GLB textures from its owning
  `ClutterAssets`; optional `ImportedPngTexture` is private. Source identities freeze file content.
  Instance storage uses the device's storage-buffer alignment and the actual frames-in-flight
  and viewport limit. A frame stamp prevents draws before that slice has been prepared.
  Its `Textures` partial uploads replacement vertex buffers for live UV changes, keeps the old
  buffers until the upload and device wait succeed, then swaps and retires them. UV-only edits
  reuse textures, indices, material descriptors and matrices; PNG changes rebuild the resource.
  Original per-entry vertices are retained in managed memory for noncumulative edits and released
  on disposal. Large models can briefly hitch during live texture edits.
- `SphinxPatches.Apply/Remove` owns three rendering postfixes on `StaticObjectRenderer`:
  UpdateRenderData, private WriteCommandsColor and WriteCommandsPrePass. Record only after the
  native pipeline/global lighting descriptors are bound; replace sets 2/3 and private mesh buffers.
  Terrain buckets, editor views and other celestial bodies are excluded. No global mesh slots,
  static-object registry entries, custom shaders or shadow casters are allocated. Physics hooks are
  owned separately by `SphinxPhysicsPatches` and removed by exact patch method.

- `CollisionGeometry` detects only complete closed bounds boxes; otherwise Auto selects a two-sided
  mesh (or a clearly reported bounds-box fallback above 100,000 triangles). `StaticCollider` owns
  one global Bepu Box/Mesh per entry, baked from the original imported geometry with the same
  grounded transform as rendering. Local center/rotation keep native coordinates compact. Geometry
  validation and the 500,000-triangle total budget precede replacement. Texture edits do not touch
  physics; failed collider builds retain the previous transform/shape.
- `SphinxPhysics` owns handles per `ConstraintSim`, sharing an entry's shape across bubbles. It
  synchronizes at BeginStaticObjectPass and UpdateSimForSnappedOrigin, scoped to CCF/body identity
  and 2 km plus collider radius from any bubble vehicle. Default Bepu awakening handles removed
  supporting surfaces. TryResetForPool/Dispose prefixes remove handles before ids can be reused.
- `SphinxPhysicsPatches` extends the native ground-contact filter only for our simulation/handle
  pairs and `IsGroundSurfaceFor` for surface friction, EVA normals and ground-contact bookkeeping.
  The internal NarrowPhaseCallbacks transpiler validates exactly one BepuHandles.IsGroundSurface
  call and appends its Sim field to a scoped helper; no per-contact reflection or thread-local state.
- Pending edits, placement/removal and unload wait for both vehicle and cloth solvers before
  mutating entries or global shapes. Native callbacks read stable entries; each bubble's handle map
  is mutated only outside its narrow-phase workers. Main-thread retirement detaches every bubble
  handle before RemoveAndDispose frees the global shape. Body/system cleanup uses the same path.

Private uploads use Pebbles' cancellable `AssetUploadSubmission`. Resource retirement waits on
its owning device before freeing descriptors/images/buffers. Removing all placements clears this
feature's GLB cache. Dead celestial references are pruned during Update, including Unscience's
hidden-HUD fallback. Native render hooks themselves are independent of HUD visibility.

Both hosts wire HotkeyGuard and patches through their normal lifecycle. Only Unscience deploys.
Shader bytes, binding assumptions and required native acceptance are recorded in
[scope/statics.md](../scope/statics.md); a successful build is not a Vulkan runtime test.
