using KSA;

namespace MeowSci.BlinkyLib;

/// <summary>Reads the selected flow-rule view without treating empty distance levels as reachable tanks.</summary>
internal static class PropellantFeedDiagnostics
{
    internal static int CountTanks(ResourceManager? manager)
    {
        if (manager == null) return 0;
        var order = manager.ConsumptionOrder;
        int count = 0;
        for (int level = 0; level < order.LevelCount; level++)
            count += order[level].Length;
        return count;
    }
}
