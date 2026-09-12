# Parts Now (parts-now.lib)

Runtime Part / SubPart loading for KSA. Paste Part XML into a brand new mod folder, or load, reload
and unload an existing mod folder — all without restarting the game. New parts show up in the
vehicle editor's part browser immediately, with thumbnails, mass and game data attached.

`parts-now.lib` holds the whole implementation. It is used by the standalone
[`parts-now`](../parts-now/README.md) mod (F10 window) and by the
[unscience](../unscience) supermod as the `Parts Now` submod.

---

## What it does and why it works

KSA loads Parts and SubParts from XML into a set of static registries during startup
(`ModLibrary.AllParts`, `AllMeshes`, `AllFiles`, `AllMaterials`, `AllPartGameDataReferences`), then
renders one thumbnail per top-level Part. **Every stage of that pipeline is still reachable after
boot**, so parts-now re-runs it by hand for one mod at a time:

| Startup stage | Game entry point | How parts-now reaches it |
|---|---|---|
| Parse XML → `AssetBundle` | `XmlHelper.Serializers[typeof(AssetBundle)]` | Uses the game's own serializer instance — a hand-built `XmlSerializer` misses the `XmlAttributeOverrides` that map `<PartModel>`, `<Tank>`, `<Collider>` … onto `Components` and would silently drop every component |
| Register templates | `AssetBundle.OnDataLoad(mod)` | Called directly, once per document |
| Read GLB / KTX2 from disk | `ILoader.Load()` via `ModLibrary.Loaders` | Only the loaders this job appended, run on a background thread |
| Upload to the GPU | `IBinder.Bind()` via `ModLibrary.Binders` | Only the binders this job appended, on the game thread, 4 per frame |
| Merge GameData onto Parts | `PartTemplate.ApplyGameData` | Re-implemented incrementally — the stock `ModLibrary.AttachGameData()` is additive and would double every part already attached at boot |
| Render thumbnails | `ThumbnailCreator` + `ThumbnailRenderer` | Rendered against `Program.ThumbnailViewport` (viewport 1, offscreen), 2 parts per frame |
| Part browser listing | `VehicleEditor.PartWindow.OnDrawUi` | Nothing to do — it re-reads `ModLibrary.AllParts.GetList()` **live every frame**, so new parts appear with no refresh call |

Two things make this safe rather than merely possible:

