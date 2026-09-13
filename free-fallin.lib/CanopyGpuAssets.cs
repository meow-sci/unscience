using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Brutal.VulkanApi;
using Core;
using KSA;

namespace MeowSci.FreeFallinLib;

/// <summary>Owns only private canopy allocations. Detach every observed canopy before release.</summary>
internal sealed class CanopyGpuAssets : IDisposable
{
    private readonly List<Action> _release = new();
    internal bool Committed;

    internal T Own<T>(AssetManager<T> manager, T asset, Action? released = null) where T : LoadedAssetRef
    {
        var field = typeof(AssetManager<T>).GetField("AssetMap", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(AssetManager<T>).FullName, "AssetMap");
        var map = (ConcurrentDictionary<AssetName, T>)field.GetValue(manager)!;
        _release.Add(() =>
        {
            ((ICollection<KeyValuePair<AssetName, T>>)map).Remove(new(asset.Id, asset));
            asset.Dispose();
            released?.Invoke();
        });
        return asset;
    }

    internal void Release()
    {
        if (_release.Count == 0) return;
        Program.GetRenderer().Device.WaitIdle();
        for (int i = _release.Count - 1; i >= 0; i--) { _release[i](); _release.RemoveAt(i); }
    }
    public void Dispose() { if (!Committed) Release(); }
}
