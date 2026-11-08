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
    private static void QueuedTeardownChecks(DohSubmod submod)
    {
        var kittens = Kittens(Enumerable.Range(0, 8).Select(i => $"buried_{i}").ToArray());
        SetWorld(kittens);
        var sets = kittens.Select(k => submod.Paint(k, Red)).ToArray();
        for (int i = 0; i < kittens.Length; i++) submod.Track(kittens[i], "Calico", sets[i]);
        Trace.Events.Clear();
        Require(submod.Spawner.RequestDespawnAll() == 8 && kittens.All(k => !k.IsDisposed)
            && sets.All(s => !s.IsReleased), "the eight-kitten UI request cannot dispose pending solver targets or materials");
        kittens[0].IsDisposed = true; // Native impact handling beats the queued user request.
        PhysicsFrameHook.Dispatch();
        Require(kittens.All(k => k.IsDisposed) && sets.All(s => s.IsReleased)
            && submod.Registry.Count == 0, "handoff handles already-dead and live kittens and releases every unique set");
        Require(Trace.Events[0] == "apply pending physics results"
            && Trace.Events.Count(e => e.StartsWith("destroy buried_")) == 7,
            "native results commit before teardown; an impact-killed kitten is never destroyed twice");

        var old = new KittenEva("same-id"); SetWorld(old);
        submod.Track(old, "Calico", null);
        Require(submod.Spawner.RequestDespawn(old.Id), "old-world request queues");
        submod.Registry.Clear(); // Native/sidecar load replaces the registration before dispatch.
        var restored = new KittenEva(old.Id); SetWorld(restored); submod.Track(restored, "Calico", null);
        PhysicsFrameHook.Dispatch();
        Require(!restored.IsDisposed && submod.Registry.Get(restored.Id) != null,
            "a stale request cannot remove a restored same-name kitten");
        PhysicsFrameHook.IsApplied = false;
        Require(!submod.Spawner.RequestDespawn(restored.Id) && !restored.IsDisposed,
            "missing handoff rejects removal instead of falling back to unsafe UI destruction");
        PhysicsFrameHook.IsApplied = true;
    }

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

namespace MeowSci.KsaAbstractions
{
    public static class PhysicsFrameHook
    {
        private static readonly System.Collections.Generic.Queue<Action> Pending = new();
        public static bool IsApplied { get; set; } = true;
        public static void Enqueue(Action action) => Pending.Enqueue(action);
        public static void Dispatch()
        {
            Trace.Events.Add("apply pending physics results");
            while (Pending.TryDequeue(out var action)) action();
        }
    }
}
