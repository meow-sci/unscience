using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;

namespace KSA;

public sealed class Vehicle
{
    public bool IsDisposed;
    public object? Parent;
}

public sealed class Part;
public sealed class VehicleUpdateState(Vehicle vehicle)
{
    public readonly Vehicle ReadOnlyVehicle = vehicle;
}

// Real game-version Bepu simulation, with only the KSA wrapper replaced by a managed fixture.
public sealed class ConstraintSim : IDisposable
{
    private readonly BufferPool _pool = new();
    public readonly Simulation Simulation;
    public readonly Dictionary<BodyHandle, VehicleUpdateState> HandleToState = new();
    public readonly List<CollidablePair> Contacts = new();
    public bool ThrowDuringPass;
    public int ModuleUpdates;

    public ConstraintSim() => Simulation = Simulation.Create(_pool, new ContactsCallback(Contacts),
        new PoseCallback(), new SolveDescription(4, 1));

    public BodyHandle Add(Vehicle vehicle, Vector3 position)
    {
        var sphere = new Sphere(1);
        var shape = Simulation.Shapes.Add(sphere);
        var handle = Simulation.Bodies.Add(BodyDescription.CreateDynamic(new RigidPose(position),
            sphere.ComputeInertia(1), shape, new BodyActivityDescription(-1)));
        HandleToState.Add(handle, new(vehicle));
        return handle;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void DetectCollisions(double dt)
    {
        Contacts.Clear();
        if (ThrowDuringPass) throw new InvalidOperationException("fixture collision failure");
        Simulation.PredictBoundingBoxes((float)dt);
        Simulation.CollisionDetection((float)dt);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Simulate(double intraStepTime, in SimStep simStep)
    {
        Contacts.Clear();
        ModuleUpdates++;
        if (ThrowDuringPass) throw new InvalidOperationException("fixture collision failure");
        Simulation.Timestep((float)simStep.DeltaTime);
    }

    public void Dispose()
    {
        Simulation.Dispose();
        _pool.Clear();
    }

    private struct ContactsCallback(List<CollidablePair> contacts) : INarrowPhaseCallbacks
    {
        public void Initialize(Simulation simulation) { }
        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b,
            ref float speculativeMargin) => true;
        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;
        public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold,
            out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
        {
            pairMaterial = new PairMaterialProperties(0.5f, 1, new SpringSettings(30, 1));
            if (manifold.Count > 0) contacts.Add(pair);
            return true;
        }
        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA,
            int childIndexB, ref ConvexContactManifold manifold) => true;
        public void Dispose() { }
    }

    private struct PoseCallback : IPoseIntegratorCallbacks
    {
        public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
        public bool AllowSubstepsForUnconstrainedBodies => true;
        public bool IntegrateVelocityForKinematics => false;
        public void Initialize(Simulation simulation) { }
        public void PrepareForIntegration(float dt) { }
        public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation,
            BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt,
            ref BodyVelocityWide velocity) { }
    }
}
