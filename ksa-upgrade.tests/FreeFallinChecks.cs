using System;
using HarmonyLib;
using KSA;
using MeowSci.FreeFallinLib;

internal static class FreeFallinChecks
{
    internal static void Run()
    {
        Harmony harmony = new Harmony("ksa-upgrade.tests.free-fallin");
        FreeFallinPatches.Apply(harmony);
        try
        {
            CanopyMaterialController.CurrentMaterialHandle = 100;
            ChuteRenderable first = new ChuteRenderable(new AnimatedRenderable(10));
            ChuteRenderable second = new ChuteRenderable(new AnimatedRenderable(20));
            first.Draw();
            second.Draw();
            Require(first.Renderable.PrimaryMaterialHandle == 100
                && second.Renderable.PrimaryMaterialHandle == 100,
                "enabled draw applies the current canopy material to each renderable");

            // Repeated overrides must update the tracked slot without changing its captured stock
            // handle. A renderable born after the first draw gets its own original independently.
            CanopyMaterialController.CurrentMaterialHandle = 200;
            first.Draw();
            second.Draw();
            ChuteRenderable late = new ChuteRenderable(new AnimatedRenderable(30));
            late.Draw();
            Require(first.Renderable.PrimaryMaterialHandle == 200
                && second.Renderable.PrimaryMaterialHandle == 200
                && late.Renderable.PrimaryMaterialHandle == 200,
                "repeated override and late spawn replace all observed slots");

            FreeFallinPatches.RestoreStock();
            Require(first.Renderable.PrimaryMaterialHandle == 10
                && second.Renderable.PrimaryMaterialHandle == 20
                && late.Renderable.PrimaryMaterialHandle == 30,
                "restore returns each canopy to its distinct captured stock handle");
            FreeFallinPatches.RestoreStock();
            Require(first.Renderable.PrimaryMaterialHandle == 10
                && second.Renderable.PrimaryMaterialHandle == 20
                && late.Renderable.PrimaryMaterialHandle == 30,
                "repeated restore is harmless");

            // An external/native change while this patch owns the slot must survive restoration;
            // blindly writing the captured value would overwrite a newer owner.
            CanopyMaterialController.CurrentMaterialHandle = 300;
            ChuteRenderable externallyChanged = new ChuteRenderable(new AnimatedRenderable(40));
            externallyChanged.Draw();
            externallyChanged.Renderable.SetPrimaryMaterialHandle(777);
            FreeFallinPatches.RestoreStock();
            Require(externallyChanged.Renderable.PrimaryMaterialHandle == 777,
                "restore leaves an externally changed material slot alone");

            // Restore clears identity markers, so the next enable captures the new native handle.
            CanopyMaterialController.CurrentMaterialHandle = 400;
            externallyChanged.Draw();
            Require(externallyChanged.Renderable.PrimaryMaterialHandle == 400,
                "re-enabled patch applies a fresh override");
            FreeFallinPatches.RestoreStock();
            Require(externallyChanged.Renderable.PrimaryMaterialHandle == 777,
                "re-enabled patch restores the newly captured original");
        }
        finally
        {
            FreeFallinPatches.Remove(harmony);
            CanopyMaterialController.Disable();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
