using System;
using KSA;
using MeowSci.DentWizardLib;
using MeowSci.KsaAbstractions;

namespace MeowSci.DentWizardTests;

internal static class LaunchModeChecks
{
    internal static void Run(Vehicle source, LaunchRequest request, UniverseTime time)
    {
        var submod = new DentWizardSubmod();
        submod.Initialize();
        int launches = source.Teleports;

        submod.Arm(automatic: false);
        submod.AcceptPick(request);
        Check(!submod.Armed && !submod.Automatic, "One-shot did not disarm");
        PhysicsFrameHook.Dispatch(time);
        submod.AcceptPick(request);
        PhysicsFrameHook.Dispatch(time);
        Check(source.Teleports == ++launches, "One-shot accepted another click without arming");

        submod.ToggleAutomaticMode();
        submod.AcceptPick(null);
        Check(submod.Automatic && submod.Armed, "A miss turned off automatic mode");
        for (int i = 0; i < 3; i++)
        {
            submod.AcceptPick(request);
            // A second pending input must not overwrite the click awaiting the handoff.
            submod.AcceptPick(request with { Source = new Vehicle(source.Parent) });
            PhysicsFrameHook.Dispatch(time);
            PhysicsFrameHook.Dispatch(time);
            Check(source.Teleports == ++launches, "Automatic mode lost a click or fired without a click");
            Check(submod.Armed && submod.Automatic, "Automatic mode disarmed after firing");
        }
        source.RejectTeleport = true;
        submod.AcceptPick(request);
        PhysicsFrameHook.Dispatch(time);
        Check(source.Teleports == launches && submod.Armed, "A failed launch broke automatic mode");
        source.RejectTeleport = false;

        submod.AcceptPick(request);
        submod.ToggleAutomaticMode();
        PhysicsFrameHook.Dispatch(time);
        Check(!submod.Automatic && !submod.Armed && source.Teleports == launches,
            "Turning automatic mode off retained its queued shot");

        submod.ToggleAutomaticMode();
        submod.AcceptPick(request);
        submod.ResetState();
        PhysicsFrameHook.Dispatch(time);
        Check(!submod.Automatic && !submod.Armed && source.Teleports == launches,
            "Scene reset retained automatic mode or its queued shot");

        submod.ToggleAutomaticMode();
        KSA.Program.EditorFlag = true;
        submod.Update(0);
        KSA.Program.EditorFlag = false;
        Check(!submod.Automatic && !submod.Armed, "Entering the editor retained automatic mode");

        submod.ToggleAutomaticMode();
        submod.AcceptPick(request);
        submod.Dispose();
        PhysicsFrameHook.Dispatch(time);
        Check(!submod.Automatic && !submod.Armed && source.Teleports == launches
            && PhysicsFrameHook.Subscribers == 0, "Unload retained automatic mode or work");
        Console.WriteLine("Dent Wizard: one-shot and automatic click-mode checks passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