* **Mesh headroom.** KSA puts every part mesh into one shared interleaved vertex/index buffer pair,
  sized once inside `ModLibrary.Bind()`. parts-now inflates the size counters from
  `[StarMapAllModsLoaded]` — which StarMap fires as a Harmony postfix on `ModLibrary.LoadAll()`,
  i.e. *before* that allocation — and rewinds the allocation cursor on the first UI frame. See
  [Mesh headroom](#mesh-headroom-parts-nowtoml).
* **Validation before anything is touched.** Parsing has no side effects, so all fifteen rules run
  against the live registries before a single template is registered or a single byte is written.

The only Harmony patch parts-now applies is the repo-mandatory `HotkeyGuard`. Everything else is
plain calls plus a small, self-testing reflection layer.

---

## The two workflows

### 1. Paste XML into a brand new mod folder

Intended for XML produced by another tool, or hand-written.
Pasted XML is **always** materialised into a real KSA mod folder — nothing is loaded from memory.

1. Fill in **Mod Id**, **Display Name**, **Author**, **Version**. The mod id is validated live
   ([rules below](#mod-id-rules)) and the resolved absolute target path is shown under the form.
2. Paste up to three documents into the **Assets**, **Part** and **GameData** tabs (256 KiB each).
   Each tab has a paste-from-clipboard button, a clear button and a character counter. Blank tabs
   are skipped.
3. **Validate** parses the documents and runs all fifteen rules. Nothing is written or registered.
4. **Install & Load** (enabled only once the mod id is valid and validation is clean) writes:

   ```
   <mods>/<mod-id>/
       mod.toml                  name / description / version / author / assets[]
       <mod-id>-assets.xml       only if the Assets tab was non-empty
       <mod-id>-part.xml
       <mod-id>-gamedata.xml
   ```

   Files are UTF-8 **without** a BOM with `\n` line endings, each written to a `.tmp` sibling and
   moved into place so an interrupted write never leaves a half-valid mod folder. An existing
   `mod.toml` (when the folder is being reused) is merged rather than replaced, so a `[StarMap]`
   section, `systems` or hand-added assets survive.
5. On success the mod is added to the game's manifest as `Enabled = true, New = false`, so it also
   loads normally at the next launch. (`new ModEntry(id, count)` is deliberately *not* used — that
   constructor sets `Enabled = false, New = true` and triggers the "confirm mods" popup at boot.)
   **This matters**: a vehicle saved with runtime-loaded parts will not resolve them without it.

If the install fails at any point after the folder was written, parts-now deletes the folder it
created — otherwise the mod id would stay unusable for the rest of the session, because a folder
that exists is itself a validation failure. A folder that was already on disk is never deleted.

### 2. Load / reload / unload an existing mod folder

The **Mod folders** panel lists every folder under `ModLibrary.LocalModsFolderPath` that contains a
`mod.toml`, with a filter, its declared `assets[]` entries and a tick per file that actually exists.

Each folder is classified two ways:

| Kind | Meaning |
|---|---|
| `Content` | Has a non-empty `assets` array — the only loadable kind |
| `StarMap` | Has `[StarMap] EntryAssembly` and no assets — a code mod; listed but disabled |
| `Both` | Both of the above; loadable |
| `Empty` | Neither; listed but disabled |

| State | Meaning |
|---|---|
| `LoadedAtBoot` | KSA loaded it during startup. Never loadable, never unloadable — *"loaded at startup — restart the game to reload"* |
| `LoadedByPartsNow` | parts-now loaded it this session. Reload and Unload are available |
| `NotLoaded` | On disk but not loaded. Load is available |

Note the ordering: the scanner checks parts-now's own registry **before** `ModLibrary.Find`.
`Mod.MakeUsing` deliberately stays out of `ModLibrary.Lookup`, so `ModLibrary.Find` normally returns
null for a mod parts-now loaded — but *not* when the same folder was also enabled at boot, because a
reload of it reuses KSA's own `Mod` object. Testing the game first would mislabel those
`LoadedAtBoot` and permanently block reloading them.

* **Load** runs the same pipeline as an install, minus the folder write. The documents come from the
  `assets` array in `mod.toml`, read in order.
* **Reload** = purge + load. Because the purge removes every id this mod introduced from every
  registry (the live list **and** the private hash dictionary behind `Find`), the fresh load sees no
  duplicates and `FileReference.Load()` really re-reads a changed GLB or KTX2. Without the purge it
  would not: `SerializedCollection.Register` returns `false` for a duplicate id and every caller
  reads that as "this is a reference to the existing entry", skipping the file read entirely.
  The purge happens at the **end** of validation, so XML you have just broken leaves the loaded mod
  exactly as it was.
* **Unload** purges without loading anything back. It is synchronous — the purge is bounded work.

Reload and Unload are destructive, so both go through a confirmation modal naming the mod id and its
part count, and both are gated by the [safety gate](#the-reload--unload-safety-gate).

### The load pipeline

One job at a time, one state per frame, driven by a single `RuntimeModLoader.Step()` call from
`PartsNowSubmod.Update(dt)`:

```
Idle
 ├─ Validate          parse + V1..V15; a reload purges its previous load here, after the rules pass
 ├─ WriteModFolder    paste flow only
 ├─ CreateMod         reuse ModLibrary.Find(id), else Mod.MakeUsing(id, mod.toml); Preload forced off
 ├─ RegisterBundles   AssetBundle.OnDataLoad per document
 ├─ RunLoaders        BACKGROUND worker; polled for completion
 ├─ CheckMeshBudget   abort cleanly if the loaders overflowed the shared buffer
 ├─ Bind              4 binders per frame, game thread
 ├─ AttachGameData    incremental ApplyGameData + ResolveConsumerFeedPoints
 ├─ WarmModels        PartModel / Glass / Dynamic .Get, so a bad <Mesh Id> surfaces here
 ├─ Thumbnails        2 parts per frame
 ├─ RefreshEditor     VehicleEditor.ResetPartDiameterCache()
 └─ Done | Failed(rollback)
```

Every transition is appended to a log shown in the UI and mirrored to the console with a
`parts-now: ` prefix. Cancel is honoured only **between** states — never mid-Vulkan and never while
the loader worker is running — and a cancelled job is unwound exactly like a failed one.

Failure handling splits at the `Bind` state: a failure **before** any bind rolls back (purge, then
rewind the shared-buffer allocation cursors); a failure **after** the first bind purges with leak
accounting instead, because a bound mesh has already copied its data to an absolute offset and
handing that range out again would corrupt it.

One subtlety worth knowing when reading the code: the registry deltas cannot all be captured at one
point. `AllParts`, `AllPartGameDataReferences`, `AllMaterials` and `ModLibrary.Loaders` gain entries
during `RegisterBundles`, but `AllFiles`, `AllMeshes` and `ModLibrary.Binders` only gain them during
`RunLoaders` (`FileReference.Load` registers itself and then calls `DoLoad`, which is where atlas
nodes and binders appear). All marks are taken up front; the two delta sets are read at their own
moments. Capturing them all early would leave unload silently incomplete.

---

## Mod id rules

`ModIdValidator` — every rule is an error, there are no warnings, and each rule runs in its own
try/catch so one failing lookup cannot mask the others. A rule that *cannot* run is reported as a
failure rather than silently passing: creating a folder that collides with an existing mod is not
recoverable from inside the game.

| Rule | Detail |
|---|---|
| Shape | Must match `^[a-z0-9]+(?:-[a-z0-9]+)*$` — lower-case kebab-case, the same regex `mkmod.ts` enforces |
| Length | 3–48 characters |
| Reserved | `Core`, `Sample`, `parts-now`, `unscience` (compared case-insensitively) |
| Mods folder | `<mods>/<id>` must not already exist |
| Content folder | `Content/<id>` must not exist — that is where KSA's built-in mods live |
| Loaded mods | `ModLibrary.Find(id)` must be null |
| Manifest | `ModLibrary.Manifest.Mods` must not already list the id |

The mods directory always comes from `ModLibrary.LocalModsFolderPath` — the path the game itself
discovered — never a hardcoded string. It is sanity-checked against
`MeowSci.KsaAbstractions.KsaPaths.UserDataDir + "\mods"` and a mismatch is logged, but the game's
value always wins. If the game reports no mods folder, or the manifest is not available yet, the
corresponding rule fails closed with an explanation.

---

## Mesh headroom (`parts-now.toml`)

`<mods>/parts-now/parts-now.toml`:

```toml
vertexHeadroomMiB = 48   # default 48, clamped to 4..512
indexHeadroomMiB = 12    # default 12, clamped to 4..512
hotkey = "F10"           # standalone window toggle; any ImGuiKey member name
```

A missing file simply leaves the defaults in place and writes nothing. Reading and writing never
throw; a bad value is logged and replaced with the default.

### Why headroom exists

`DeviceMeshInterleaved.Shared` owns exactly **one** vertex buffer and one index buffer, sized from
`Shared.RunningVertexBufferSize` / `RunningIndexBufferSize` the first and only time `Shared.Build()`
runs, inside `ModLibrary.Bind()`. Every `new DeviceMeshInterleaved(...)` atomically bumps those
counters and records its own offset, so a mesh created after `Build()` lands past the end of the
allocation and its `vkCmdCopyBuffer` writes out of range.

parts-now solves this with a two-step trick that depends entirely on StarMap's lifecycle:

1. `MeshBudget.Reserve()` runs from `[StarMapAllModsLoaded]` — a Harmony postfix on
   `ModLibrary.LoadAll()`, which is *before* `ModLibrary.Bind()` allocates. It records the startup
   watermark and inflates both counters by the configured headroom.
2. `MeshBudget.OnFirstFrame()` runs from the very first `PartsNowSubmod.Update(dt)` — long after
   `Build()` — and rewinds both counters back to the watermark.

Net effect: the buffers are allocated at `watermark + headroom` bytes while the bump cursor sits
back at `watermark`, leaving the headroom free for runtime meshes at correct offsets. Both steps log
what they did, and both warn if `Shared.IsBuilt` disagrees with the expected ordering — that warning
is the tripwire for a future KSA change that moves the allocation.

**Changing the headroom only takes effect on the next launch of the game.** The buffers are
allocated once during startup and can never be resized (`Shared.Rebuild()` is not a way out — it
copies `VertexAllocation.BufferSize` bytes out of the *old* buffer). The Settings section of the UI
says so next to the two sliders, and the values it saves are what `Reserve()` reads at the next
boot.

A load that would overflow the headroom is caught in `CheckMeshBudget`, **before** anything is
bound, and fails with the exact numbers and the setting to change:

> Mesh headroom exhausted (needed X MiB, Y MiB free). Increase `vertexHeadroomMiB` in
> parts-now.toml and restart the game.

### The leak counter

The shared allocator is a monotonic bump pointer with no free list. When a mod is unloaded or
reloaded, its slice of the shared buffer **is not reclaimed** — those bytes stay spent until the
game restarts. The status strip therefore reports, next to the two usage bars:

> Orphaned by unload / reload: *N* MiB vtx / *M* MiB idx

and turns that line into a warning once the orphaned total passes **50 % of the reserved headroom**
on either buffer, recommending a restart before loading much more. Rollbacks are excluded from the
counter: a rollback rewinds the allocation cursors instead of orphaning the bytes, so charging them
as leaked as well would double-count them. `MeshBudget.RestoreCursors` also refuses to rewind below
the startup watermark — a zero snapshot would otherwise hand the next runtime mesh offset 0 and let
it overwrite the game's own geometry.

---

## Validation rules

All fifteen rules run as pure functions over the parsed documents plus a read-only look at the live
registries. **Any Error blocks the load**; warnings are informational. Each rule is isolated in its
own try/catch, so one rule throwing (for instance because a KSA rename broke a reflection accessor)
cannot lose the other fourteen rules' findings — it is reported as a warning naming itself.

The documents submitted together are always validated as **one set**, because they legitimately
cross-reference each other (a Part in one file, its SubParts and PartGameData in others).

| # | Severity | Rule | What it catches / why it matters |
|---|---|---|---|
| V1 | Error | Root element must be `<Assets>` and the document must deserialize | `XmlLoader` swallows parse errors and returns null. A wrong root or malformed XML must be a loud failure, with the line and column |
| V2 | Error | Every `<Part>`, `<SubPart>`, `<PbrMaterial>` needs a non-empty `Id`; a file reference needs an `Id` **or** a `Path` | `SerializedId.OnDataLoad` sets `IsReferenceable = !string.IsNullOrEmpty(Id)` and an unreferenceable template is never registered. File references fall back to `Id = ModPath`, so a `Path` is enough — and `<Texture Id>` with no `Path` is a legal *reference* to an already-loaded file |
| V3 | Error | No declared id may collide with an already-registered id in the same registry (`AllParts`, `AllMaterials`, `AllMeshes`, `AllFiles`) | `SerializedCollection.Register` returns `false` on a duplicate and every caller treats that as "a reference to the existing one" — the declaration is silently dropped, and a colliding file is never read from disk. Ids owned by the mod being reloaded are exempt; the reload purges them first. Game-data collisions are left to V14 so they are reported once, with the explanation that matters |
| V4 | Error | No id declared twice inside the submitted set | Same silent drop as V3. The message names every document involved |
| V5 | Error | Every `<SubPart InstanceOf="X">` must resolve, in this set or in `AllParts` | `PartInstance.GetTemplate()` → `ModLibrary.Get<PartTemplate>` throws `NullReferenceException` at spawn or thumbnail time, far from the load. Game-data entries are checked too, because `ApplyGameData` merges their `SubPartInstances` into the target |
| V6 | Error* | Every `<Mesh Id="X"/>` must resolve to a mesh this set creates or one already registered | Same lazy failure as V5. A `<MeshAtlas>` names its meshes after the GLB's mesh nodes (skipping names starting with `_`), so the atlas has to be readable at validation time. *When an atlas cannot be inspected the rule degrades to Warning rather than guessing |
| V7 | Error | Every `<EditorTag Value="T"/>` must already exist | `VehicleEditor.MarkEditorTagDefinitionsLoaded()` locks the tag list at boot; after that `RegisterTag` logs a warning and adds nothing, so a part with a new tag has no category button. The message lists every valid tag |
| V8 | Error | Reject `<Substance>`, `<MixtureReaction>`, `<FixedReaction>`, `<ThermalReaction>`, `<GrainGeometry>`, `<Situation>`, `<EditorTagDef>` as top-level assets | Each feeds a library populated once at boot with `Dictionary.Add` (`SubstanceLibrary.LoadAll`, `GrainGeometryLibrary.LoadAll`) or a list the editor locks. Matched only on the bundle's direct children, so a same-named *reference* nested inside game data is not hit |
| V9 | Error | Every model component needs a `<Material>`, and that material must declare **all three** of `<Diffuse>`, `<Normal>`, `<AoRoughMetal>` | **This is the crash guard.** `ThumbnailRenderResources.AddDraw` and `PartModel(.Glass/.Dynamic).WriteInstancesToGpu` read those three `BindlessHandle`s with no null check — a missing channel takes the whole game down at the first thumbnail or the first frame the part is visible. An id-only `<PbrMaterial>` is a reference and is resolved against the set and then the registry before its channels are judged |
| V10 | Error | `<Reaction Id>`, `<Grain Id>`, `<VolumetricExhaust Id>` and `<SoundEvent SoundId>` must resolve | These resolve lazily at part spawn and throw there. When the corresponding game library is empty the check degrades to a warning instead of failing |
| V11 | Error | Every `Path=` attribute must be relative, must not escape the mod folder (`..`, rooted paths, drive letters), and must point at a file that exists | Security, plus `FileReference.Load()` only *logs* a missing file — the mod would load half-broken and silent. When the mod folder does not exist yet (validating pasted XML before the folder is written) the existence half degrades to one warning |
| V12 | Warning | A `<SubPart>` with no `<MeshView>` | Editor picking degrades — there is no collision geometry to click |
| V13 | Warning | A top-level `<Part>` with no matching `<PartGameData Id="…">` | The part loads with no mass, connectors or game-data modules |
| V14 | Error | A `<PartGameData Id="X">` whose id already exists in `AllPartGameDataReferences` | `PartGameDataReference.OnDataLoad` *merges* into the existing reference when registration fails, so the incremental attach would never see the new game data — and the merge is additive, permanently corrupting the existing entry. Exempt for the mod being reloaded |
| V15 | Error | `new textures + BindlessTextures.TextureCount <= MaxTextures - 16` | The pool is a `FreeListIndexPool(maxTextures, allowResize: false)` — exhausting it is fatal, not slow. Textures are de-duplicated by path, and 16 slots are held in reserve for the game's own runtime allocations |

Findings are shown grouped by severity, each line carrying the rule number, the offending element id
and (where the rule reads the XML) the line number, with a **Copy issues** button.

### Corrections found against the 5018 game build

Three details in the plan did not survive contact with the actual game content, and the
implementation follows the game:

* **There is no `<Combustion>` element in 5018.** Reactions are referenced as
  `<Reaction Id="…"/>` inside `<Combustor>` and `<SolidMotor>`.
* **The GrainGeometry *reference* element is `<Grain Id="…"/>`,** not `<GrainGeometry>`.
  `<GrainGeometry>` is a *definition*, which V8 rejects outright.
* **`<Texture Id>` with no `Path` is a legal reference** to an already-loaded file, so V2 requires
  an `Id` **or** a `Path` for file references rather than demanding a `Path`.

One more implementation note: `ModLibrary.TryGet<SoundBehavior>` can never succeed (it takes an
`IsSubclassOf`-only branch, which never matches the base type), so the V10 sound check goes through
`ModLibrary.Get<SoundBehavior>` inside a try/catch. And because `Brutal.Gltf.dll` is referenced by no
project in this repository, mesh-atlas node names for V6 are read straight out of the GLB's JSON
chunk (`GlbMeshNames`) rather than by pulling in the real glTF loader — the cost is independent of
the asset's size.

---

## The reload / unload safety gate

Purging a `PartTemplate` that something still points at leaves that object holding a template which
is in no registry, with a disposed thumbnail image — the game crashes the next time the editor or the
part browser touches it. So the gate **fails closed**: any exception while checking is itself a
refusal, never a silent pass. Every refusal is a sentence the UI shows verbatim in the disabled
button's tooltip.

An unload or reload is refused when:

1. **parts-now did not load this mod this session.** Only mods in `RuntimeModRegistry` can be
   touched; a mod KSA loaded at boot is never offered Load, Reload or Unload, because purging it
   would remove templates parts-now never registered and cannot account for.
2. **A parts-now load job is still running.** `GameRegistry.Unregister` deliberately does not take
   `SerializedCollection`'s private lock; single-threaded access is what makes that safe.
3. **A live vehicle is flying one of the mod's parts.** Every vehicle in the current system is
   walked, recursing through `Part.SubParts`; the refusal names the vehicle and the part.
4. **The vehicle editor is open and contains one of them** — either attached in `EditingSpace` or in
   a detached `UnattachedPartTrees` tree. The refusal names the part and tells you to remove it or
   close the editor.
5. **The mod's folder is gone** (reload only) — there is nothing to load back.

A record that registered no parts at all skips checks 3 and 4: nothing live can be referencing it.

### The purge order

Once the gate passes, the purge runs in a strict order, each step individually try/caught so one
broken object cannot strand the rest half-registered:

```
0.  clear VehicleEditor.DynamicThumbnail's hover preview   (it would otherwise draw freed buffers,
                                                             or throw out of Editor.OnPreRender)
1.  renderer.Device.WaitIdle()
2.  PartTemplate.Dispose() + unregister from AllParts
3.  unregister PartGameDataReferences
4.  prune PartModel / PartModelGlass / PartModelDynamic .Instances, .InstancesRayTrace and both
    Template.RayTracers lists — matched by OBJECT IDENTITY, never by Template.Id
5.  dispose bound textures (never handle 0 — that is the bindless library's shared empty texture)
    and unregister them
6.  measure, dispose and unregister meshes; record the leak
7.  unregister the remaining file references (atlases, mesh files)
8.  unregister materials
9.  remove this load's entries from ModLibrary.Loaders / Binders
10. VehicleEditor.ResetPartDiameterCache()
11. forget the LoadedModRecord
```

Step 4 matches by identity because `ModuleBase.TemplateDataBase.Id` is an *optional* XML attribute
that nothing requires to be present or unique: matching by id would miss every id-less template
(leaving a stale `PartModel` that `PartModel.Get` would hand to the reloaded part, still pointing at
the purged mesh's old buffer offsets) and would evict another mod's instances on a collision.

The purge is idempotent at the record level via a `Purged` flag, because it is *not* idempotent at
the item level — `ThumbnailReference.Dispose()` and `TextureReference.Dispose(Device)` both
double-free.

---

## Architecture

```
parts-now.lib/
  PartsNowSubmod.cs                 ISubmod entry point: Initialize() reserves headroom + runs the
                                    self-test, Update(dt) drives one loader step, RenderContent()
                                    draws the four panels, Dispose() abandons an in-flight job

  Runtime/
    GameRegistry.cs                 THE ONLY file allowed to use reflection. Resolves ModLibrary's
                                    internal SerializedCollection<T> fields and
                                    VehicleEditor._editorTagLookup once, adds the Unregister<T>
                                    helper SerializedCollection lacks, and SelfTest()
    MeshBudget.cs                   shared-buffer headroom reservation, budget queries, cursor
                                    snapshot/restore and leak accounting
    PartsNowSettings.cs             parts-now.toml (headroom + hotkey), lazily loaded, never throws

    BundleParser.cs                 side-effect-free deserialization via KSA's own serializer
    BundleParserQueries.cs          classification over AssetBundle.Assets, most-derived type first
    BundleValidationIssue.cs        ValidationIssue record + IssueSeverity
    BundleValidator.cs              runs V1..V15, each rule isolated
    BundleValidatorContext.cs       the indexes the rules share, built once per Validate() call
    BundleValidatorRulesIdentity.cs   V1, V2, V3, V4, V14, V15
    BundleValidatorRulesReferences.cs V5, V6, V7, V10, V13
    BundleValidatorRulesSchema.cs     V8, V9, V11, V12
    GlbMeshNames.cs                 reads mesh node names out of a GLB's JSON chunk (for V6)

    RuntimeModLoader.cs             the state machine core: Step(), Log, Progress, Fail/rollback
    RuntimeModLoaderApi.cs          StartInstall / StartLoad / StartReload / Unload + preconditions
    RuntimeModLoaderStates.cs       Validate, WriteModFolder, CreateMod, RegisterBundles, RunLoaders
    RuntimeModLoaderGpuStates.cs    CheckMeshBudget, Bind, AttachGameData, WarmModels, Thumbnails,
                                    RefreshEditor, CompleteJob
    RuntimeModLoaderDeltas.cs       registry marks, the two delta capture points, loader
                                    post-condition verification
    RuntimeModLoaderJob.cs          LoadJob + LoadJobKind — per-job state between frames
    LoadedModRecord.cs              everything one load registered, in the shape an unload needs
    RuntimeModRegistry.cs           session map of mod id -> LoadedModRecord

    RuntimeModUnloader.cs           the gate entry point, the purge driver and Rollback()
    RuntimeModUnloadGate.cs         the fail-closed safety checks
    RuntimeModPurgeSteps.cs         the bodies of the numbered purge steps
    EditorRefresh.cs                VehicleEditor.ResetPartDiameterCache() — the one post-load nudge

    PartThumbnailGenerator.cs       2 parts per frame against Program.ThumbnailViewport
    ThumbnailReadback.cs            optional diagnostic image->buffer copy (non-zero texel fraction)

  Io/
    ModIdValidator.cs               mod-id rules + mods-directory discovery + target path
    ModFolderWriter.cs              mod.toml + XML files (atomic, UTF-8 no BOM, LF) + manifest entry
    ModFolderScanner.cs             read-only survey of the mods directory: kind, state, loadability

  Ui/
    StatusPanel.cs                  self-test banner, mesh budget bars, bindless slots, job state
    StatusPanelSettings.cs          the Settings and Limitations collapsibles
    PastePanel.cs                   mod-id form + resolved target path + Validate / Install & Load
    PastePanelActions.cs            the validate/install handlers and result capture
    XmlTabEditor.cs                 the Assets / Part / GameData tabs (256 KiB each)
    ModFolderPanel.cs               folder scan, filter, table, selection detail
    ModFolderPanelActions.cs        Load / Reload / Unload buttons, tooltips, confirm modal
    ResultsPanel.cs                 per-part thumbnail table, job log, validation findings
    ValidationIssueView.cs          grouped issue rendering, shared by two panels
    PanelStyle.cs                   shared colours, formatters and widget idioms
```

`StatusPanel` is the single source of truth for whether loading is possible: it publishes
`LoadingEnabled`, which the other panels take as their `Render(bool canLoad)` argument, so the
banner and the disabled buttons can never disagree. Nothing expensive runs per frame — the folder
scan, the unload safety gate and mod-id validation are all cached and recomputed only on the events
that change them.

### The reflection tripwire

`GameRegistry` resolves everything it needs once, in a non-throwing static constructor, and splits
what it finds into two buckets:

* **Fatal** — the six `ModLibrary` registries and `SerializedCollection<T>._collection` (the removal
  path). Any of these missing means parts-now cannot register or purge safely, so loading is
  disabled behind a red banner naming the exact member.
* **Degraded** — `VehicleEditor._editorTagLookup`. Losing it only narrows editor-tag validation to
  the built-in tags plus the registered tag definitions, which is still a usable check, so it is
  shown as an amber notice and loading stays enabled.

That split is what turns a future KSA rename into a readable message instead of a crash.

---

## The threading rule

> **Everything runs on the game thread except `RuntimeModLoader`'s loader step, which runs on a
> `Task.Run` worker. The worker touches only `ILoader.Load()`. Completion is polled from
> `Update(dt)`.**

It is repeated at the top of every file in the project. Two hard reasons stand behind it:

* **Loaders must be off the main thread.** `FileReference.Load()` calls `Loading.Task()` →
  `Loading.PushTask()` → `Loading.Current.OnFrame()`, which renders a *complete ImGui frame* and
  submits it — catastrophic inside the game's own frame. `Loading.OnFrame()` early-returns when
  `!Program.IsMainThread()`, so on a worker the whole thing is a no-op. (Nulling `Loading.Current`
  is not an alternative: `LoadTask`'s initialiser throws when it is null, and that throw escapes
  `FileReference.Load`'s try block.)
* **Binders and thumbnails must be on the game thread.** `StagingPool.Dispose()` submits to
  `renderer.Graphics` and blocks, and `vkQueueSubmit` is externally synchronised. Both are only safe
  from `Program.OnDrawUiFrame` — i.e. `ISubmod.Update(dt)` — which runs before the frame's swapchain
  image is acquired.

All game-state work is deliberately kept on the game thread so parts-now remains safe standalone.

---

## Known limitations

1. **Mesh memory is never reclaimed.** Each reload permanently consumes headroom in the shared
   interleaved buffer until the game restarts. Reload budget ≈ headroom ÷ mod mesh size.
2. **Headroom is fixed at launch.** Changing it in `parts-now.toml` requires a restart.
3. **New EditorTags, Substances, Reactions and GrainGeometry are rejected.** Parts must reference
   ids that already exist. (Implementable later: tags need reflection into `VehicleEditor`'s
   `_editorTags` / `_editorTagLookup` / `_editorTagDefinitionsLoaded` and its whitelist/blacklist
   lists; substances, reactions and grains need reflection into `SubstanceLibrary`'s and
   `GrainGeometryLibrary`'s private dictionaries plus a call to each template's `Create()`.)
4. **Mods loaded at boot cannot be reloaded** — only mods parts-now itself loaded this session.
5. **Reload and unload require the mod's parts to be unused** — no live vehicle may use one, and the
   vehicle editor must not hold one (an editor that is open but contains none of them is fine).
6. **Raytracing (IVA) is untested.** With `GameSettings.Current.Graphics.IVARayTracing` on, the
   shared buffer is allocated through `RaytraceAllocator` and BLASes reference it. Headroom still
   works (the buffer is simply bigger), but verify with Vulkan validation enabled before claiming
   support; if it misbehaves, disable loading while raytracing is active.
7. **Saved vehicles depend on the mod folder staying put.** Deleting `<mods>/<id>/` will break any
   vehicle that used its parts.

The same list is shown in-game under **Limitations** in the status strip, so nobody has to open this
file to find out why a reload was refused or where their mesh memory went.

---

## Troubleshooting a bad thumbnail

Thumbnails are rendered against `Program.ThumbnailViewport` (viewport index 1) — the offscreen
viewport KSA creates at boot purely for thumbnails. Because that camera is never driven by the
player, this needs no camera save/restore, no viewport resize and produces no `Following <x>` alerts.
The invariant is **never move this camera — move the root part**; each batch only re-asserts
origin/identity before writing the camera UBO. Sharing the viewport with the part browser's hover
preview is safe: parts-now submits in `Update(dt)` and `ThumbnailDynamic.Render` submits later in
the *same* frame, each writing the camera UBO immediately before its own fence-waited submit.

Work through these in order. **None of them is a reason to change approach.**

| Symptom | Cause to check |
|---|---|
| Image is uniformly transparent `(0,0,0,0)` | Zero collected draws — `CollectDraws` found no `PartModel` / `PartModelDynamic`, i.e. the part's SubPart templates carry no `<PartModel>` or a mesh id did not resolve. The draw count is logged for **every** part (`thumbnail '<id>' collected N draw(s)`), and a part with zero draws is recorded as *"no draws collected"* and deliberately gets no image at all |
| Part is off-centre, clipped, or a dot | `MoveRootPart` framing. It uses `camera.GetFieldOfView()` and `camera.NearPlane`, and honours a `<Thumbnail><ModelTransform>` if the part declares one. Verify `ComputeBoundingSphereRadius` returns non-zero — it reads `MeshReference.Get().HostMesh`, which requires the loader step to have run |
| Geometry is right, shading is wrong or black | The camera UBO write. `ThumbnailDynamic.UpdateGlobalCameraData(viewport, camera)` must run **after** `camera.OnFrame(...)` and **before** the submit, and the viewport index must be 1 — `RecordPartRender` binds `GlobalShaderBindings.DynamicOffset(viewport.Index)` |
| Textures are wrong or garbage | Bindless handles. `ThumbnailRenderResources.AddDraw` reads `Material.*.BindlessHandle`, which is only valid after that `TextureReference`'s `Bind()` ran — which is why the `Thumbnails` state always runs after `Bind` completes. A texture whose bind *failed* keeps `BindlessHandle == 0`, the bindless library's shared empty white texture, so the part renders plain white; failed binds mark their parts **Degraded** in the results table naming the asset |
| Nothing renders and the log is silent | The fence wait. `WaitForFence(fence, -1)` must return `VkResult.Success`; a non-Success result is logged and recorded as the part's failure reason |
| Thumbnail size looks off | `ThumbnailRenderer.SIZE` reads `GameSettings.Current.Graphics.PartThumbnailSize` live, while the viewport was sized at boot. If the player changed the setting mid-session a warning is logged — both are square, so framing is unaffected. The game setting is never mutated |

For a one-click answer, `PartThumbnailGenerator.DebugReadback` copies each rendered image back into
a host-visible buffer and reports the fraction of non-zero texels, in the log and in that part's
result row. A uniformly transparent image is the render pass's clear colour, so a zero fraction is a
positive diagnosis rather than a guess.

---

## Related

* [`parts-now`](../parts-now/README.md) — the standalone StarMap wrapper (F10 window)
* [`scope/part-editor-and-robotics.md`](../scope/part-editor-and-robotics.md) — the authoritative map
  of every game integration point parts-now depends on
* [`plans/done/PARTS_NOW_PLAN.md`](../plans/done/PARTS_NOW_PLAN.md) — the design document, including
  the in-game test matrix and an as-built list of where the implementation diverged from it

## Scene saves

Records runtime mod IDs and part-template dependencies. Installed folders and enabled manifest entries already persist through Parts Now; save loading does not duplicate, unload or execute asset bundles. Required part mods must be installed/enabled before KSA reads the native save. Template IDs unavailable at restore produce actionable dependency diagnostics; the sidecar is not a portable asset bundle.

Scene persistence is registered by the Unscience host through `ISaveParticipantSource`; standalone development hosts retain their existing lifecycle. Use ordinary KSA Save/Load. See [save behavior](../unscience/README.md#scene-saves) and the [design plan](../plans/SAVES.md).
