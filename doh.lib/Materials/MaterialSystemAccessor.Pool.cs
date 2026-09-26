using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Core;
using KSA;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.DohLib.Materials;

/// <summary>
/// GPU material pool accounting and release.
///
/// KSA's GpuMaterialSystem is a fixed-size pool (512 slots at KSA 2026.9.22.5482,
/// <c>Program.cs</c> <c>new GpuMaterialSystem(..., 512, ...)</c>) that never grows. When it is full,
/// every allocation throws, including the game's own per-kitten fur material in the
/// <c>KittenEva</c> constructor. doh therefore keeps a reserve free for the game and releases its
/// cloned materials when their kittens go away.
/// </summary>
public static partial class MaterialSystemAccessor
{
    /// <summary>Slots doh never allocates into, so the game's own material loads keep working.</summary>
    public const int ReservedSlots = 64;

    /// <summary>Planning estimate for one kitten's cloned set (stock kittens: 8 non-fur + 1 fur).</summary>
    public const int EstimatedSlotsPerSet = 10;

    /// <summary>Stock pool size, used only when the allocator cannot be reflected.</summary>
    private const int DefaultCapacity = 512;

    private static FieldInfo? _bigBufferAllocatorField;

    /// <summary>Total material slots in the game's GPU pool, or -1 when not initialized.</summary>
    public static int GetCapacity()
    {
        if (_materialSystem == null) return -1;
        try
        {
            var allocator = _bigBufferAllocatorField?.GetValue(_materialSystem);
            var capacity = allocator?.GetType().GetProperty("Capacity")?.GetValue(allocator);
            return capacity is int value && value > 0 ? value : DefaultCapacity;
        }
        catch
        {
            return DefaultCapacity;
        }
    }

    /// <summary>
    /// Free material slots, or -1 when unknown. Every persistent allocation is a named entry in
    /// AssetMap (GpuObjectSystem.CreateObject frees the slot again when the name is taken), so the
    /// distinct live handles there are the slots in use.
    /// </summary>
    public static int GetFreeSlotCount()
    {
        int capacity = GetCapacity();
        if (capacity < 0 || _assetMap == null) return -1;

        try
        {
            var used = new HashSet<int>();
            foreach (DictionaryEntry entry in _assetMap)
            {
                if (entry.Value is GpuObjectAssetRef asset && asset.IsValid && asset.Handle >= 0)
                    used.Add(asset.Handle);
            }
            return Math.Max(0, capacity - used.Count);
        }
        catch (Exception ex)
        {
            _lastError = $"GetFreeSlotCount error: {ex.Message}";
            return -1;
        }
    }

    /// <summary>Slots doh may still allocate without touching the game's reserve, or -1 when unknown.</summary>
    public static int GetAvailableSlots()
    {
        int free = GetFreeSlotCount();
        return free < 0 ? -1 : Math.Max(0, free - ReservedSlots);
    }

    /// <summary>
    /// Removes a doh-created material from the AssetMap and returns its slot to the pool.
    /// Only call for materials doh created and that no live renderable still references.
    /// </summary>
    public static bool DestroyMaterial(string assetName)
    {
        if (_assetMap == null) return false;

        try
        {
            var key = (AssetName)assetName;
            if (!_assetMap.Contains(key)) return false;

            var asset = _assetMap[key] as GpuObjectAssetRef;
            // Unmap first so nothing can resolve the name to a slot that is about to be reused.
            _assetMap.Remove(key);
            if (asset == null) return false;

            MaterialColorState.Forget(asset.Handle);
            asset.Dispose(); // GpuObjectAssetRef.Destroy -> GpuObjectSystem.Free(handle)
            return true;
        }
        catch (Exception ex)
        {
            _lastError = $"DestroyMaterial error for '{assetName}': {ex.Message}";
            Console.WriteLine($"doh: {_lastError}");
            return false;
        }
    }
}
