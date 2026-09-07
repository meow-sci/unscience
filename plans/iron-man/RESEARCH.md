# Iron Man: existing EVA kittens as editable rocket vessels

Research and implementation assessment for Unscience, 2026-09-07. Sources: KSA build
**2026.9.7.5402**, from the adjacent `ksa-game-assemblies/current` checkout. The older
`unscience/decomp/ksa` tree was excluded. Audience: maintainers implementing and validating the mod.

## Answer

The current game supports a targeted runtime implementation. A `KittenEva` already inherits
`Vehicle`, owns a normal part tree, and uses the game's part/module infrastructure. It does not
need conversion into a different vehicle object or a global edit of the backpack XML. Iron Man
keeps the existing kitten and adds instance-owned connectors, editor support and an opt-in switch
from character locomotion to ordinary vessel physics.

This is **implemented and compile/managed-test verifiable**, but rocket flight, snapping and native
rendering still require an in-game acceptance pass. The source supports the architecture; source
inspection cannot establish final handling quality or Vulkan/Bepu behavior.

## Evidence and implementation choices

| Question | Primary source evidence in current decomp | Decision |
|---|---|---|
| Can an existing kitten enter the editor? | [Program.cs](../../../ksa-game-assemblies/current/decomp/KSA/Program.cs), line 3826 blocks the menu for EVA; `OnFrameEditor`, line 2475, constructs `VehicleEditor(ControlledVehicle,false)`. [VehicleEditor.cs](../../../ksa-game-assemblies/current/decomp/KSA/VehicleEditor.cs), `Build`, line 1174, accepts any existing Vehicle. | The explicit Iron Man button sets `Program.EditorFlag`; use the stock transaction. No global editor-menu unlock. |
| Does editor exit preserve the kitten? | [VehicleEditor.cs](../../../ksa-game-assemblies/current/decomp/KSA/VehicleEditor.cs), lines 2293–2330, updates `ExistingVehicle.Parts`, configuration and control. | Preserve the same vehicle identity, Character and orbit. Protect its body/root from deletion, copying, grabbing, focus replacement and New Vehicle. |
| Can connectors be authored per instance? | [Part.cs](../../../ksa-game-assemblies/current/decomp/KSA/Part.cs), `Connector` at line 191, has a public template constructor, transform fields and owner; `Part.Connectors` is mutable. Editor `HandleSnapping` iterates connectors. | Create private `TemplateBase` objects and public runtime connectors. Never edit the shared `PartTemplate`. Up/down defaults plus editable position/direction/radius and add/remove controls. |
| Which direction is up? | [PartGameData.xml](../../../ksa-game-assemblies/current/Content/Core/PartGameData.xml), line 24, documents feet at origin and -Z up. `Part.Connector` snapping uses local +X as its outward normal. | Rotate connector +X onto -Z (up) or +Z (down). Use ordinary external node flags even when geometrically inside the body; `Internal` means different mating rules. |
| Will fuel cross a node? | [Part.cs](../../../ksa-game-assemblies/current/decomp/KSA/Part.cs), connector capability construction and `Connection.Connect`; [ResourceManager.cs](../../../ksa-game-assemblies/current/decomp/KSA/ResourceManager.cs), resource graph walks connection edges. | Explicit `BulkFluid` plus stock service-fluid/electric defaults. Keep real stock connections and resource groups; no synthetic force or unlimited propellant. |
| Can copied or saved connectors survive? | [PartTree.cs](../../../ksa-game-assemblies/current/decomp/KSA/PartTree.cs), `Serialize`/`DeepCopy` at lines 260/280; [Part.cs](../../../ksa-game-assemblies/current/decomp/KSA/Part.cs), `GetReferenceWithChildren` at 2023, `RegenerateConnectionsFromPartInstance` at 2309, store and restore node indices. | A versioned marker in the existing serialized `PartInstance.Id` carries private node definitions. Constructor prefix restores the original runtime Id; postfix reconstructs nodes before stock connection regeneration. Validate version, bounds and stock-node count; reject invalid marked data instead of continuing with missing indices. |
| Are normal engine modules sufficient? | [VehicleUpdateState.cs](../../../ksa-game-assemblies/current/decomp/KSA/VehicleUpdateState.cs), `PrepareFromVehicle`, line 295, sets `IsKitten` from CLR type. [PhysicsBubble.cs](../../../ksa-game-assemblies/current/decomp/KSA/PhysicsBubble.cs), `FlightComputerInputsFor`/`AdvanceKittenLocomotion`, and [PoseIntegratorCallbacks.cs](../../../ksa-game-assemblies/current/decomp/KSA/PoseIntegratorCallbacks.cs), lines 73/95, branch on it for character inputs/servos. | For activated instances only, set the worker snapshot `IsKitten=false`. Existing mass, collision, fuel, engines and RCS then follow ordinary vessel behavior. Reverse-call base Vehicle input handlers to avoid EVA action interception. |
| Why do added parts need a render patch? | [Program.cs](../../../ksa-game-assemblies/current/decomp/KSA/Program.cs), line 4210 excludes EVA from early part submission, line 4227 uploads batches, lines 4327/4522 draw EVA later. [KittenEva.cs](../../../ksa-game-assemblies/current/decomp/KSA/KittenEva.cs), line 1064 calls base at that late point. | Submit the base Vehicle part rendering before upload; suppress the duplicate late base submission while retaining character draw. Applies to authored equipment even after flight is disabled. |
| Why does the editor need an avatar patch? | [Program.cs](../../../ksa-game-assemblies/current/decomp/KSA/Program.cs), `RenderEditor`, line 4793 clears character buckets then renders the prepass, without drawing KittenEva. | After the matching `SuperMeshRenderSystem.ClearBuckets`, submit the existing public `Renderable` using the editor's assembly-to-camera matrix. No new model, shader or GPU layout. |
| Are blueprint and world saves equivalent? | [VehicleSaveData.cs](../../../ksa-game-assemblies/current/decomp/KSA/VehicleSaveData.cs), `Create(string,PartTree)`, character lookup depends on save name; [CelestialSystem.cs](../../../ksa-game-assemblies/current/decomp/KSA/CelestialSystem.cs), lines 621/677 dispatch world-save EVA correctly. Empty-editor launch calls `Vehicle.CreateVehicle` at editor line 2274. | Preserve blueprint Character for a matching authored live kitten root. World-save reload is supported by metadata restoration. Opening/launching an EVA blueprint from an empty stock editor is not the supported workflow. |

