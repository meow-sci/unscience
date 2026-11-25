using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MeowSci.PebblesLib;

/// <summary>
/// Partial native retirement unmaps every mapped-memory field before any buffer field is disposed.
/// KSA 5638 turned ClutterCubeCellGrid's per-viewport maps into jagged arrays; the field selector must
/// keep finding them, whatever the nesting. KSA 5919 moved those maps, their buffers and the descriptor
/// pools into GroundClutterGpuResources (now on Pebbles' retirement type list) and left the grid CPU-only.
/// </summary>
internal static class RetirementShapeChecks
{
    private readonly struct Mapped;
    private sealed class Buffer;
    private sealed class Node : IEnumerable<Node>
    {
        public IEnumerator<Node> GetEnumerator() { yield break; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

#pragma warning disable CS0169, CS0649 // Field shapes mirror KSA 5919 GroundClutterGpuResources; values are never used.
    private sealed class Borrowed; // e.g. the planet mesh's BoundingVolumeHierarchy: not a retired type, never walked
    private sealed class GpuResources5919
    {
        private Buffer[][]? _planetMatricesUbos;
        private Buffer[][]? _generateCellDataBuffers;
        private Buffer? _generateCellsToUnloadBuffer;
        public Buffer? CellObjectCountsBuffer, OutCcfPositionsBuffer, LodFadeDataBuffer; // per-planet (lazy) since 5919
        public Buffer? LodParamsBuffer, ObjectBoundingSpheresBuffer, OutDisplacedRestDeltasBuffer, EcotypeInfoBuffer, ExclusionsBuffer;
        private Buffer[][]? _cellAnchorEgoBuffers;
        private Mapped[][]? _cellAnchorEgoMemory;
        private Buffer[][]? _viewportCellSlotBuffers;
        private Mapped[][]? _viewportCellSlotMemory;
        private readonly int[] _viewportCellSlotCounts = new int[5];
        public Buffer[]? DisplacedInstanceBuffers;
        private Mapped[]? _displacedInstanceMemory;
        private Mapped[][]? _generateUboMemory;
        public Mapped[][]? GenerateCellDataMemory;
        public Mapped GenerateCellsToUnloadMemory;
        public Borrowed? Bvh;
        public bool RenderDataInitialized;
    }

    /// <summary>KSA 5919 ClutterCubeCellGrid keeps only CPU state (push constants, slot caches, free slot ids).</summary>
    private sealed class CellGrid5919
    {
        private bool _snapToMesh;
        private uint[]? _cellObjectCounts;
        private bool[] _slotHasExclusions = [];
        public readonly Stack<int> AvailableBufferBlockIDs = new();
    }
#pragma warning restore CS0169, CS0649

    public static void Run()
    {
        Check(FieldShapes.Holds(typeof(Mapped), typeof(Mapped)), "the element itself");
        Check(FieldShapes.Holds(typeof(Mapped[]), typeof(Mapped)), "single array (5482 shape)");
        Check(FieldShapes.Holds(typeof(Mapped[][]), typeof(Mapped)), "jagged array (5638 shape)");
        Check(FieldShapes.Holds(typeof(Mapped[][][]), typeof(Mapped)), "deeper jagged array");
        Check(FieldShapes.Holds(typeof(Mapped[,]), typeof(Mapped)), "multidimensional array");
        Check(FieldShapes.Holds(typeof(List<Mapped[]>), typeof(Mapped)), "generic sequence of arrays");
        Check(FieldShapes.Holds(typeof(IReadOnlyList<Mapped>), typeof(Mapped)), "sequence interface");
        Check(FieldShapes.Holds(typeof(Dictionary<int, Mapped[]>), typeof(Mapped)), "dictionary values");
        Check(FieldShapes.Holds(typeof(Mapped?[]), typeof(Mapped)), "nullable elements");
        Check(!FieldShapes.Holds(typeof(Buffer[][]), typeof(Mapped)), "buffers are not maps");
        Check(!FieldShapes.Holds(typeof(int[]), typeof(Mapped)), "unrelated arrays");
        Check(!FieldShapes.Holds(typeof(string), typeof(Mapped)), "strings");
        Check(!FieldShapes.Holds(typeof(Dictionary<Mapped, int>), typeof(Mapped)), "dictionary keys are not retired values");
        Check(!FieldShapes.Holds(typeof(Node), typeof(Mapped)), "self-referential sequences terminate");

        var mapped = MappedFields(typeof(GpuResources5919));
        string[] expected = ["GenerateCellDataMemory", "GenerateCellsToUnloadMemory", "_cellAnchorEgoMemory", "_displacedInstanceMemory", "_generateUboMemory", "_viewportCellSlotMemory"];
        Check(mapped.SequenceEqual(expected.OrderBy(n => n, StringComparer.Ordinal)), "every 5919 GroundClutterGpuResources map is unmapped first: " + string.Join(", ", mapped));
        Check(MappedFields(typeof(CellGrid5919)).Length == 0, "the 5919 cell grid holds no mapped memory");
        Check(!FieldShapes.Holds(typeof(Borrowed), typeof(Mapped)), "the borrowed BVH is not treated as a map");
        Console.WriteLine("PASS: Pebbles retirement unmaps nested mapped-memory fields before buffers (5919 GroundClutterGpuResources shapes).");
    }

    private static string[] MappedFields(Type type) => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(f => FieldShapes.Holds(f.FieldType, typeof(Mapped))).Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Retirement shape check failed: " + message);
    }
}
