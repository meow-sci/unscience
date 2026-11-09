using System;
using System.Linq;
using System.Text.Json.Nodes;
using Brutal.Numerics;
using KSA;
using MeowSci.DohLib;
using MeowSci.WarPaintLib;
using MeowSci.DohLib.Spawning;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

internal static partial class Checks
{
    private static void DefaultOffsetChecks()
    {
        Require(new SpawnRequest().OffsetBodyFrame == new double3(1, 1, -2), "public spawn requests use the requested default");
        var submod = new DohSubmod();
        var coordinator = new SceneSaveCoordinator(submod.SaveParticipants.Append(WarPaintService.Shared.SaveParticipant));
        Require(submod.SpawnOffset == new float3(1, 1, -2), "fresh UI defaults match the request");
        submod.SpawnOffset = new float3(9, 8, 7);
        var saved = RoundTrip(coordinator.Capture());
        Load(coordinator, saved, Array.Empty<KittenEva>());
        Require(submod.SpawnOffset == new float3(9, 8, 7), "explicit saved offsets remain unchanged");
        var state = JsonNode.Parse(saved.Features["doh"].State.GetRawText())!.AsObject();
        state.Remove("Offset");
        saved.Features["doh"].State = System.Text.Json.JsonDocument.Parse(state.ToJsonString()).RootElement.Clone();
        Load(coordinator, saved, Array.Empty<KittenEva>());
        Require(submod.SpawnOffset == new float3(1, 1, -2), "legacy missing offsets get the new default");
        Load(coordinator, new SaveDocument(), Array.Empty<KittenEva>());
        Require(submod.SpawnOffset == new float3(1, 1, -2), "vanilla reset restores the new default");
    }
}
