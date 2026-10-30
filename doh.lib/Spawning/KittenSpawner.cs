using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.DohLib.Materials;
using MeowSci.KsaAbstractions;

namespace MeowSci.DohLib.Spawning;

/// <summary>
/// Spawns kitten entities (KittenEva) programmatically.
/// Replicates the game's EVADoor.CreateKittenEva() flow with additional features:
///   - Arbitrary positioning (vehicle-relative or absolute orbital)
///   - Batch spawning with offset chains
///   - Optional per-kitten material customization
///
/// Partial files: Positioning (spawn frame), Materials (per-kitten cloning), Catalog (game lookups),
/// Naming (exact names and numbered prefixes), Teardown (despawn, prune and native vehicle destruction).
/// MUST be called on the game thread (not from HTTP handlers directly).
/// </summary>
public sealed partial class KittenSpawner
{
    /// <summary>
    /// Free GPU material slots required before constructing a KittenEva. Its avatar allocates a
    /// fur material slot (released again when the name already exists) and throws if none is free.
    /// </summary>
    private const int MinFreeSlotsForKitten = 2;

    private readonly MaterialFactory _materialFactory;
    private readonly SpawnedKittenRegistry _registry;

    public KittenSpawner(MaterialFactory materialFactory, SpawnedKittenRegistry registry)
    {
        _materialFactory = materialFactory;
        _registry = registry;
    }

