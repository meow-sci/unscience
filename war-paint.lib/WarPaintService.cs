using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.WarPaintLib.Materials;

namespace MeowSci.WarPaintLib;

/// <summary>Single owner of all per-kitten paint. Call on the game thread before rendering.</summary>
public sealed partial class WarPaintService
{
    public static WarPaintService Shared { get; } = new(new NativeKittenPaintBackend(), new PaintPresetLibrary());
    private readonly IKittenPaintBackend _backend;
    private readonly Dictionary<KittenEva, ActivePaint> _active = new();
    private readonly Dictionary<string, KittenMaterialSet> _detached = new(StringComparer.Ordinal);
    private bool _shutdown;
    public PaintPresetLibrary Presets { get; }
    public string? LastError { get; private set; }
    public int SceneRevision { get; private set; }

    public WarPaintService(IKittenPaintBackend backend, PaintPresetLibrary presets)
    {
        _backend = backend; Presets = presets;
    }

    public bool Initialize()
    {
        _shutdown = false;
        return _backend.Initialize();
    }

    public IReadOnlyList<ActivePaint> GetActivePaints() => _active.Values
        .Where(p => IsCurrent(p.Kitten)).OrderBy(p => p.VehicleId, StringComparer.Ordinal).ToArray();

    public bool TryGetPaint(KittenEva kitten, out ActivePaint paint)
    {
        if (IsCurrent(kitten)) return _active.TryGetValue(kitten, out paint!);
        paint = null!; return false;
    }
    public ActivePaint? Apply(KittenEva kitten, float4 tint) => Apply(kitten, new PaintSettings { Tint = tint });
    public ActivePaint? Apply(KittenEva kitten, PaintSettings settings) => Apply(kitten, settings, null);

    /// <summary>Apply a named recipe. Legacy callers without a name import a reusable preset first.</summary>
    public ActivePaint? Apply(KittenEva kitten, PaintSettings settings, string? presetName)
    {
        LastError = null;
        try
        {
            settings.Validate();
            if (_shutdown || !IsCurrent(kitten))
                return Fail("The selected kitten is no longer a unique live target.");
            presetName ??= ImportPreset(settings, $"Imported paint - {kitten.Id}");
            if (presetName == null) return Fail(Presets.LastError ?? "Could not create a named paint preset.");
            if (!Presets.TryGet(presetName, out _)) return Fail($"Paint preset unavailable: {presetName}.");
            if (_active.TryGetValue(kitten, out var existing))
            {
                if (!ApplySettings(existing, settings)) return null;
                existing.PresetName = presetName;
                return existing;
            }
            var bindings = _backend.CaptureBindings(kitten);
            if (bindings == null) return Fail("No kitten material bindings are available.");
            string characterId = kitten.Character.Id;
            string key = CacheKey(kitten.Id, characterId);
            _detached.TryGetValue(key, out var reusable);
            var set = _backend.CreateMaterials(bindings, characterId, settings.Tint, reusable);
            if (set == null) return Fail("Private kitten materials are unavailable; the GPU material pool may be full.");
            // Claim the cache entry only when it actually supplied this target's set.
            if (ReferenceEquals(set, reusable)) _detached.Remove(key);
            var paint = new ActivePaint(kitten, characterId, presetName, set, bindings);
            try
            {
                if (!ApplySettings(paint, settings))
                {
                    _backend.Release(set);
                    return null;
                }
                _backend.Bind(bindings, set);
                _active.Add(kitten, paint);
                return paint;
            }
            catch
            {
                _backend.Restore(bindings, set);
                _backend.Release(set);
                throw;
            }
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private ActivePaint? Fail(string message) { LastError = message; return null; }

    private bool ApplySettings(ActivePaint paint, PaintSettings settings)
    {
        if (!_backend.CanReuse(paint.MaterialSet))
        { LastError = "The kitten's private materials are no longer available."; return false; }
        var entries = new List<(MaterialEntry Entry, float4 Color)>();
        foreach (var material in settings.Materials)
        {
            var matches = paint.MaterialSet.Materials.Where(m => m.Source == material.Source
                && (material.Slot >= 0 ? m.Slot == material.Slot : m.Name == material.Name)).ToArray();
            if (matches.Length != 1)
            {
                LastError = $"Material unavailable: {material.Source}/{material.Name}.";
                return false;
            }
            entries.Add((matches[0], material.Color));
        }
        if (!_backend.SetTint(paint.MaterialSet, settings.Tint))
        { LastError = "Could not upload the kitten tint."; return false; }
        foreach (var (entry, color) in entries)
            if (!_backend.SetColor(entry, color))
            { LastError = $"Could not upload material: {entry.Name}."; return false; }
        return true;
    }

    public bool UpdateTint(KittenEva kitten, float4 tint)
    {
        if (!PaintSettings.Finite(tint) || _shutdown || !IsCurrent(kitten)
            || !_active.TryGetValue(kitten, out var paint) || !_backend.CanReuse(paint.MaterialSet)) return false;
        bool success = _backend.SetTint(paint.MaterialSet, tint);
        LastError = success ? null : "Could not upload the kitten tint.";
        return success;
    }

    public bool UpdateMaterial(KittenEva kitten, string source, string name, float4 color)
    {
        if (!PaintSettings.Finite(color) || _shutdown || !IsCurrent(kitten)
            || !_active.TryGetValue(kitten, out var paint) || !_backend.CanReuse(paint.MaterialSet)) return false;
        var matches = paint.MaterialSet.Materials.Where(m => m.Source == source && m.Name == name).ToArray();
        bool success = matches.Length == 1 && _backend.SetColor(matches[0], color);
        LastError = success ? null : $"Could not upload material: {source}/{name}.";
        return success;
    }

    public PaintSettings? Snapshot(KittenEva kitten)
    {
        if (!_active.TryGetValue(kitten, out var paint) || !IsCurrent(kitten)) return null;
        return new PaintSettings
        {
            Tint = paint.MaterialSet.TintColor,
            Materials = paint.MaterialSet.Materials.Select(m => new PaintMaterialSettings
                { Source = m.Source, Name = m.Name, Slot = m.Slot, Color = MaterialColorState.GetOrDefault(m.Handle, m.Color) }).ToList()
        };
    }

    /// <summary>Detach original live bindings before returning the private GPU slots.</summary>
    public bool Unset(KittenEva kitten)
    {
        if (!_active.TryGetValue(kitten, out var paint)) return false;
        if (!kitten.IsDisposed) _backend.Restore(paint.Bindings, paint.MaterialSet);
        _active.Remove(kitten);
        if (!_shutdown) _backend.Release(paint.MaterialSet);
        return true;
    }

    /// <summary>Prune deleted kittens and release safe cache entries after native scene replacement.</summary>
    public void Update()
    {
        if (_shutdown) return;
        foreach (var paint in _active.Values.Where(p => !IsCurrent(p.Kitten)).ToArray()) Unset(paint.Kitten);
        ReleaseDetached();
    }

    public void Cleanup()
    {
        if (_shutdown) return;
        foreach (var kitten in _active.Keys.ToArray()) Unset(kitten);
        ReleaseDetached();
        _shutdown = true;
        MaterialSystemAccessor.Cleanup();
    }

    private void ReleaseDetached()
    {
        foreach (var set in _detached.Values.Distinct()) _backend.Release(set);
        _detached.Clear();
    }

    private static string CacheKey(string vehicleId, string characterId) => vehicleId + "\n" + characterId;
    private static bool IsCurrent(KittenEva kitten) => !kitten.IsDisposed
        && ReferenceEquals(VehicleProvider.FindVehicle(kitten.Id), kitten);
}
