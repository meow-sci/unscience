using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuUtilities.Memory;

namespace MeowSci.DohLib.Placement;

/// <summary>Detached contact queries, centred on the proposed capsule. Borrows shapes; owns scratch memory.</summary>
internal sealed class CapsuleCollisionQuery : IDisposable
{
    private static readonly CollisionTaskRegistry Tasks = DefaultTypes.CreateDefaultCollisionTaskRegistry();
    private sealed class Result { public float Depth; }
    private struct Callbacks(Result result) : ICollisionCallbacks
    {
        public bool AllowCollisionTesting(int pairId, int childA, int childB) => true;
        public void OnChildPairCompleted(int pairId, int childA, int childB, ref ConvexContactManifold manifold) { }
        public void OnPairCompleted<T>(int pairId, ref T manifold) where T : unmanaged, IContactManifold<T>
        {
            for (int i = 0; i < manifold.Count; i++) result.Depth = Math.Max(result.Depth, manifold.GetDepth(i));
        }
    }

    private readonly BufferPool _pool = new();
    private readonly Shapes _shapes;
    private readonly Capsule _capsule;
    private readonly Quaternion _orientation;
    private readonly Result _result = new();
    private CollisionBatcher<Callbacks> _batch;
    private bool _flushed;

    public CapsuleCollisionQuery(Shapes shapes, Capsule capsule, Quaternion orientation)
    {
        CapsulePlacementMath.Validate(capsule);
        _shapes = shapes; _capsule = capsule; _orientation = orientation;
        _batch = new(_pool, shapes, Tasks, 0, new(_result));
    }

    public unsafe void Add(TypedIndex shape, RigidPose pose)
    {
        _shapes[shape.Type].GetShapeData(shape.Index, out var obstacle, out _);
        var capsule = _capsule;
        _batch.CacheShapeB(shape.Type, Capsule.TypeId, Unsafe.AsPointer(ref capsule),
            Unsafe.SizeOf<Capsule>(), out var cached);
        _batch.AddDirectly(shape.Type, Capsule.TypeId, obstacle, cached,
            -pose.Position, pose.Orientation, _orientation, 0, new PairContinuation(0));
    }

    public void Add<T>(T shape, RigidPose pose) where T : struct, IShape =>
        _batch.Add(shape, _capsule, -pose.Position, pose.Orientation, _orientation, 0, 0);

    public bool Penetrates()
    {
        if (!_flushed) { _batch.Flush(); _flushed = true; }
        return _result.Depth > .0001f;
    }

    public void Dispose()
    {
        try { if (!_flushed) _batch.Flush(); }
        finally { _flushed = true; _pool.Clear(); }
    }
}
