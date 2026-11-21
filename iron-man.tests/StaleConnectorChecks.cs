using System;
using System.Collections.Generic;
using System.Linq;
using KSA;
using MeowSci.IronManLib;

/// <summary>
/// KSA 5866 world/blueprint loads call PartInstance.DropStaleConnectors() before construction.
/// The production guard must keep a marked backpack's owned-anchor references while native still
/// drops genuinely stale indices, recurses into children and treats unmarked parts exactly as before.
/// </summary>
internal static class StaleConnectorChecks
{
    private static PartConnectorInstance At(int index) => new() { Index = index, ConnectedLocalInstanceId = (ulong)(100 + index) };
    private static int[] Indices(PartInstance instance) => instance.Connectors!.Select(c => c.Index).ToArray();

    private static PartInstance Marked(string templateId, string payload, params int[] indices) => new()
    {
        InstanceOf = templateId,
        Components = { new IronManAnchorModule.SaveData { Payload = payload } },
        Connectors = indices.Select(At).ToList(),
    };

    /// <summary>Runs with the production patches applied; <paramref name="payload"/> owns indices 0 and 1.</summary>
    internal static void Run(string templateId, string payload, Action<bool, string> check)
    {
        var tank = new PartTemplate { Id = "stale-check-tank" };
        tank.Connectors.Add(new Part.Connector.TemplateBase());
        PartTemplate.Library[tank.Id] = tank;
        try
        {
            var child = new PartInstance { InstanceOf = tank.Id, Connectors = [At(0), At(3)] };
            var marked = Marked(templateId, payload, 0, 1, 5);
            marked.Children = [child];
            int dropped = marked.DropStaleConnectors();
            check(dropped == 2 && Indices(marked).SequenceEqual(new[] { 0, 1 }) && Indices(child).SequenceEqual(new[] { 0 }),
                "5866 stale-connector pass keeps owned anchor references, drops only truly stale indices and still recurses");
            check(marked.Connectors!.Select(c => c.ConnectedLocalInstanceId).SequenceEqual(new ulong[] { 100, 101 }),
                "kept anchor references retain their saved connection identities");

            var reordered = Marked(templateId, payload, 1, 7, 0);
            check(reordered.DropStaleConnectors() == 1 && Indices(reordered).SequenceEqual(new[] { 1, 0 }),
                "kept anchor references keep their saved order around a dropped stale entry");

            var unmarked = new PartInstance { InstanceOf = templateId, Connectors = [At(0), At(1)] };
            check(unmarked.DropStaleConnectors() == 2 && unmarked.Connectors!.Count == 0,
                "unmarked kittens keep native stale-connector semantics");

            var malformed = Marked(templateId, "iron-man:v1:garbage", 0);
            int malformedDropped = -1;
            try { malformedDropped = malformed.DropStaleConnectors(); }
            catch (Exception) { }
            check(malformedDropped == 1,
                "a damaged record never throws (world load is outside the per-vehicle guard) and falls back to native dropping");

            var missingTemplate = Marked("no-such-template", payload, 0);
            check(missingTemplate.DropStaleConnectors() == 0 && missingTemplate.Connectors!.Count == 1,
                "an unknown template is left to native handling without throwing");
        }
        finally { PartTemplate.Library.Remove(tank.Id); }
    }

    /// <summary>After IronManConnectorPatches.Remove the native method must behave unpatched again.</summary>
    internal static void RunUnpatched(string templateId, string payload, Action<bool, string> check)
    {
        var marked = Marked(templateId, payload, 0, 1);
        check(marked.DropStaleConnectors() == 2 && marked.Connectors!.Count == 0,
            "removing the Iron Man patches also removes the stale-connector guard");
    }
}
