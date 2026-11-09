using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MeowSci.WarPaintLib;
using RenderCore.Systems;

internal static class KittenVisorChecks
{
    public static void Run()
    {
        var harmony = new Harmony("unscience.tests.war-paint-visors");
        var kitten = new KSA.KittenRenderable();
        try
        {
            Require(!KittenVisorPatches.Hidden, "visor glass starts shown");
            KittenVisorPatches.Apply(harmony);
            Require(KittenVisorPatches.IsApplied && KittenVisorPatches.LastError == null, "War Paint installs the draw gate");
            kitten.UpdateRenderData(new ViewHandle(1));
            Require(kitten.Helmet.VisorMesh.Views.Count == 1 && kitten.Helmet.HelmetMesh.Views.Count == 1,
                "checked setting preserves native visor and shell submission");
            KittenVisorPatches.Hidden = true;
            kitten.UpdateRenderData(new ViewHandle(2));
            kitten.UpdateRenderData(new ViewHandle(3));
            Require(kitten.Helmet.VisorMesh.Views.Count == 1 && kitten.Helmet.HelmetMesh.Views.Count == 3,
                "unchecked setting omits glass in every view while retaining the helmet shell");
            var spawned = new KSA.KittenRenderable();
            spawned.UpdateRenderData(new ViewHandle(4));
            Require(spawned.Helmet.VisorMesh.Views.Count == 0 && spawned.Helmet.HelmetMesh.Views.Count == 1,
                "global setting also hides glass on new kittens");
            KittenVisorPatches.Hidden = false;
            kitten.UpdateRenderData(new ViewHandle(5));
            Require(kitten.Helmet.VisorMesh.Views.Count == 2, "checking the setting resumes visor submission");
            KittenVisorPatches.Hidden = true;
            KittenVisorPatches.Remove(harmony);
            Require(!KittenVisorPatches.Hidden && !KittenVisorPatches.IsApplied, "unload resets the default visible state");
            kitten.UpdateRenderData(new ViewHandle(6));
            Require(kitten.Helmet.VisorMesh.Views.Count == 3, "targeted unpatch restores native rendering");
            Console.WriteLine("PASS: War Paint visor ownership, global draw gating, new kittens and unload");
        }
        finally { KittenVisorPatches.Remove(harmony); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

namespace RenderCore.Systems
{
    public readonly record struct ViewHandle(int Index);
}

namespace KSA
{
    public sealed class StaticMeshRenderable
    {
        public readonly List<int> Views = new();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Draw(ViewHandle view) => Views.Add(view.Index);
    }

    public static class CharacterAvatar
    {
        public sealed class Helmet
        {
            public readonly StaticMeshRenderable VisorMesh = new();
            public readonly StaticMeshRenderable HelmetMesh = new();
        }
    }

    public sealed class KittenRenderable
    {
        public readonly CharacterAvatar.Helmet Helmet = new();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void UpdateRenderData(ViewHandle view)
        {
            ViewHandle drawView = view;
            Helmet.HelmetMesh.Draw(drawView);
            Helmet.VisorMesh.Draw(drawView);
        }
    }
}

namespace Brutal.Logging
{
    public static class DefaultCategory
    {
        public static readonly FixtureLog Log = new();
    }
    public sealed class FixtureLog
    {
        public void Info(string message) { }
        public void Error(string message) { }
    }
}
