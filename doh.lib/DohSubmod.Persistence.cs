using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.DohLib.Materials;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.DohLib;

public sealed partial class DohSubmod
{
    private readonly Dictionary<string, KittenMaterialSet> _savedMaterialCache = new(StringComparer.Ordinal);

    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return new SaveParticipant<SavedDoh>("doh", CaptureDoh, ResetDoh, RestoreDoh, order: 60, validate: ValidateDoh); }
    }

    private static void ValidateDoh(SavedDoh saved)
    {
        if (saved.SpawnCount is < 1 or > 10000 || saved.Kittens == null || saved.Kittens.Count > 10000
            || saved.Kittens.Any(k => k == null || string.IsNullOrWhiteSpace(k.VehicleId) || string.IsNullOrWhiteSpace(k.CharacterId)
                || (k.MaterialGroup != null && string.IsNullOrWhiteSpace(k.MaterialGroup))
                || k.Materials == null || k.Materials.Count > 10000
                || k.Materials.Any(m => m == null || string.IsNullOrWhiteSpace(m.Name) || m.Source == null)))
            throw new InvalidOperationException("Invalid DOH kitten/material records.");
        if (saved.Kittens.Select(k => k.VehicleId).Distinct(StringComparer.Ordinal).Count() != saved.Kittens.Count)
            throw new InvalidOperationException("Duplicate saved DOH kitten identifiers.");
        foreach (var kitten in saved.Kittens)
            if (kitten.Materials.Select(m => (m.Source, m.Name)).Distinct().Count() != kitten.Materials.Count)
                throw new InvalidOperationException("Duplicate saved DOH material identities.");
    }

    private SavedDoh CaptureDoh()
    {
        var saved = new SavedDoh
        {
            Offset = _offset, SpawnCount = _spawnCount, UseCustomColor = _useCustomColor,
            TintColor = _tintColor, UniquePerKitten = _uniquePerKitten,
            CharacterId = _selectedCharacterIndex >= 0 && _selectedCharacterIndex < _availableCharacters.Length
                ? _availableCharacters[_selectedCharacterIndex] : null
        };
        if (_registry == null) return saved;
        foreach (var entry in _registry.GetAll())
        {
            if ((entry.Vehicle ?? VehicleProvider.FindVehicle(entry.KittenId)) is not KittenEva kitten || kitten.IsDisposed) continue;
            saved.Kittens.Add(new()
            {
                VehicleId = kitten.Id, CharacterId = entry.CharacterId, Tint = entry.MaterialSet?.TintColor,
                MaterialGroup = entry.MaterialSet?.Id,
                Materials = entry.MaterialSet?.Materials.Select(m => new SavedMaterial
                    { Name = m.Name, Source = m.Source, Color = MaterialColorState.GetOrDefault(m.Handle, m.Color) }).ToList() ?? new()
            });
        }
        return saved;
    }

    private void ResetDoh()
    {
        // Native load owns kitten destruction. Keep only detached GPU recipes/slots, and
        // never call DespawnAll here or create replacement kittens during restore.
        if (_registry != null)
            foreach (var entry in _registry.GetAll())
                if (entry.MaterialSet != null)
                    _savedMaterialCache[CacheKey(entry.Vehicle?.Id ?? entry.KittenId, entry.CharacterId)] = entry.MaterialSet;
        _registry?.Clear();
        _selectedVehicle = null;
        _offset = new float3(0, 0, 10);
        _spawnCount = 1;
        _useCustomColor = _uniquePerKitten = false;
        _tintColor = new float4(1, 1, 1, 1);
        _selectedCharacterIndex = -1;
    }

    private void RestoreDoh(SavedDoh saved, SaveRestoreContext context)
    {
        context.Require(saved.SpawnCount >= 1 && saved.SpawnCount <= 10000, "Invalid DOH spawn count.");
        _offset = saved.Offset; _spawnCount = saved.SpawnCount;
        _useCustomColor = saved.UseCustomColor; _uniquePerKitten = saved.UniquePerKitten;
        _tintColor = saved.TintColor;
        _selectedCharacterIndex = Array.IndexOf(_availableCharacters, saved.CharacterId);
        var groupSets = new Dictionary<string, KittenMaterialSet>(StringComparer.Ordinal);
        var claimed = new HashSet<KittenMaterialSet>();
        foreach (var item in saved.Kittens)
        {
            if (VehicleProvider.FindVehicle(item.VehicleId) is not KittenEva kitten || kitten.IsDisposed)
            { context.Warn($"DOH kitten missing: {item.VehicleId}."); continue; }
            KittenMaterialSet? set = null;
            if (item.Tint.HasValue)
            {
                var reusable = FindReusableSet(item, groupSets, claimed);
                set = _spawner?.ApplyClonedMaterials(kitten, item.Tint.Value, item.CharacterId, reusable);
                if (set == null) context.Warn($"DOH materials unavailable for {item.VehicleId}.");
                else
                {
                    foreach (var material in item.Materials)
                    {
                        var matches = set.Materials.Where(m => m.Name == material.Name && m.Source == material.Source).ToArray();
                        if (matches.Length != 1) { context.Warn($"DOH material unresolved: {item.VehicleId}/{material.Source}/{material.Name}."); continue; }
                        matches[0].Color = material.Color;
                        if (!matches[0].ApplyColor()) context.Warn($"DOH material upload failed: {material.Name}.");
                    }
                    claimed.Add(set);
                    if (item.MaterialGroup != null) groupSets[item.MaterialGroup] = set;
                }
            }
            _registry?.Register(kitten.Id, item.CharacterId, set);
        }
        ReleaseStaleSavedMaterials();
    }

    private static string CacheKey(string vehicleId, string characterId) => vehicleId + "\n" + characterId;

    /// <summary>
    /// A kitten reuses its save group's set (a batch that shared materials), else its own detached
    /// set from the previous world. A detached set is claimed at most once, so kittens that were
    /// unique in the save are never merged just because they shared a set in the previous world.
    /// </summary>
    private KittenMaterialSet? FindReusableSet(SavedKitten item,
        Dictionary<string, KittenMaterialSet> groupSets, HashSet<KittenMaterialSet> claimed)
    {
        if (item.MaterialGroup != null && groupSets.TryGetValue(item.MaterialGroup, out var shared))
            return shared;
        return _savedMaterialCache.TryGetValue(CacheKey(item.VehicleId, item.CharacterId), out var cached)
            && !cached.IsReleased && !claimed.Contains(cached) ? cached : null;
    }

    /// <summary>
    /// Returns detached material sets that no restored kitten rebound to the fixed-size GPU pool.
    /// Runs after restore, and from Update to cover loads/new scenes with no DOH record (reset
    /// without restore). Their kittens were destroyed by the native load, so nothing renders them.
    /// </summary>
    private void ReleaseStaleSavedMaterials()
    {
        if (_savedMaterialCache.Count == 0) return;
        foreach (var set in _savedMaterialCache.Values.Distinct().ToList())
        {
            if (_registry == null || !_registry.IsMaterialSetInUse(set))
                _materialFactory?.Release(set);
        }
        _savedMaterialCache.Clear();
    }

    public sealed class SavedDoh
    {
        public float3 Offset { get; set; }
        public int SpawnCount { get; set; } = 1;
        public bool UseCustomColor { get; set; }
        public bool UniquePerKitten { get; set; }
        public float4 TintColor { get; set; }
        public string? CharacterId { get; set; }
        public List<SavedKitten> Kittens { get; set; } = new();
    }

    public sealed class SavedKitten
    {
        public string VehicleId { get; set; } = "";
        public string CharacterId { get; set; } = "";
        public float4? Tint { get; set; }
        /// <summary>
        /// Kittens with the same group shared one cloned material set when saved. Optional: absent in
        /// saves from before sharing existed, which restore every kitten with its own set.
        /// </summary>
        public string? MaterialGroup { get; set; }
        public List<SavedMaterial> Materials { get; set; } = new();
    }

    public sealed class SavedMaterial
    {
        public string Name { get; set; } = "";
        public string Source { get; set; } = "";
        public float4 Color { get; set; }
    }
}
