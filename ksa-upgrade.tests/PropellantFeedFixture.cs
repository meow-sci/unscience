using System;

namespace KSA;

// The supplied game DLL is a Windows x64 binary and cannot be loaded by the macOS ARM64 managed
// test runner. These tiny declarations preserve only the members touched by the linked production
// PropellantFeedDiagnostics helper. They are fixtures, not copied game sources.
public sealed class Tank
{
}

public readonly struct FlowOrder<T>
{
    private readonly T[][]? _levels;

    public FlowOrder(T[][]? levels)
    {
        _levels = levels;
    }

    public int LevelCount => _levels?.Length ?? 0;

    public ReadOnlySpan<T> this[int index] => _levels is null ? default : _levels[index];
}

public sealed class ResourceManager
{
    public ResourceManager()
    {
    }

    public ResourceManager(FlowOrder<Tank> consumptionOrder)
    {
        ConsumptionOrder = consumptionOrder;
    }

    public FlowOrder<Tank> ConsumptionOrder { get; }
}
