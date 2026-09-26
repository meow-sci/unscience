using System;
using System.Collections.Generic;
using System.Linq;
using KSA;

namespace MeowSci.PebblesLib;

/// <summary>
/// Removed/displaced clutter across graph swaps and saves. Each record remembers every grid it has seen
/// (<see cref="ClutterGridMemory"/>); matching grids receive the state, other grids keep it for later.
/// Native saves carry stock-grid state (rewritten here when the override uses another spacing); the
/// `pebbles.clutter-state` sidecar carries the override grids.
/// </summary>
public sealed partial class ClutterController
{
    private static void DrainExclusions(Celestial body, GroundClutterRenderer renderer)
    {
        // Mirrors Universe.SyncGroundClutter (KSA 5482): removals carry their exclusion type, and
        // displaced objects must be recorded before ClearStatics settles them.
        var bubbles = Bubbles(); var events = new List<BubbleClutterStatics.ClutterRemoval>();
        foreach (var bubble in bubbles) if (ReferenceEquals(bubble.Parent, body)) bubble.PopulatePendingExclusions(events);
        foreach (var removal in events)
        {
            var e = removal.Key;
            renderer.ExcludeInstance(e.CelestialHash, (uint)e.EcotypeIndex, e.Cell, e.SubCellId, removal.Type);
            foreach (var bubble in bubbles) bubble.RemoveExcludedClutterInstance(in e);
            var placement = renderer.PlanetPlacementData[e.CelestialHash][e.EcotypeIndex];
            if (removal.Type == BubbleClutterStatics.ClutterExclusionType.Displaced)
                placement.AddDisplacedObject(in e, in removal.Displaced);
            else
                placement.RemoveDisplacedObject(in e);
        }
    }

    /// <summary>
    /// Called before ClearStatics, so in-flight displaced objects keep their velocity and stay loose.
    /// A disabled grid never received displaced records, so it cannot be authoritative for them.
    /// </summary>
    private static void RememberGrids(ClutterLiveRecord record, GroundClutterReference graph, GroundClutterPlacementData[] placement, bool authoritative)
    {
        for (var i = 0; i < placement.Length; i++)
            record.Memory.Remember(ClutterGridNative.Snapshot(graph.Ecotypes[i], placement[i]), authoritative && ClutterGridNative.HostsObjects(graph.Ecotypes[i]));
    }

    private static void ReplayGrids(ClutterLiveRecord record, GroundClutterReference graph, GroundClutterPlacementData[] placement,
        ClutterEcotypeRenderData[] render, ClutterEcotypePhysicalData[] physics, IReadOnlyCollection<string>? onlyKeys = null)
    {
        for (var i = 0; i < placement.Length; i++)
        {
            var key = ClutterGridNative.Key(graph.Ecotypes[i]);
            if (onlyKeys != null && !onlyKeys.Contains(key)) continue;
            var state = record.Memory.Get(key);
            if (state != null) ClutterGridNative.Replay(state, record.Body.Hash, i, graph.Ecotypes[i], placement[i], render[i], physics[i]);
        }
    }

    private static HashSet<string> StockKeys(ClutterLiveRecord record)
        => record.OriginalTemplate!.GroundClutterReference!.Ecotypes.Select(ClutterGridNative.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Postfix of GroundClutterRenderer.SerializeSave. Native entries are keyed only by body and ecotype
    /// name and reload into the stock grid, so an override at another spacing (or a disabled one, which
    /// holds no displaced records) is replaced by the remembered stock-grid state. Enabled same-spacing
    /// overrides keep their live state, which is valid for stock.
    /// </summary>
    internal void WriteNativeSave(GroundClutterRenderer renderer, List<ClutterEcotypeSaveData> data)
    {
        foreach (var record in _live.Values)
        {
            if (!ReferenceEquals(record.Owner, renderer) || record.Resources == null || record.OriginalTemplate == null) continue;
            var stock = record.OriginalTemplate.GroundClutterReference!.Ecotypes;
            var live = record.Resources.Graph.Reference.Ecotypes;
            var rewrite = Enumerable.Range(0, stock.Count)
                .Where(i => ClutterGridNative.Key(stock[i]) != ClutterGridNative.Key(live[i]) || !ClutterGridNative.HostsObjects(live[i])).ToArray();
            if (rewrite.Length == 0) continue;
            var entries = rewrite.ToDictionary(i => i, i => data.Where(e => e.CelestialId == record.BodyId && e.EcotypeId == stock[i].Name).ToArray());
            try
            {
                RememberGrids(record, record.Resources.Graph.Reference, record.Resources.Placement, authoritative: true);
                foreach (var (i, matches) in entries)
                    foreach (var entry in matches) ClutterGridNative.WriteNative(record.Memory.Get(ClutterGridNative.Key(stock[i])), entry);
            }
            catch (Exception ex)
            {
                // Never leave override-grid cells in a stock-grid entry: they would punch unrelated holes.
                foreach (var entry in entries.Values.SelectMany(e => e)) { entry.Excluded.Clear(); entry.DynamicObjects.Clear(); }
                Console.WriteLine($"pebbles: {record.BodyId} stock clutter state omitted from native save: {ex.Message}");
            }
        }
    }

    /// <summary>Override-grid clutter state for the sidecar. Stock grids are excluded: native saves own them.</summary>
    internal ClutterBodyState[] CaptureGridStates()
    {
        var result = new List<ClutterBodyState>();
        foreach (var record in _live.Values)
        {
            if (record.OriginalTemplate == null) continue;
            if (record.Owner != null && record.Resources != null)
                RememberGrids(record, record.Resources.Graph.Reference, record.Resources.Placement, authoritative: true);
            var grids = record.Memory.Export(StockKeys(record));
            if (grids.Count != 0) result.Add(new ClutterBodyState { BodyId = record.BodyId, Grids = grids });
        }
        return result.ToArray();
    }

    /// <summary>Seeds saved override grids after the body's recipe was reapplied, then replays matching live grids.</summary>
    internal int RestoreGridState(ClutterBodyState saved)
    {
        if (!_live.TryGetValue(saved.BodyId, out var record) || record.OriginalTemplate == null)
            throw new InvalidOperationException("its clutter recipe is not applied, so saved override clutter state was not restored.");
        var stock = StockKeys(record);
        var added = record.Memory.Seed(saved.Grids.Where(g => !stock.Contains(g.GridKey())));
        if (added.Count == 0 || record.Owner == null || record.Resources == null) return added.Count;
        Quiesce(); CheckOwnership(record);
        var live = record.Resources;
        ReplayGrids(record, live.Graph.Reference, live.Placement, live.Render, live.Physical, added);
        return added.Count;
    }
}
