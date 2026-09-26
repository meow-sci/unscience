using System;
using System.Linq;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using MeowSci.KsaAbstractions;

/// <summary>
/// KSA 5482 PartRenderFilter: the shared compaction that replaced blinky's and its-so-shiny's
/// per-module render-skip prefixes. Runs the production patches against fixture compose methods.
/// </summary>
internal static class RenderFilterChecks
{
    private static readonly IViewport View = new TestViewport(ViewportOptionFlags.RenderPartModels);
    private static readonly IViewport NoParts = new TestViewport(ViewportOptionFlags.None);

    internal static void Run()
    {
        var harmony = new Harmony("ksa-upgrade.tests.render-filter");
        var tree = new PartTreeRenderData();
        var mixed = new PartModel();
        var plainOnly = new PartModel();
        var nonRaster = new PartModel { RasterPath = false };
        var misaligned = new PartModel();
        var dynamic = new PartModelDynamic();
        var glass = new PartModelGlass();

        // A sub-part is judged by its full part, like the stock render-skip prefixes did.
        var pixelSub = new Part("sub", 4, new Part("pixel_b", 40));
        tree.AddStatic(mixed, new Part("pixel_a", 2), new Part("hull", 3), pixelSub, new Part("shiny_a", 5));
        tree.AddStatic(plainOnly, new Part("hull", 6), new Part("hull", 7));
        tree.AddStatic(nonRaster, new Part("pixel_c", 8));
        tree.AddStatic(misaligned, new Part("pixel_d", 9), new Part("hull", 10));
        tree.AddDynamic(dynamic, new PartModelDynamicModule(new Part("pixel_e", 11)), new PartModelDynamicModule(new Part("hull", 12)));
        tree.AddGlass(glass, new Part("shiny_b", 13), new Part("hull", 14));

        Frame(tree, View);
        Require(Static(mixed).SequenceEqual(new[] { 2, 3, 4, 5 }), "no owner: every instance renders");

        PartRenderFilter.Register(harmony, "blinky", part => part.Id.StartsWith("pixel_", StringComparison.Ordinal));
        PartRenderFilter.Register(harmony, "its-so-shiny", part => part.Id.StartsWith("shiny_", StringComparison.Ordinal));
        Require(PartRenderFilter.IsInstalled, "first owner installs the shared patches");

        // Another vehicle's instances already queued for the same model must stay untouched.
        Clear(mixed, plainOnly, nonRaster, misaligned, dynamic, glass);
        PartModel.ViewportData.Get(mixed, View).InstanceList.AddRange(new[] { 900, 901 });
        PartModel.ViewportData.Get(mixed, View).DentInstanceList.AddRange(new[] { 1900, 1901 });
        PartModel.ViewportData.Get(misaligned, View).DentInstanceList.Add(1999); // stray dent entry
        Frame(tree, View);
        Require(Static(mixed).SequenceEqual(new[] { 900, 901, 3 }), "both owners compact one static range in order");
        Require(Dents(mixed).SequenceEqual(new[] { 1900, 1901, 1003 }), "static dent list stays aligned");
        Require(Static(plainOnly).SequenceEqual(new[] { 6, 7 }), "ranges without hidden parts are unchanged");
        Require(Static(nonRaster).Count == 0, "non-raster submissions are left alone");
        Require(Static(misaligned).SequenceEqual(new[] { 9, 10 }), "misaligned dent lists fail open");
        Require(Dynamic(dynamic).SequenceEqual(new[] { 12 }) && DynamicDents(dynamic).SequenceEqual(new[] { 1012 }),
            "dynamic instances and dents compact together");
        Require(Glass(glass).SequenceEqual(new[] { 14 }), "glass instances compact");

        Clear(mixed, plainOnly, nonRaster, misaligned, dynamic, glass);
        Frame(tree, NoParts);
        Require(Static(mixed).Count == 0, "viewports without part models are skipped");

        PartRenderFilter.Unregister(harmony, "its-so-shiny");
        Clear(mixed, plainOnly, nonRaster, misaligned, dynamic, glass);
        Frame(tree, View);
        Require(Static(mixed).SequenceEqual(new[] { 3, 5 }) && Glass(glass).SequenceEqual(new[] { 13, 14 }),
            "removing one owner keeps the other owner's filter");

        PartRenderFilter.Unregister(harmony, "blinky");
        Require(!PartRenderFilter.IsInstalled, "last owner removes the shared patches");
        Clear(mixed, plainOnly, nonRaster, misaligned, dynamic, glass);
        Frame(tree, View);
        Require(Static(mixed).SequenceEqual(new[] { 2, 3, 4, 5 }), "unpatched compose renders everything");

        // Last: a fault disables filtering for the session instead of breaking the render loop.
        PartRenderFilter.Register(harmony, "faulty", _ => throw new InvalidOperationException("predicate bug"));
        Clear(mixed, plainOnly, nonRaster, misaligned, dynamic, glass);
        Frame(tree, View);
        Require(Static(mixed).SequenceEqual(new[] { 2, 3, 4, 5 }), "a throwing predicate fails open");
        PartRenderFilter.Unregister(harmony, "faulty");

        Console.WriteLine("PartRenderFilter: shared compaction, ordering, dent alignment, owners and fail-open passed.");
    }

    private static void Frame(PartTreeRenderData tree, IViewport viewport)
    {
        double4x4 matrix = default;
        tree.Compose(in matrix, false, viewport, 0);
        tree.ComposeDynamic(in matrix, false, viewport, 0);
        tree.ComposeGlass(in matrix, false, viewport, 0);
    }

    private static void Clear(PartModel a, PartModel b, PartModel c, PartModel d, PartModelDynamic dynamic, PartModelGlass glass)
    {
        foreach (var model in new[] { a, b, c, d })
        {
            PartModel.ViewportData.Get(model, View).InstanceList.Clear();
            PartModel.ViewportData.Get(model, View).DentInstanceList.Clear();
        }
        PartModelDynamic.ViewportData.Get(dynamic, View).InstanceList.Clear();
        PartModelDynamic.ViewportData.Get(dynamic, View).DentInstanceList.Clear();
        PartModelGlass.ViewportData.Get(glass, View).InstanceList.Clear();
    }

    private static System.Collections.Generic.List<int> Static(PartModel model) => PartModel.ViewportData.Get(model, View).InstanceList;
    private static System.Collections.Generic.List<int> Dents(PartModel model) => PartModel.ViewportData.Get(model, View).DentInstanceList;
    private static System.Collections.Generic.List<int> Dynamic(PartModelDynamic model) => PartModelDynamic.ViewportData.Get(model, View).InstanceList;
    private static System.Collections.Generic.List<int> DynamicDents(PartModelDynamic model) => PartModelDynamic.ViewportData.Get(model, View).DentInstanceList;
    private static System.Collections.Generic.List<int> Glass(PartModelGlass model) => PartModelGlass.ViewportData.Get(model, View).InstanceList;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("PartRenderFilter: " + message);
    }
}
