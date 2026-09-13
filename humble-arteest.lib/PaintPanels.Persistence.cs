namespace MeowSci.HumbleArteestLib;

public sealed partial class VehiclePaintSubmod
{
    internal void ClearSavedTargets()
    {
        _groups.Clear();
        _groupIndex = 0;
        _timeSinceRefresh = double.MaxValue;
    }
}

public sealed partial class KittenColorSubmod
{
    internal void SynchronizeSavedState(bool active)
    {
        _active = active;
        _materialEntries.Clear();
        if (!active || !KittenColor.IsInitialized) return;
        RebuildEntries();
        var colors = KittenColor.CaptureColors();
        foreach (var entry in _materialEntries)
            if (colors.TryGetValue(entry.Name, out var color)) { entry.Color = color; entry.Enabled = true; }
    }
}

public sealed partial class EngineEmissiveSubmod
{
    internal void SynchronizeSavedState(bool active)
    {
        _active = active;
        _entries.Clear();
        _globalTemp = EngineEmissive.GlobalTemperature;
        _globalTfi = EngineEmissive.GlobalTfi;
        _applyToAll = EngineEmissive.GlobalEnabled;
        if (!active) return;
        ScanEntries();
        foreach (var entry in _entries)
        {
            var settings = EngineEmissive.GetSettings(entry.Model);
            if (settings == null) continue;
            entry.Enabled = true; entry.Temp = settings.Value.Temperature; entry.Tfi = settings.Value.Tfi;
        }
    }
}
