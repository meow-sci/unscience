using System;
using System.Linq;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using MeowSci.KitchenSinkLib;
using MeowSci.KsaAbstractions;

internal static class CapsuleGlassChecks
{
    private static int _checks;

    internal static void Run()
    {
        var harmony = new Harmony("ksa-upgrade.tests.capsule-glass");
        var view = new TestViewport(ViewportOptionFlags.RenderPartModels);
        var tree = new PartTreeRenderData();
        var capsule = new Part("renamed-capsule-instance", 1);
        var windowA = Model("CoreCommandA_Subpart_MediumCapsuleWindowA_Model");
        var windowB = Model("CoreCommandA_Subpart_MediumCapsuleWindowB_Model");
        var hull = Model("CoreCommandA_Subpart_MediumCapsuleHull_Model");
        var similar = Model("CoreCommandA_Subpart_MediumCapsuleWindowA_Model_Clone");
        var interior = Model("cabin", true);
        var shadow = Model("shadow", true, PartModelModule.RaytracingMode.ShadowProxy);
        var glass = new PartModelGlass();
        tree.AddStatic(windowA, new Part("renamed-window-a", 2, capsule), new Part("second-capsule-window", 3));
        tree.AddStatic(windowB, new Part("renamed-window-b", 4, capsule));
        tree.AddStatic(hull, capsule);
        tree.AddStatic(similar, new Part("other-model", 5, capsule));
        tree.AddStatic(interior, new Part("interior", 6, capsule));
        tree.AddStatic(shadow, new Part("shadow", 7, capsule));
        tree.AddGlass(glass, new Part("glass", 8, capsule));
        var models = new[] { windowA, windowB, hull, similar, interior, shadow };

        IvaForceRender.Patch(harmony);
        CapsuleGlassExperiment.Apply(harmony);
        try
        {
            Require(!CapsuleGlassExperiment.Enabled && CapsuleGlassExperiment.IsAvailable, "available but off by default");
            Frame();
            Require(Count(windowA) == 2 && Count(windowB) == 1 && Count(interior) == 0, "off retains stock windows and hidden cabin");

            CapsuleGlassExperiment.SetEnabled(true);
            Frame();
            Require(Count(windowA) == 0 && Count(windowB) == 0, "both exact window model IDs are hidden across instances");
            Require(Count(hull) == 1 && Count(similar) == 1, "sibling hull and similar model ID remain visible");
            Require(Count(interior) == 1 && GlassCount() == 1, "native cabin and glass remain submitted");
            Require(!IvaForceRender.Enabled && IvaForceRender.EffectiveEnabled, "experiment leaves independent IVA preference off");
            Require(shadow.Template.Internal && Count(shadow) == 0, "shadow proxy stays native");
            Require(models.All(m => Count(m) == PartModel.ViewportData.Get(m, view).DentInstanceList.Count), "dent ranges remain aligned");

            var late = Model("late-cabin", true);
            Require(!late.Template.Internal, "new models honor the experiment visibility requirement");
            CapsuleGlassExperiment.SetEnabled(false);
            Frame();
            Require(Count(windowA) == 2 && Count(windowB) == 1 && Count(interior) == 0, "off restores exact exterior and interior visibility");
            Require(late.Template.Internal, "late model baseline is restored");

            IvaForceRender.Enabled = true;
            CapsuleGlassExperiment.SetEnabled(true);
            CapsuleGlassExperiment.SetEnabled(false);
            Require(!interior.Template.Internal && IvaForceRender.Enabled, "force-first order retains independent visibility when experiment turns off");
            CapsuleGlassExperiment.SetEnabled(true);
            IvaForceRender.Enabled = false;
            Require(!interior.Template.Internal, "turning force off cannot remove experiment ownership");
            IvaForceRender.Enabled = true;
            CapsuleGlassExperiment.SetEnabled(false);
            Require(!interior.Template.Internal, "turning force on during experiment persists after experiment turns off");
            IvaForceRender.Enabled = false;
            Require(interior.Template.Internal, "final visibility owner restores baseline");

            PartRenderFilter.Register(harmony, "other-owner", part => ReferenceEquals(part, capsule));
            CapsuleGlassExperiment.SetEnabled(true);
            Frame();
            Require(Count(hull) == 0 && GlassCount() == 0 && Count(windowA) == 0, "full-part and model owners coexist");
            CapsuleGlassExperiment.Remove(harmony);
            Frame();
            Require(PartRenderFilter.IsInstalled && Count(windowA) == 1 && Count(hull) == 0,
                "removing experiment preserves the other owner's full-part filter");
            PartRenderFilter.Unregister(harmony, "other-owner");
            Require(!PartRenderFilter.IsInstalled, "last owner removes shared patches");

            CapsuleGlassExperiment.Apply(harmony);
            CapsuleGlassExperiment.SetEnabled(true);
            view.Mode = CameraMode.IVA;
            Frame();
            Require(Count(windowA) == 0 && Count(interior) == 1 && GlassCount() == 1, "native IVA glass remains submitted while opaque windows are hidden");
            CapsuleGlassExperiment.Remove(harmony);
            IvaForceRender.Enabled = true;
            IvaForceRender.Unpatch(harmony);
            Require(interior.Template.Internal && late.Template.Internal && !IvaForceRender.EffectiveEnabled,
                "unpatch restores templates and releases all visibility ownership");
        }
        finally
        {
            CapsuleGlassExperiment.Remove(harmony);
            PartRenderFilter.Unregister(harmony, "other-owner");
            IvaForceRender.Unpatch(harmony);
            PartModel.Instances.Clear();
        }
        Console.WriteLine($"PASS: {_checks} capsule glass rendering/ownership checks; native visual acceptance remains pending.");

        int Count(PartModel model) => PartModel.ViewportData.Get(model, view).InstanceList.Count;
        int GlassCount() => PartModelGlass.ViewportData.Get(glass, view).InstanceList.Count;
        void Frame()
        {
            foreach (var model in models)
            {
                PartModel.ViewportData.Get(model, view).InstanceList.Clear();
                PartModel.ViewportData.Get(model, view).DentInstanceList.Clear();
            }
            PartModelGlass.ViewportData.Get(glass, view).InstanceList.Clear();
            double4x4 matrix = default;
            tree.Compose(in matrix, false, view, 0);
            tree.ComposeGlass(in matrix, false, view, 0);
        }
    }

    private static PartModel Model(string id, bool internalModel = false,
        PartModelModule.RaytracingMode rayTracing = PartModelModule.RaytracingMode.Disabled) =>
        new(new PartModelModule.Template { Id = id, Internal = internalModel, RayTracing = rayTracing });

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Capsule glass: " + message);
        _checks++;
    }
}
