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
/// Partial files: Positioning (spawn frame), Materials (per-kitten cloning), Catalog (game lookups).
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
    private int _nextKittenIndex;

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

    /// <summary>Despawns a kitten by ID and releases its materials when no other kitten shares them.</summary>
    public bool Despawn(string kittenId)
    {
        try
        {
            var entry = _registry.Get(kittenId);
            if (entry == null) return false;

            // Use the kitten object captured at registration so a renamed kitten still resolves;
            // never fall back to a non-kitten vehicle that merely shares the name.
            var kitten = entry.Vehicle as KittenEva ?? VehicleProvider.FindVehicle(kittenId) as KittenEva;
            if (kitten != null && !kitten.IsDisposed)
            {
                // Match game's two-phase removal (EVADoor ingress / docking pattern).
                // Dispose releases physics shapes (same registry lock as spawn), so the
                // vehicle solver must be idle first.
                WaitForVehicleSolverIdle();
                Universe.CurrentSystem?.Deregister(kitten);
                kitten.Dispose();
            }

            // Also reached when the game already destroyed the kitten, so the entry and its
            // GPU material slots are never stranded.
            _registry.Unregister(kittenId);
            ReleaseIfUnused(entry.MaterialSet);
            Console.WriteLine($"doh: Despawned '{kittenId}'");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: Despawn error for '{kittenId}': {ex.Message}");
            return false;
        }
    }

    /// <summary>Despawns all kittens spawned by this mod.</summary>
    public void DespawnAll()
    {
        var ids = _registry.KittenIds.ToList();
        foreach (var id in ids)
            Despawn(id);
    }

    /// <summary>
    /// Drops registry entries whose kitten the game already destroyed (e.g. it boarded a vehicle)
    /// and returns their GPU material slots to the pool. Returns the number of entries pruned.
    /// </summary>
    public int PruneDisposed()
    {
        var disposed = _registry.GetDisposed();
        if (disposed == null) return 0;

        foreach (var entry in disposed)
        {
            _registry.Unregister(entry.KittenId);
            ReleaseIfUnused(entry.MaterialSet);
        }
        Console.WriteLine($"doh: Pruned {disposed.Count} kitten(s) the game already removed");
        return disposed.Count;
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

            // Generate unique name
            string kittenName = GenerateUniqueName(system);

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

    private void ReleaseIfUnused(KittenMaterialSet? set)
    {
        if (set != null && !_registry.IsMaterialSetInUse(set))
            _materialFactory.Release(set);
    }

    /// <summary>
    /// The Vehicle base constructor registers the kitten in system.All before KittenEva builds its
    /// renderable, so a throw there leaves a registered kitten without a renderable, which the game
    /// then updates and draws every frame. Remove it again.
    /// </summary>
    private static void RemoveOrphanedKitten(CelestialSystem system, string kittenName)
    {
        if (!system.All.TryGet(kittenName, out Astronomical? orphan) || orphan == null) return;

        try { system.Deregister(orphan); }
        catch (Exception ex) { Console.WriteLine($"doh: Failed to deregister orphaned '{kittenName}': {ex.Message}"); }

        if (orphan is Vehicle vehicle)
        {
            try { vehicle.Dispose(); }
            catch (Exception ex) { Console.WriteLine($"doh: Failed to dispose orphaned '{kittenName}': {ex.Message}"); }
        }
        Console.WriteLine($"doh: Removed partially constructed kitten '{kittenName}'");
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

    /// <summary>
    /// Blocks until the game's background vehicle physics step (VehicleUpdateTask, kicked
    /// from Program.PrepareFrame and running concurrently with the UI pass) has finished.
    /// Constructing or disposing a Vehicle mutates the shared shapes registry, which the
    /// game locks while that step runs. PrepareFrame would wait on this same scheduler
    /// next frame anyway, so this only moves the wait earlier.
    /// </summary>
    private static void WaitForVehicleSolverIdle()
    {
        JobSystems.VehicleSolver?.Wait();
    }

    private string GenerateUniqueName(CelestialSystem system)
    {
        string name;
        do
        {
            name = $"Kitten_doh_{_nextKittenIndex++}";
        } while (system.All.TryGet(name, out _));
        return name;
    }
}
