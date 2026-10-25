using System;
using HarmonyLib;
using KSA;
using MeowSci.IronManLib;

internal static class InputCompatibilityChecks
{
    private static KittenEva? _somersaultOwner;
    private static int _somersaultRequests;
    private static int _checks;

    // Purr's Space hook is an effectful bool prefix: accepting a request consumes the key.
    private static bool SomersaultPrefix(KittenEva __instance, ref bool __result)
    {
        if (!ReferenceEquals(__instance, _somersaultOwner)) return true;
        _somersaultRequests++;
        __result = true;
        return false;
    }

    internal static void Run()
    {
        _checks = 0;
        var previous = IronManSubmod.Instance;
        try
        {
            for (int order = 0; order < 2; order++) RunOrder(order);
        }
        finally
        {
            _somersaultOwner = null;
            IronManSubmod.Instance = previous;
        }
        Console.WriteLine($"PASS: Iron Man input ownership with an effectful somersault prefix ({_checks} checks)");
    }

    private static void RunOrder(int order)
    {
        var rocket = new Harmony($"iron-man.fixture.input-{order}");
        var somersault = new Harmony($"iron-man.fixture.somersault-{order}");
        var mode = new IronManSubmod();
        IronManSubmod.Instance = mode;
        var enabled = new KittenEva();
        var other = new KittenEva();
        var ordinary = new KittenEva();
        _somersaultRequests = 0;
        void InstallSomersault() => somersault.Patch(
            AccessTools.DeclaredMethod(typeof(KittenEva), nameof(KittenEva.OnKey)),
            prefix: new HarmonyMethod(typeof(InputCompatibilityChecks), nameof(SomersaultPrefix)));

        try
        {
            if (order == 0) InstallSomersault();
            IronManFlightPatches.Apply(rocket);
            if (order == 1) InstallSomersault();

            _somersaultOwner = enabled;
            Check(enabled.OnKey(default) && _somersaultRequests == 1 && enabled.BaseKeys == 0 && enabled.EvaKeys == 0,
                "inactive Iron Man permits a somersault request");
            mode.Enabled.Add(enabled);
            Check(enabled.OnKey(default) && enabled.BaseKeys == 1 && _somersaultRequests == 1 && enabled.EvaKeys == 0,
                "active Iron Man dispatches one vessel key before the competing request");

            _somersaultOwner = other;
            Check(other.OnKey(default) && _somersaultRequests == 2 && other.BaseKeys == 0 && other.EvaKeys == 0,
                "another kitten retains its somersault input");
            Check(!ordinary.OnKey(default) && ordinary.EvaKeys == 1 && ordinary.BaseKeys == 0,
                "a kitten outside both owners retains native EVA input");
            _somersaultOwner = enabled;
            mode.Enabled.Remove(enabled);
            Check(enabled.OnKey(default) && _somersaultRequests == 3 && enabled.BaseKeys == 1,
                "disabling Iron Man immediately restores the competing input owner");

            mode.Enabled.Add(enabled);
            IronManFlightPatches.Remove(rocket);
            Check(enabled.OnKey(default) && _somersaultRequests == 4 && enabled.BaseKeys == 1,
                "unloading Iron Man leaves the other mod's prefix installed");
            somersault.UnpatchAll(somersault.Id);
            Check(!enabled.OnKey(default) && enabled.EvaKeys == 1 && enabled.BaseKeys == 1,
                "removing both owners restores native EVA input");
        }
        finally
        {
            IronManFlightPatches.Remove(rocket);
            somersault.UnpatchAll(somersault.Id);
            _somersaultOwner = null;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
}
