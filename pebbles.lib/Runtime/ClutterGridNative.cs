using System;
using Brutal.Numerics;
using KSA;

namespace MeowSci.PebblesLib;

/// <summary>
/// Converts between native placement state (exclusion masks + KSA 5447 displaced objects) and the
/// game-independent <see cref="ClutterGridState"/>. Reads use the public native save serializer;
/// writes use public ExcludeCell/DeserializeDisplacedObject, so no private placement fields are touched.
/// </summary>
internal static class ClutterGridNative
{
    public static string Key(ClutterEcotypeReference ecotype) => ClutterGridMemory.Key(ecotype.Name, ecotype.Placement.ObjectSeparation.InMeters());
    /// <summary>A disabled (zero biome mask) ecotype places nothing, so it must neither draw nor own displaced records.</summary>
    public static bool HostsObjects(ClutterEcotypeReference ecotype) => ecotype.Placement.BiomeMask != 0;

    public static ClutterGridState Snapshot(ClutterEcotypeReference ecotype, GroundClutterPlacementData placement)
    {
        var native = new ClutterEcotypeSaveData();
        placement.SerializeSave(native);
        var state = new ClutterGridState { Ecotype = ecotype.Name, Separation = ecotype.Placement.ObjectSeparation.InMeters() };
        foreach (var cell in native.Excluded)
        {
            var words = new uint[ClutterCellMask.WordCount];
            var mask = cell.ExclusionData;
            for (var i = 0; i < words.Length; i++) words[i] = mask[i];
            state.Cells.Add(new ClutterCellMask { X = cell.X, Y = cell.Y, Face = cell.FaceId, Words = words });
        }
        foreach (var d in native.DynamicObjects)
            state.Displaced.Add(new ClutterDisplacedState
            {
                X = d.X, Y = d.Y, Face = d.FaceId, SubCell = d.SubCellId, ObjectId = d.ObjectId, ScaleId = d.ScaleId, PackedColor = d.PackedColor, Settled = d.Settled,
                Position = Array(d.PositionCcf), Rotation = Array(d.RotationCcf), RestPosition = Array(d.PositionCcf + d.RestOffsetCcf),
                RestRotation = Array(d.RestRotationCcf), Velocity = Array(d.VelocityCcf), AngularVelocity = Array(d.AngularVelocityCcf)
            });
        return state;
    }

    /// <summary>ANDs masks into the destination and, if it hosts objects, replaces its displaced records with the remembered set.</summary>
    public static void Replay(ClutterGridState state, KeyHash body, int index, ClutterEcotypeReference ecotype,
        GroundClutterPlacementData placement, ClutterEcotypeRenderData render, ClutterEcotypePhysicalData physics)
    {
        foreach (var saved in state.Cells)
        {
            var cell = new CubeCellGrid.Cell(saved.X, saved.Y, saved.Face);
            var mask = placement.GetExclusionData(cell);
            for (var i = 0; i < ClutterCellMask.WordCount; i++) mask[i] &= saved.Words[i];
            placement.ExcludeCell(cell, mask);
            render.QueueExclusionUpload(cell); physics.QueueExclusionUpload(cell);
        }
        if (!HostsObjects(ecotype)) return;
        var existing = new ClutterEcotypeSaveData();
        placement.SerializeSave(existing);
        foreach (var d in existing.DynamicObjects) placement.RemoveDisplacedObject(InstanceKey(body, index, d.X, d.Y, d.FaceId, d.SubCellId));
        foreach (var d in state.Displaced)
        {
            if (d.ObjectId >= ecotype.ClutterObjects.Count || d.ScaleId >= GroundClutterRenderer.MAX_UNIQUE_SCALES)
            {
                Console.WriteLine($"pebbles: skipped displaced {ecotype.Name} object {d.ObjectId}/{d.ScaleId}; its slot no longer exists.");
                continue;
            }
            var record = new GroundClutterPlacementData.DisplacedObject
            {
                ObjectId = d.ObjectId, ScaleId = d.ScaleId, PackedColor = d.PackedColor, Settled = d.Settled,
                PositionCcf = Vector(d.Position), RotationCcf = Quat(d.Rotation), RestPositionCcf = Vector(d.RestPosition),
                RestRotationCcf = Quat(d.RestRotation), VelocityCcf = Vector(d.Velocity), AngularVelocityCcf = Vector(d.AngularVelocity)
            };
            placement.DeserializeDisplacedObject(InstanceKey(body, index, d.X, d.Y, d.Face, d.SubCell), in record, loose: !d.Settled);
        }
    }

    /// <summary>Rewrites one native save entry with this grid's state (none clears it).</summary>
    public static void WriteNative(ClutterGridState? state, ClutterEcotypeSaveData entry)
    {
        entry.Excluded.Clear(); entry.DynamicObjects.Clear();
        if (state == null) return;
        foreach (var saved in state.Cells)
        {
            var mask = GroundClutterRenderer.ExclusionData.AllIncluded;
            for (var i = 0; i < ClutterCellMask.WordCount; i++) mask[i] = saved.Words[i];
            entry.Excluded.Add(new ClutterEcotypeSaveData.Cell { X = saved.X, Y = saved.Y, FaceId = saved.Face, ExclusionData = mask });
        }
        foreach (var d in state.Displaced)
            entry.DynamicObjects.Add(new ClutterEcotypeSaveData.DisplacedObject
            {
                X = d.X, Y = d.Y, FaceId = d.Face, SubCellId = d.SubCell, ObjectId = d.ObjectId, ScaleId = d.ScaleId, PackedColor = d.PackedColor, Settled = d.Settled,
                PositionCcf = Vector(d.Position), RotationCcf = Quat(d.Rotation), RestOffsetCcf = Vector(d.RestPosition) - Vector(d.Position),
                RestRotationCcf = Quat(d.RestRotation), VelocityCcf = Vector(d.Velocity), AngularVelocityCcf = Vector(d.AngularVelocity)
            });
    }

    private static BubbleClutterStatics.ClutterInstanceKey InstanceKey(KeyHash body, int index, int x, int y, int face, uint subCell)
        => new(body, index, new CubeCellGrid.Cell(x, y, face), subCell);
    private static double[] Array(double3 v) => [v.X, v.Y, v.Z];
    private static double[] Array(doubleQuat q) => [q.X, q.Y, q.Z, q.W];
    private static double3 Vector(double[] v) => new(v[0], v[1], v[2]);
    private static doubleQuat Quat(double[] q) => new(q[0], q[1], q[2], q[3]);
}