## Activation and restoration

Activation is session-only and tied to the actual kitten object, not a reusable string ID. An
ordinary game launch, new save load and a newly spawned kitten all start with flight/editor
changes off. Loading authored attachment data restores the nodes and their rendering without
activating vessel physics. The user explicitly enables each kitten from the Iron Man panel.

Transitions and node changes use the existing `PhysicsFrameHook` handoff after completed worker
results and before new snapshots. Activation rejects ladder attachment, clears held input and
starts in manual flight with engines stopped. Disabling shuts down/disarms engines and restores
the saved flight-computer modes; parts and connectors remain. Editor exit must precede disable.
Unload closes an owned editor while its protections are present and converts attachment links
before removing persistence hooks where possible.

## Consequences and limits

- Parts follow the rigid body assembly. They are not skinned to animated ankle bones. Positioning
  engines beside the feet gives a rocket-boots arrangement while stationary; limb animation can
  separate the feet visually from that hardware.
- Ordinary vessel physics replaces walking, swimming, righting and ladder behavior while enabled.
  Normal structural failure and G-load limits replace special EVA handling. This is a deliberate
  consequence of the selected routing flag, not a custom flight model.
- The initial version left native EVA HUD restrictions in place. The [flight-computer follow-up](FLIGHT_COMPUTER.md)
  now selects vessel gauges and stock button eligibility for enabled kittens. Part picking remains
  EVA-specific; equipment is configured through the editor.
- Tank propellants, resource groups, engine activation, balance and available thrust still matter.
  Asymmetric boots can spin the kitten; RCS/TVC authority must counter the actual torque.
- Marked saves with connected nodes require the mod. Loading those files in stock KSA can index
  nonexistent connectors. Removing the mod does not rewrite previously saved files.
- Current 5402 VehicleEditor has no undo/redo facility. Metadata supports its underlying deep-copy
  and serialization paths; connector controls do not invent an undo history.
- Native acceptance is outstanding. In particular verify body alignment at different scales,
  main/secondary views, contact behavior, engine fuel use, connection occupancy and save reload.

## Search record and stopping rationale

Three independent research lanes traced editor/lifecycle, connectors/persistence, and
physics/rendering through bounded `rg` searches and selected method bodies. Follow-up searches
resolved the initial false inference that inherited rendering alone was sufficient, the copy
index hazard, blueprint Character loss and character-servo conflicts. The coordinator re-read
the decisive editor lifecycle, worker classification, rendering order and persistence seams.
Research stopped when each implementation dependency had primary source support; remaining
questions require the live native runtime rather than more source searches. No internet sources
or claims about another KSA build were substituted.

See [integration scope](../../scope/iron-man.md) for the maintained hook inventory and acceptance
checklist, and [usage](../../iron-man/README.md) for player controls.

## Verification completed

- `dotnet build ksa-mod-experiments.slnx --no-restore --disable-build-servers -m:1
  -p:UNSCIENCE_DIST_DIR=/private/tmp/iron-man-dist -v minimal`: passed, zero warnings/errors.
- `dotnet run --project iron-man.tests --no-restore -v quiet`: passed; actual Harmony
  constructor/serializer hooks, connector math/isolation, XML/index reconstruction, invalid-data
  rejection and transactional unload rollback.
- `dotnet run --project iron-man-flight.tests --no-restore -v quiet`: passed; actual Harmony
  per-instance/default-off routing, base dispatch, early/late rendering, disabled visibility,
  cross-mod installation order and unload/reapply.
- Documentation links checked; native acceptance remains unchecked in the maintained scope.