    /// <summary>Spawns kitten(s) according to the request parameters.</summary>
    public SpawnResult Spawn(SpawnRequest request)
    {
        try
        {
            return SpawnInternal(request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: Spawn error: {ex}");
            return SpawnResult.Failure($"Spawn error: {ex.Message}");
        }
    }

    /// <summary>Updates the tint color on a previously spawned kitten's materials.</summary>
    public bool RecolorKitten(string kittenId, float4 newColor)
    {
        var entry = _registry.Get(kittenId);
        if (entry?.MaterialSet == null) return false;
        return entry.MaterialSet.UpdateTint(newColor);
    }

    /// <summary>Lists all available character IDs from ModLibrary.</summary>
    public string[] GetAvailableCharacters()
    {
        try
        {
            var characters = GetAllCharacterReferences();
            return characters.Select(c => c.Id).OrderBy(id => id).ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: GetAvailableCharacters error: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    // ---- Internal implementation ----

    private SpawnResult SpawnInternal(SpawnRequest request)
    {
        // Validate
        if (request.Count < 1 || request.Count > 100)
            return SpawnResult.Failure("Count must be between 1 and 100.");

        var system = Universe.CurrentSystem;
        if (system == null)
            return SpawnResult.Failure("No current system.");

        PruneDisposed();

        // Resolve positioning
        var pos = ResolvePositioning(request);
        if (pos.Error != null)
            return SpawnResult.Failure(pos.Error);
        if (pos.Parent == null)
            return SpawnResult.Failure("Failed to resolve positioning.");

        // Resolve character
        string characterId = request.CharacterId ?? GetRandomCharacterId();
        if (string.IsNullOrEmpty(characterId))
            return SpawnResult.Failure("No character available.");

        bool tinted = request.TintColor.HasValue || request.PerKittenColors != null;
        bool uniqueSets = request.UniqueMaterialsPerKitten || request.PerKittenColors != null;
        string? budgetError = CheckMaterialBudget(tinted ? (uniqueSets ? request.Count : 1) : 0);
        if (budgetError != null)
            return SpawnResult.Failure(budgetError);

        // Spawn loop
        var results = new List<SpawnedKittenInfo>();
        KittenMaterialSet? sharedSet = null;
        int untinted = 0;

        for (int i = 0; i < request.Count; i++)
        {
            int freeSlots = MaterialSystemAccessor.GetFreeSlotCount();
            if (freeSlots >= 0 && freeSlots < MinFreeSlotsForKitten)
                return Stopped(results, request.Count, $"GPU material pool exhausted ({freeSlots} slots free)");

            // Preserve exact single names; batch/automatic names skip occupied IDs.
            string? kittenName = GenerateKittenName(system, request);
            if (kittenName == null)
                return Stopped(results, request.Count, $"The name '{request.VehicleName}' is already used by a vehicle or a rostered kitten. Choose another name");

            // Create backpack part
            Part? backpackPart = CreateBackpackPart();
            if (backpackPart == null)
                return Stopped(results, request.Count, "Failed to create backpack part (KittenBackPackPart not found)");

            // Calculate this kitten's position offset
            double3 chainOffset = pos.OffsetCci * (i + 1);
            double3 kittenPosCci = pos.BasePositionCci + chainOffset;
            double3 kittenVelCci = pos.VelocityCci;

            // Create KittenEva. The Vehicle ctor mutates the BepuPhysics shapes registry
            // (BepuHandles.Create -> ConstraintSim.UnlockShapes), which throws if the
            // background vehicle physics step is still running. Wait for it to go idle.
            WaitForVehicleSolverIdle();
            KittenEva kittenEva;
            try
            {
                kittenEva = new KittenEva(
                    system,
                    characterId,
                    pos.Body2Cce,
                    pos.BodyRates,
                    pos.Parent!,
                    kittenName,
                    backpackPart,
                    pos.ReferenceOrbit!);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"doh: KittenEva construction failed for '{kittenName}': {ex}");
                RemoveOrphanedKitten(system, kittenName);
                return Stopped(results, request.Count, $"Kitten construction failed: {ex.Message}");
            }

            // Create orbit and teleport
            var orbitColor = pos.ReferenceOrbit?.OrbitLineColor ?? new byte4(255, 200, 0, 255);
            var orbit = Orbit.CreateFromStateCci(
                pos.Parent!, pos.StateTime, kittenPosCci, kittenVelCci, orbitColor);
            kittenEva.Teleport(orbit, null, null);

            // Register with parent body
            pos.Parent!.Children.Add(kittenEva);
            kittenEva.UpdatePerFrameData();

            // Apply custom materials — clone every material this kitten uses, or share the
            // batch's set when unique materials were not requested.
            KittenMaterialSet? matSet = null;
            if (tinted)
            {
                float4 color = float4.One;
                if (request.PerKittenColors != null && i < request.PerKittenColors.Length)
                    color = request.PerKittenColors[i];
                else if (request.TintColor.HasValue)
                    color = request.TintColor.Value;

                matSet = ApplyClonedMaterials(kittenEva, color, characterId, uniqueSets ? null : sharedSet);
                if (!uniqueSets) sharedSet ??= matSet;
                if (matSet == null) untinted++;
            }

            // Track in registry
            _registry.Register(kittenName, characterId, matSet);

            results.Add(new SpawnedKittenInfo
            {
                KittenId = kittenName,
                CharacterId = characterId,
                MaterialSetId = matSet?.Id,
                TintColor = request.TintColor,
                PositionCci = kittenPosCci,
                VelocityCci = kittenVelCci,
                ParentBodyName = pos.Parent!.Id
            });
        }

        Console.WriteLine($"doh: Spawned {results.Count} kitten(s)");
        return new SpawnResult
        {
            Success = true,
            SpawnedKittens = results.ToArray(),
            Warning = untinted > 0
                ? $"{untinted} kitten(s) could not be tinted: {MaterialSystemAccessor.LastError ?? "material cloning failed"}"
                : null
        };
    }

    /// <summary>
    /// Refuses a spawn whose cloned materials would not fit in the game's fixed-size GPU material
    /// pool without eating into the reserve the game itself needs. Returns an error or null.
    /// </summary>
    private static string? CheckMaterialBudget(int setsNeeded)
    {
        if (!MaterialSystemAccessor.IsInitialized && !MaterialSystemAccessor.Initialize())
            return setsNeeded > 0 ? $"MaterialSystem unavailable: {MaterialSystemAccessor.LastError}" : null;

        int available = MaterialSystemAccessor.GetAvailableSlots();
        int needed = setsNeeded * MaterialSystemAccessor.EstimatedSlotsPerSet;
        if (available < 0 || available >= needed) return null;

        return $"Not enough GPU material slots: ~{needed} needed for {setsNeeded} tinted set(s), {available} available " +
            $"({MaterialSystemAccessor.ReservedSlots} kept for the game). Despawn tinted kittens, turn off " +
            "'Unique Each', or spawn fewer.";
    }

    private static SpawnResult Stopped(List<SpawnedKittenInfo> spawned, int requested, string reason)
    {
        Console.WriteLine($"doh: Spawn stopped: {reason} ({spawned.Count}/{requested} spawned)");
        return new SpawnResult
        {
            Success = false,
            Error = $"{reason} ({spawned.Count} of {requested} spawned).",
            SpawnedKittens = spawned.ToArray()
        };
    }
}
