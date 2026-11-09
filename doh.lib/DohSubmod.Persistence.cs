using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Brutal.Numerics;
using KSA;
using MeowSci.WarPaintLib;
using MeowSci.DohLib.Spawning;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.DohLib;

public sealed partial class DohSubmod
{
    private const int SpawnNameCapacity = 256;
    private static float3 DefaultSpawnOffset => new((float)SpawnRequest.DefaultOffsetBodyFrame.X,
        (float)SpawnRequest.DefaultOffsetBodyFrame.Y, (float)SpawnRequest.DefaultOffsetBodyFrame.Z);

    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return new SaveParticipant<SavedDoh>("doh", CaptureDoh, ResetDoh, RestoreDoh, order: 60, validate: ValidateDoh); }
    }

    private static void ValidateDoh(SavedDoh saved)
    {
        if (saved.SpawnName == null || saved.SpawnName.Contains('\0')
            || Encoding.UTF8.GetByteCount(saved.SpawnName) >= SpawnNameCapacity)
            throw new InvalidOperationException("Invalid DOH spawn name.");
        if (saved.PaintPresetName != null && (string.IsNullOrWhiteSpace(saved.PaintPresetName) || saved.PaintPresetName.Length > 128 || saved.PaintPresetName.Contains('\0')))
            throw new InvalidOperationException("Invalid DOH War Paint preset name.");
        saved.SpawnPaint?.Validate();
        if (saved.UseCustomColor) new PaintSettings { Tint = saved.TintColor }.Validate();
        if (saved.SpawnCount is < 1 or > 10000 || saved.Kittens == null || saved.Kittens.Count > 10000
            || saved.Kittens.Any(k => k == null || string.IsNullOrWhiteSpace(k.VehicleId) || string.IsNullOrWhiteSpace(k.CharacterId)
                || (k.MaterialGroup != null && string.IsNullOrWhiteSpace(k.MaterialGroup))
                || k.Materials == null || k.Materials.Count > 10000
                || k.Materials.Any(m => m == null || string.IsNullOrWhiteSpace(m.Name) || m.Source == null)))
            throw new InvalidOperationException("Invalid DOH kitten/material records.");
        if (saved.Kittens.Select(k => k.VehicleId).Distinct(StringComparer.Ordinal).Count() != saved.Kittens.Count)
            throw new InvalidOperationException("Duplicate saved DOH kitten identifiers.");
        foreach (var kitten in saved.Kittens)
        {
            if (kitten.Materials.Select(m => (m.Source, m.Name)).Distinct().Count() != kitten.Materials.Count)
                throw new InvalidOperationException("Duplicate saved DOH material identities.");
            new PaintSettings
            {
                Tint = kitten.Tint ?? float4.One,
                Materials = kitten.Materials.Select(m => new PaintMaterialSettings
                    { Name = m.Name, Source = m.Source, Color = m.Color }).ToList()
            }.Validate();
        }
    }

    private SavedDoh CaptureDoh()
    {
        var saved = new SavedDoh
        {
            Offset = _offset, SpawnCount = _spawnCount, UseCustomColor = _useCustomColor,
            SpawnName = _spawnName.ToString(),
            TintColor = _tintColor, UniquePerKitten = _uniquePerKitten,
            PaintPresetName = _paintPresetName, SpawnPaint = _spawnPaint?.Clone(),
            CharacterId = _selectedCharacterIndex >= 0 && _selectedCharacterIndex < _availableCharacters.Length
                ? _availableCharacters[_selectedCharacterIndex] : null
        };
        if (_registry == null) return saved;
        foreach (var entry in _registry.GetAll())
        {
            if ((entry.Vehicle ?? VehicleProvider.FindVehicle(entry.KittenId)) is not KittenEva kitten || kitten.IsDisposed) continue;
            saved.Kittens.Add(new()
            {
                VehicleId = kitten.Id, CharacterId = entry.CharacterId
                // Active paint is now captured once by War Paint's own scene-save participant.
            });
        }
        return saved;
    }

    private void ResetDoh()
    {
        // Native load owns kitten destruction and War Paint owns material cleanup/rebinding.
        _registry?.Clear();
        _selectedVehicle = null;
        _offset = DefaultSpawnOffset;
        _spawnCount = 1;
        _spawnName.SetValue(SpawnRequest.DefaultNamePrefix.AsSpan());
        _useCustomColor = _uniquePerKitten = false;
        _tintColor = new float4(1, 1, 1, 1);
        _selectedCharacterIndex = -1;
        _paintPresetName = null;
        _spawnPaint = null;
    }

    private void RestoreDoh(SavedDoh saved, SaveRestoreContext context)
    {
        context.Require(saved.SpawnCount >= 1 && saved.SpawnCount <= 10000, "Invalid DOH spawn count.");
        _offset = saved.Offset; _spawnCount = saved.SpawnCount;
        _spawnName.SetValue(saved.SpawnName.AsSpan());
        _useCustomColor = saved.UseCustomColor; _uniquePerKitten = saved.UniquePerKitten;
        _tintColor = saved.TintColor;
        _selectedCharacterIndex = Array.IndexOf(_availableCharacters, saved.CharacterId);
        _paintPresetName = saved.PaintPresetName;
        _spawnPaint = saved.SpawnPaint?.Clone();
        if (_spawnPaint == null && _paintPresetName != null
            && WarPaintService.Shared.Presets.TryGet(_paintPresetName, out var selected))
            _spawnPaint = selected.Clone();
        if (_spawnPaint == null && _paintPresetName == null && saved.UseCustomColor)
        {
            // Preserve legacy spawn appearance as a reusable named preset. The original flags
            // remain in v1 so unused tint input and the old unique/shared choice are not discarded.
            _spawnPaint = new PaintSettings { Tint = saved.TintColor };
            _paintPresetName = ImportLegacySpawnPreset(_spawnPaint, context);
        }
        if (_paintPresetName != null && _spawnPaint == null)
            context.Warn($"DOH spawn preset unavailable: {_paintPresetName}.");

        foreach (var item in saved.Kittens)
        {
            if (VehicleProvider.FindVehicle(item.VehicleId) is not KittenEva kitten || kitten.IsDisposed)
            { context.Warn($"DOH kitten missing: {item.VehicleId}."); continue; }
            if (kitten.Character.Id != item.CharacterId)
            { context.Warn($"DOH kitten character changed: {item.VehicleId}."); continue; }
            try { KittenSpawner.EnsureRoster(kitten); }
            catch (Exception ex)
            {
                context.Warn($"DOH roster unavailable for {item.VehicleId}: {ex.Message}");
                continue;
            }
            if (item.Tint.HasValue || item.Materials.Count > 0)
            {
                // MaterialGroup represented shared mutable GPU slots. Import the effective recipe
                // independently for every kitten; central War Paint never aliases kitten ownership.
                var paint = new PaintSettings
                {
                    Tint = item.Tint ?? float4.One,
                    Materials = item.Materials.Select(m => new PaintMaterialSettings
                        { Name = m.Name, Source = m.Source, Color = m.Color }).ToList()
                };
                WarPaintService.Shared.ApplyLegacy(kitten, paint, context);
            }
            _registry?.Register(kitten.Id, item.CharacterId, null);
        }
    }

    private static string ImportLegacySpawnPreset(PaintSettings paint, SaveRestoreContext context)
    {
        float4 tint = paint.Tint;
        string baseName = $"DOH legacy tint {BitConverter.SingleToInt32Bits(tint.X):X8}{BitConverter.SingleToInt32Bits(tint.Y):X8}" +
            $"{BitConverter.SingleToInt32Bits(tint.Z):X8}{BitConverter.SingleToInt32Bits(tint.W):X8}";
        var presets = WarPaintService.Shared.Presets;
        string name = baseName;
        for (int i = 1; presets.TryGet(name, out var existing); i++)
        {
            if (existing.Tint.Equals(paint.Tint) && existing.Materials.Count == 0) return name;
            name = $"{baseName} ({i})";
        }
        if (!presets.Save(name, paint))
            context.Warn($"DOH legacy spawn preset could not be saved: {presets.LastError}. Detached spawn recipe retained.");
        return name;
    }

    public sealed class SavedDoh
    {
        public float3 Offset { get; set; } = DefaultSpawnOffset;
        public int SpawnCount { get; set; } = 1;
        /// <summary>Additive v1 field; legacy records restore the default name/prefix.</summary>
        public string SpawnName { get; set; } = SpawnRequest.DefaultNamePrefix;
        public bool UseCustomColor { get; set; }
        public bool UniquePerKitten { get; set; }
        public float4 TintColor { get; set; }
        public string? CharacterId { get; set; }
        /// <summary>Additive v1 selector and detached recipe; absent in older DOH records.</summary>
        public string? PaintPresetName { get; set; }
        public PaintSettings? SpawnPaint { get; set; }
        public List<SavedKitten> Kittens { get; set; } = new();
    }

    public sealed class SavedKitten
    {
        public string VehicleId { get; set; } = "";
        public string CharacterId { get; set; } = "";
        public float4? Tint { get; set; }
        /// <summary>
        /// Legacy sharing identity, accepted and validated during migration. War Paint imports each
        /// kitten independently while preserving its effective tint and named material colors.
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
