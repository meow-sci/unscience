using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BepuPhysics;
using Brutal.Numerics;
using KSA;

namespace MeowSci.SphinxLib;

/// <summary>Per-simulation handles; shapes belong to entries and are never freed on solver threads.</summary>
internal static class SphinxPhysics
{
    private sealed class Bubble
    {
        public readonly Dictionary<SphinxEntry, StaticHandle> Entries = new();
        public readonly HashSet<StaticHandle> Handles = new();
    }
    private static readonly ConcurrentDictionary<ConstraintSim, Bubble> Bubbles = new();
    public static bool Owns(ConstraintSim sim, StaticHandle handle) =>
        Bubbles.TryGetValue(sim, out var bubble) && bubble.Handles.Contains(handle);

    // Called before Bepu workers run. Each simulation is only mutated by its own bubble worker.
    public static void Sync(ConstraintSim sim, IReadOnlyList<SphinxEntry> entries)
    {
        if (entries.Count == 0) { Clear(sim); return; }
        var state = sim.HandleToState.Values.FirstOrDefault();
        if (state == null || state.Origin.BubFrame != BubbleFrame.Ccf) { Clear(sim); return; }
        var origin = state.Origin;
        var bubble = Bubbles.GetOrAdd(sim, _ => new Bubble());
        var wanted = new HashSet<SphinxEntry>();
        foreach (var entry in entries)
        {
            if (!entry.Visible || entry.Collider is not { } collider || !ReferenceEquals(entry.Anchor.Body, origin.Parent)) continue;
            var frame = GroundPlacement.FrameCcf(entry.Anchor, entry.Align);
            var center = collider.Center;
            var localCenter = new double3(center.X, center.Y, center.Z);
            var orientation = doubleQuat.CreateFromRotationMatrix(frame);
            var position = entry.Anchor.PositionCcf + localCenter.Transform(orientation);
            // Include actual collider reach; a large building's anchor can be far from its nearest wall.
            if (!sim.HandleToState.Values.Any(v =>
                (position - origin.PositionBub - v.GetReadOnlyStates().Kinematic.PositionPhys).Length() <= collider.Radius + 2000)) continue;
            wanted.Add(entry);
            var r = collider.Rotation;
            var localRotation = new doubleQuat(r.X, r.Y, r.Z, r.W);
            var description = new StaticDescription
            {
                Pose = new RigidPose((position - origin.PositionBub).ToBepu(), localRotation.Concatenate(orientation).ToBepu()),
                Shape = collider.Shape
            };
            if (bubble.Entries.TryGetValue(entry, out var handle))
            {
                var current = sim.Simulation.Statics[handle];
                if (current.Pose.Position != description.Pose.Position || current.Pose.Orientation != description.Pose.Orientation || current.Shape != description.Shape)
                    sim.Simulation.Statics.ApplyDescription(handle, in description);
            }
            else
            {
                handle = sim.Simulation.Statics.Add(in description);
                bubble.Entries.Add(entry, handle); bubble.Handles.Add(handle);
            }
        }
        foreach (var entry in bubble.Entries.Keys.Where(e => !wanted.Contains(e)).ToArray()) Remove(sim, bubble, entry);
    }
    private static void Remove(ConstraintSim sim, Bubble bubble, SphinxEntry entry)
    {
        var handle = bubble.Entries[entry];
        sim.Simulation.Statics.Remove(handle); // Default awakening wakes bodies resting on a removed surface.
        bubble.Entries.Remove(entry); bubble.Handles.Remove(handle);
    }
    // Main-thread callers must first wait for the solvers. Drop all references before disposing a shape.
    public static void Detach(SphinxEntry entry)
    {
        foreach (var pair in Bubbles)
            if (pair.Value.Entries.ContainsKey(entry)) Remove(pair.Key, pair.Value, entry);
    }
    public static void Clear(ConstraintSim sim)
    {
        if (!Bubbles.TryRemove(sim, out var bubble)) return;
        foreach (var entry in bubble.Entries.Keys.ToArray()) Remove(sim, bubble, entry);
    }
    public static void ClearAll()
    {
        foreach (var sim in Bubbles.Keys) Clear(sim);
    }
}
