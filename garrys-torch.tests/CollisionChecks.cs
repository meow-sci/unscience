using System;
using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using HarmonyLib;
using KSA;
using MeowSci.GarrysTorchLib;

internal static class CollisionChecks
{
    public static void Run()
    {
        using var sim = new ConstraintSim();
        var source = new Vehicle();
        var target = new Vehicle();
        var handle = sim.Add(source, Vector3.Zero);
        sim.Add(target, new Vector3(1, 0, 0));
        // An unrelated pair must continue colliding.
        sim.Add(new Vehicle(), new Vector3(10, 0, 0));
        sim.Add(new Vehicle(), new Vector3(11, 0, 0));
        sim.Simulation.Statics.Add(new StaticDescription(new Vector3(0, -1, 0),
            sim.Simulation.Shapes.Add(new Box(2, 1, 2))));
        var shape = sim.Simulation.Bodies[handle].Collidable.Shape;
        var weld = new WeldEntry { Source = source, Target = target };
        Require(!weld.Collisions, "collisions default off");
        var step = new SimStep(new(0), new(0.01), 0.01);

        // Warm both methods before Harmony installation to catch caller/inlining problems.
        sim.DetectCollisions(0.01);
        Require(sim.Contacts.Exists(pair => pair.A.Mobility == CollidableMobility.Static
            || pair.B.Mobility == CollidableMobility.Static), "fixture must include static contacts");
        sim.Simulate(0, step);
        Require(HasContact(sim, handle), "stock source must collide");
        var harmony = new Harmony("garrys-torch.tests.collisions");
        WeldCollisionPatches.Apply(harmony);
        try
        {
            WeldCollisionPatches.Publish(new[] { weld });
            for (int i = 0; i < 3; i++)
            {
                sim.DetectCollisions(0.01);
                CheckSuppressed();
                int updates = sim.ModuleUpdates;
                sim.Simulate(0, step);
                CheckSuppressed();
                Require(sim.Simulation.Bodies[handle].Constraints.Count == 0, "remove cached contact constraints");
                Require(sim.ModuleUpdates == updates + 1, "simulation must still execute");
            }

            weld.Collisions = true;
            sim.DetectCollisions(0.01);
            CheckSuppressed(); // Mutable UI state cannot leak into an in-flight snapshot.
            WeldCollisionPatches.Publish(new[] { weld });
            sim.DetectCollisions(0.01);
            Require(HasContact(sim, handle), "collision opt-in restores contacts");

            weld.Collisions = false;
            weld.WeldEnabled = false;
            CheckReleased("suspended weld");
            weld.WeldEnabled = true;
            target.IsDisposed = true;
            CheckReleased("destroyed target");
            target.IsDisposed = false;
            source.IsDisposed = true;
            CheckReleased("destroyed source");
            source.IsDisposed = false;
            target.Parent = new object();
            CheckReleased("parent mismatch");
            target.Parent = null;

            // Collider rebuilds/scaling must restore the new shape, not the original weld-time shape.
            shape = sim.Simulation.Shapes.Add(new Sphere(1.1f));
            sim.Simulation.Bodies[handle].SetShape(shape);
            WeldCollisionPatches.Publish(new[] { weld });
            sim.DetectCollisions(0.01);
            CheckSuppressed();

            WeldCollisionPatches.Publish(new[] { weld });
            sim.ThrowDuringPass = true;
            foreach (Action run in new Action[] { () => sim.DetectCollisions(0.01), () => sim.Simulate(0, step) })
            {
                try { run(); throw new Exception("expected fixture failure"); }
                catch (InvalidOperationException) { }
                Require(sim.Simulation.Bodies[handle].Collidable.Shape == shape, "restore after exception");
            }
            sim.ThrowDuringPass = false;
            WeldCollisionPatches.Publish(Array.Empty<WeldEntry>());
            sim.DetectCollisions(0.01);
            Require(HasContact(sim, handle), "unweld restores contacts");
            WeldCollisionPatches.Publish(new[] { weld });
        }
        finally { WeldCollisionPatches.Remove(harmony); }

        sim.DetectCollisions(0.01);
        Require(HasContact(sim, handle), "unload restores stock contacts");
        Console.WriteLine("PASS: real Bepu weld collision suppression, restoration and simulation retention");

        void CheckSuppressed()
        {
            Require(!HasContact(sim, handle), "source must have no vehicle or terrain contacts");
            Require(sim.Contacts.Count > 0, "unrelated bodies must still collide");
            Require(sim.Simulation.Bodies[handle].Collidable.Shape == shape, "restore original shape after pass");
        }
        void CheckReleased(string reason)
        {
            WeldCollisionPatches.Publish(new[] { weld });
            sim.DetectCollisions(0.01);
            Require(HasContact(sim, handle), reason + " restores contacts");
        }
    }

    private static bool HasContact(ConstraintSim sim, BodyHandle body) => sim.Contacts.Exists(pair =>
        (pair.A.Mobility != CollidableMobility.Static && pair.A.BodyHandle == body) ||
        (pair.B.Mobility != CollidableMobility.Static && pair.B.BodyHandle == body));

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
