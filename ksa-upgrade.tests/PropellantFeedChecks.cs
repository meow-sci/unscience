using System;
using KSA;
using MeowSci.BlinkyLib;

internal static class PropellantFeedChecks
{
    internal static void Run()
    {
        Require(PropellantFeedDiagnostics.CountTanks(null) == 0,
            "null ResourceManager has no tanks");

        ResourceManager empty = new ResourceManager();
        Require(PropellantFeedDiagnostics.CountTanks(empty) == 0,
            "empty ResourceManager has no tanks");

        CheckCount("zero-level order", Manager(Array.Empty<Tank[]>()), 0);

        // BuildOrders can retain empty distance levels when the only reachable entries occur
        // later. These are representative already-selected views supplied to the production
        // helper; native filtering and reversal are outside this managed check.
        CheckCount("all-empty order", Manager(new[]
        {
            Array.Empty<Tank>(), Array.Empty<Tank>(), Array.Empty<Tank>(),
        }), 0);
        CheckCount("first empty, later entries", Manager(new[]
        {
            Array.Empty<Tank>(), new Tank[1], new Tank[2],
        }), 3);
        CheckCount("reversed selected view", Manager(new[]
        {
            new Tank[2], new Tank[1], Array.Empty<Tank>(),
        }), 3);
        CheckCount("same-stage selected spans", Manager(new[]
        {
            Array.Empty<Tank>(), new Tank[1], new Tank[1],
        }), 2);
    }

    private static ResourceManager Manager(Tank[][] levels)
    {
        return new ResourceManager(new FlowOrder<Tank>(levels));
    }

    private static void CheckCount(string name, ResourceManager manager, int expected)
    {
        Require(PropellantFeedDiagnostics.CountTanks(manager) == expected,
            name + " has the expected selected tank count");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
