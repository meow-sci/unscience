using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Brutal;
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
/// MUST be called on the game thread (not from HTTP handlers directly).
/// </summary>
public sealed class KittenSpawner
{
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

    /// <summary>Despawns a kitten by ID.</summary>
    public bool Despawn(string kittenId)
    {
        try
        {
            var entry = _registry.Get(kittenId);
            if (entry == null) return false;

            var system = Universe.CurrentSystem;
            if (system == null) return false;

            // Find the kitten in the system
            Astronomical? astro;
            if (!system.All.TryGet(kittenId, out astro))
                return false;

            if (astro is Vehicle vehicle)
            {
                // Match game's two-phase removal (EVADoor ingress / docking pattern).
                // Dispose releases physics shapes (same registry lock as spawn), so the
                // vehicle solver must be idle first.
                WaitForVehicleSolverIdle();
                Universe.CurrentSystem?.Deregister(vehicle);
                vehicle.Dispose();
            }

            _registry.Unregister(kittenId);
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

        // Spawn loop
        var results = new List<SpawnedKittenInfo>();

        for (int i = 0; i < request.Count; i++)
        {
            // Generate unique name
            string kittenName = GenerateUniqueName(system);

            // Create backpack part
            Part? backpackPart = CreateBackpackPart();
            if (backpackPart == null)
                return SpawnResult.Failure("Failed to create backpack part (KittenBackPackPart not found).");

            // Calculate this kitten's position offset
            double3 chainOffset = pos.OffsetCci * (i + 1);
            double3 kittenPosCci = pos.BasePositionCci + chainOffset;
            double3 kittenVelCci = pos.VelocityCci;

            // Create KittenEva. The Vehicle ctor mutates the BepuPhysics shapes registry
            // (BepuHandles.Create -> ConstraintSim.UnlockShapes), which throws if the
            // background vehicle physics step is still running. Wait for it to go idle.
            WaitForVehicleSolverIdle();
            var kittenEva = new KittenEva(
                system,
                characterId,
                pos.Body2Cce,
                pos.BodyRates,
                pos.Parent!,
                kittenName,
                backpackPart,
                pos.ReferenceOrbit!);

            // Create orbit and teleport
            var simTime = Universe.GetElapsedTime();
            var orbitColor = pos.ReferenceOrbit?.OrbitLineColor ?? new byte4(255, 200, 0, 255);
            var orbit = Orbit.CreateFromStateCci(
                pos.Parent!, simTime, kittenPosCci, kittenVelCci, orbitColor);
            kittenEva.Teleport(orbit, null, null);

            // Register with parent body
            pos.Parent!.Children.Add(kittenEva);
            kittenEva.UpdatePerFrameData();

            // Apply custom materials — clone every material this kitten uses
            KittenMaterialSet? matSet = null;
            if (request.TintColor.HasValue || request.PerKittenColors != null)
            {
                float4 color = float4.One;
                if (request.PerKittenColors != null && i < request.PerKittenColors.Length)
                    color = request.PerKittenColors[i];
                else if (request.TintColor.HasValue)
                    color = request.TintColor.Value;

                matSet = ApplyClonedMaterials(kittenEva, color, characterId);
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
            SpawnedKittens = results.ToArray()
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

    private PositionResult ResolvePositioning(SpawnRequest request)
    {
        if (!string.IsNullOrEmpty(request.ReferenceVehicleId))
            return ResolveVehicleRelative(request);
        if (request.PositionCci.HasValue && request.VelocityCci.HasValue)
            return ResolveAbsolute(request);

        return new PositionResult { Error = "Either ReferenceVehicleId or PositionCci+VelocityCci required." };
    }

    private PositionResult ResolveVehicleRelative(SpawnRequest request)
    {
        var vehicles = VehicleProvider.GetAllVehicles();
        var refVehicle = vehicles.FirstOrDefault(v => v.Id == request.ReferenceVehicleId);
        if (refVehicle == null)
            return new PositionResult { Error = $"Vehicle '{request.ReferenceVehicleId}' not found." };

        var sv = refVehicle.Orbit.StateVectors;
        var body2Cci = refVehicle.GetAsmb2Cci();
        var offsetCci = request.OffsetBodyFrame.Transform(body2Cci);

        return new PositionResult
        {
            BasePositionCci = sv.PositionCci,
            OffsetCci = offsetCci,
            VelocityCci = sv.VelocityCci,
            Body2Cce = refVehicle.Body2Cce,
            BodyRates = SafeBodyRates(refVehicle.BodyRates),
            Parent = refVehicle.Parent,
            ReferenceOrbit = refVehicle.Orbit
        };
    }

    private PositionResult ResolveAbsolute(SpawnRequest request)
    {
        if (string.IsNullOrEmpty(request.ParentBodyName))
            return new PositionResult { Error = "ParentBodyName required for absolute positioning." };

        var celestials = CelestialProvider.GetAllCelestials();
        var parent = celestials.FirstOrDefault(c => c.Id == request.ParentBodyName);
        if (parent == null)
            return new PositionResult { Error = $"Celestial body '{request.ParentBodyName}' not found." };

        // Create a reference orbit from the absolute position
        var simTime = Universe.GetElapsedTime();
        var tempOrbit = Orbit.CreateFromStateCci(
            parent, simTime, request.PositionCci!.Value, request.VelocityCci!.Value, new byte4(255, 200, 0, 255));

        return new PositionResult
        {
            BasePositionCci = request.PositionCci!.Value,
            OffsetCci = double3.Zero,
            VelocityCci = request.VelocityCci!.Value,
            Body2Cce = doubleQuat.Identity,
            BodyRates = double3.Zero,
            Parent = parent,
            ReferenceOrbit = tempOrbit
        };
    }

    private Part? CreateBackpackPart()
    {
        var partTemplate = FindPartTemplate("KittenBackPackPart");
        if (partTemplate == null) return null;

        var part = new Part(partTemplate.Id, partTemplate);
        part.Tree.ReinitializeDerivedValues();

        var mix = TryGetReactantMix("MMH_NTO");
        if (mix != null)
        {
            var tanks = part.SubtreeModules.Get<Tank>();
            for (int i = 0; i < tanks.Length; i++)
                tanks[i].ConfigureFor(mix);
        }

        part.Tree.RefillConsumables();
        return part;
    }

    /// <summary>
    /// Resolves a propellant mix by reaction id.
    ///
    /// KSA 2026.7.9.5018 replaced the combustion-process model
    /// (SubstanceLibrary.TryGetCombustionProcess, ids like "MMH_NTO_1.6") with a
    /// reaction model: mixture ratio is no longer baked into the id, so a
    /// MixtureReaction must be evaluated at its default ratio to obtain a mix.
    /// </summary>
    private static ReactantMix? TryGetReactantMix(string reactionId)
    {
        var reaction = SubstanceLibrary.TryGetReaction(KeyHash.Make(reactionId.AsSpan()));
        return reaction switch
        {
            MixtureReaction mixture => mixture.AtMixtureRatio(mixture.DefaultMixtureRatio).ReactantMix,
            IReactantMix fixedMix => fixedMix.ReactantMix,
            _ => null,
        };
    }

    private string GetRandomCharacterId()
    {
        var characters = GetAllCharacterReferences();
        if (characters.Count == 0) return "";
        return characters[Random.Shared.Next(characters.Count)].Id;
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

    private static double3 SafeBodyRates(double3 rates)
    {
        if (double.IsNaN(rates.X) || double.IsNaN(rates.Y) || double.IsNaN(rates.Z))
            return double3.Zero;
        return rates;
    }

    /// <summary>Finds a PartTemplate by name via reflection on ModLibrary.AllParts (internal).</summary>
    private static PartTemplate? FindPartTemplate(string partName)
    {
        try
        {
            var field = typeof(ModLibrary).GetField("AllParts",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) return null;

            var collection = field.GetValue(null);
            if (collection == null) return null;

            var findMethod = collection.GetType().GetMethod("Find");
            if (findMethod == null) return null;

            var keyHash = KeyHash.Make(partName.AsSpan());
            return findMethod.Invoke(collection, new object[] { keyHash }) as PartTemplate;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: FindPartTemplate error: {ex.Message}");
            return null;
        }
    }

    /// <summary>Gets all CharacterReference entries via reflection on ModLibrary.AllCharacters (internal).</summary>
    private static List<CharacterReference> GetAllCharacterReferences()
    {
        try
        {
            var field = typeof(ModLibrary).GetField("AllCharacters",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) return new List<CharacterReference>();

            var collection = field.GetValue(null);
            if (collection == null) return new List<CharacterReference>();

            var getListMethod = collection.GetType().GetMethod("GetList");
            if (getListMethod == null) return new List<CharacterReference>();

            return getListMethod.Invoke(collection, null) as List<CharacterReference> ?? new List<CharacterReference>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: GetAllCharacterReferences error: {ex.Message}");
            return new List<CharacterReference>();
        }
    }

    /// <summary>
    /// Creates unique per-kitten materials by cloning every material the kitten uses
    /// (character model, fur, helmet, visor, MMU, cosmetics), then replaces all
    /// MaterialIndices entries with the cloned handles.
    /// Returns the KittenMaterialSet, or null on failure.
    /// </summary>
    internal KittenMaterialSet? ApplyClonedMaterials(KittenEva kittenEva, float4 tintColor, string characterId,
        KittenMaterialSet? reusable = null)
    {
        try
        {
            // Navigate to CharacterAvatar
            var avatar = GetCharacterAvatar(kittenEva);
            if (avatar == null)
            {
                Console.WriteLine("doh: Failed to get CharacterAvatar from KittenEva.");
                return null;
            }

            // Collect MaterialIndices arrays from all renderables
            var renderables = new List<(string name, int[] indices)>();

            // 1. CharacterModel (body/head/eyes)
            var charModelIndices = GetMaterialIndicesFromPath(avatar, "Core", "CharacterModel");
            if (charModelIndices != null)
                renderables.Add(("CharacterModel", charModelIndices));

            // 2. Fur — collected but cloned separately (needs special ExtraData for fur shader)
            var furIndices = GetMaterialIndicesFromPath(avatar, "Fur", "CatFurRenderable");
            if (furIndices != null)
                renderables.Add(("Fur", furIndices));

            // 3. Helmet
            var helmetIndices = GetMaterialIndicesFromPath(avatar, "Attachments", "Helmet", "HelmetMesh");
            if (helmetIndices != null)
                renderables.Add(("Helmet", helmetIndices));

            // 4. Visor
            var visorIndices = GetMaterialIndicesFromPath(avatar, "Attachments", "Helmet", "VisorMesh");
            if (visorIndices != null)
                renderables.Add(("Visor", visorIndices));

            // 5. MMU
            var mmuIndices = GetMaterialIndicesFromPath(avatar, "Attachments", "Mmu", "MmuMesh");
            if (mmuIndices != null)
                renderables.Add(("MMU", mmuIndices));

            if (renderables.Count == 0)
            {
                Console.WriteLine("doh: No MaterialIndices found on any renderable.");
                return null;
            }

            // Separate fur handles — fur shader needs ExtraData with fur texture handles
            var furHandleSet = furIndices != null ? new HashSet<int>(furIndices) : new HashSet<int>();
            var nonFurHandles = renderables
                .Where(r => r.name != "Fur")
                .SelectMany(r => r.indices)
                .ToArray();

            Console.WriteLine($"doh: Found {renderables.Count} renderables, {nonFurHandles.Distinct().Count()} non-fur + {furHandleSet.Count} fur unique handles");

            // Clone non-fur materials via PbrMaterialReference lookup
            // The global material system survives ordinary loads. Reuse our private slots
            // when the native shared material identity still matches, avoiding allocation
            // growth on repeated A -> B -> A loads.
            var matSet = reusable != null && nonFurHandles.Where(h => h >= 0).All(reusable.HandleMap.ContainsKey)
                ? reusable : _materialFactory.CloneAllMaterials(nonFurHandles, tintColor);
            if (matSet == null) return null;
            if (ReferenceEquals(matSet, reusable)) matSet.UpdateTint(tintColor);

            // Clone fur materials with proper ExtraData (FurTexture, FurSampler, FurMask)
            if (furIndices != null && furIndices.Length > 0)
            {
                foreach (int oldHandle in furIndices.Distinct())
                {
                    if (oldHandle < 0 || matSet.HandleMap.ContainsKey(oldHandle)) continue;
                    int newHandle = _materialFactory.CreateClonedFurMaterial(
                        matSet.Id, characterId, tintColor);
                    if (newHandle >= 0)
                    {
                        matSet.HandleMap[oldHandle] = newHandle;
                        matSet.AllMaterialHandles.Add(newHandle);
                        matSet.Materials.Add(new MaterialEntry
                        {
                            Name = "CatFur",
                            Source = "Fur",
                            Handle = newHandle,
                            Color = tintColor
                        });
                        Console.WriteLine($"doh:   Fur: cloned handle {oldHandle} → {newHandle}");
                    }
                    else
                    {
                        Console.WriteLine($"doh:   Fur: failed to clone handle {oldHandle}");
                    }
                }
            }

            // Replace entries in each renderable's MaterialIndices using HandleMap
            int totalReplacements = 0;
            foreach (var (name, indices) in renderables)
            {
                int replaced = 0;
                for (int i = 0; i < indices.Length; i++)
                {
                    if (matSet.HandleMap.TryGetValue(indices[i], out int newHandle))
                    {
                        indices[i] = newHandle;
                        replaced++;
                    }
                }
                if (replaced > 0)
                    Console.WriteLine($"doh:   {name}: {replaced}/{indices.Length} slots replaced");
                totalReplacements += replaced;
            }

            // Tag Materials entries with their source renderable name
            var newHandleToSource = new Dictionary<int, string>();
            foreach (var (name, indices) in renderables)
            {
                foreach (int h in indices)
                {
                    if (!newHandleToSource.ContainsKey(h))
                        newHandleToSource[h] = name;
                }
            }
            foreach (var mat in matSet.Materials)
            {
                if (string.IsNullOrEmpty(mat.Source) && newHandleToSource.TryGetValue(mat.Handle, out var src))
                    mat.Source = src;
            }

            Console.WriteLine($"doh: Applied cloned materials '{matSet.Id}' ({totalReplacements} replacements, {matSet.AllMaterialHandles.Count} unique cloned handles)");
            return matSet;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: ApplyClonedMaterials error: {ex.Message}");
            return null;
        }
    }

    /// <summary>Gets the CharacterAvatar from a KittenEva via reflection.</summary>
    private static object? GetCharacterAvatar(KittenEva kittenEva)
    {
        var renderableField = typeof(KittenEva).GetField("_renderable",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (renderableField == null) return null;

        var renderable = renderableField.GetValue(kittenEva);
        if (renderable == null) return null;

        var avatarField = renderable.GetType().GetField("_characterAvatar",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return avatarField?.GetValue(renderable);
    }

    /// <summary>
    /// Navigates a chain of fields on an object and extracts MaterialIndices from the final renderable.
    /// E.g., GetMaterialIndicesFromPath(avatar, "Core", "CharacterModel") navigates
    /// avatar.Core.CharacterModel.MaterialIndices.
    /// </summary>
    private static int[]? GetMaterialIndicesFromPath(object root, params string[] fieldPath)
    {
        object? current = root;
        foreach (string fieldName in fieldPath)
        {
            if (current == null) return null;
            current = GetFieldValue(current, fieldName);
        }

        if (current == null) return null;

        // Get MaterialIndices from the final renderable
        var matField = FindFieldInHierarchy(current.GetType(), "MaterialIndices");
        return matField?.GetValue(current) as int[];
    }

    /// <summary>Gets a field value by name, searching public and non-public fields.</summary>
    private static object? GetFieldValue(object instance, string name)
    {
        var field = instance.GetType().GetField(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(instance);
    }

    private static FieldInfo? FindFieldInHierarchy(Type? type, string fieldName)
    {
        while (type != null)
        {
            var field = type.GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null) return field;
            type = type.BaseType;
        }
        return null;
    }

    private class PositionResult
    {
        public double3 BasePositionCci;
        public double3 OffsetCci;
        public double3 VelocityCci;
        public doubleQuat Body2Cce;
        public double3 BodyRates;
        public IParentBody? Parent;
        public Orbit? ReferenceOrbit;
        public string? Error;
    }
}
