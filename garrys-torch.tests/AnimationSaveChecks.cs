using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using MeowSci.GarrysTorchLib;
using MeowSci.ZippoLib;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

internal static class AnimationSaveChecks
{
    internal static void Run()
    {
        WeldQueue(); LightQueue();
        Console.WriteLine("PASS: weld/light active elapsed state and queued recipes round-trip with identical continuation/promotion");
    }

    private static void WeldQueue()
    {
        var original = new WeldEntry { Source = new Vehicle(), Scale = new(1) };
        var manager = new WeldAnimationManager();
        manager.Enqueue(original, new(default, default, new float3(1), new(8, 6, 4), new(3, 6, 9), new float3(3), 4, EasingType.EaseInOut));
        manager.Enqueue(original, new(default, default, new float3(1), new(-2, 1, 3), new(9, 6, 3), new float3(1), 2, EasingType.Linear));
        manager.Update(1);
        var saved = SaveJson.FromElement<List<SavedWeldAnimation>>(SaveJson.ToElement(manager.CaptureSaved(original)));
        Require(saved.Count == 2 && saved[0].Elapsed == 1 && saved[1].Duration == 2, "weld queue omitted recipe/progress");
        var restored = new WeldEntry { Source = new Vehicle(), Position = original.Position, Rotation = original.Rotation, Scale = original.Scale };
        var replay = new WeldAnimationManager(); replay.RestoreSaved(restored, saved);
        foreach (double dt in new[] { .5, 2.5, 1, 1 })
        {
            manager.Update(dt); replay.Update(dt);
            Require(original.Position == restored.Position && original.Rotation == restored.Rotation && original.Scale == restored.Scale,
                "restored weld animation diverged from uninterrupted continuation");
        }
        Require(replay.CaptureSaved(restored).Count == 0, "restored weld queue did not finish");
    }

    private static void LightQueue()
    {
        var original = new Part(); var manager = new LightAnimationManager();
        manager.Enqueue("before", new(new(1, 0, 0), new(0, 1, 0), 1, .2f, 4, EasingType.EaseInOut));
        manager.Enqueue("before", new(new(0, 0, 1), new(.3f, .2f, .8f), .9f, .7f, 2, EasingType.EaseOut));
        manager.Update(1, _ => original);
        var saved = SaveJson.FromElement<List<SavedLightAnimation>>(SaveJson.ToElement(manager.CaptureSaved("before")));
        Require(saved.Count == 2 && saved[0].Elapsed == 1 && saved[1].Duration == 2, "light queue omitted recipe/progress");
        var loaded = new Part(); loaded.Template.Color = original.Template.Color; loaded.Template.Intensity = original.Template.Intensity;
        var replay = new LightAnimationManager(); replay.RestoreSaved("new-runtime-id", saved);
        foreach (double dt in new[] { .5, 2.5, 1, 1 })
        {
            manager.Update(dt, _ => original); replay.Update(dt, _ => loaded);
            Require(original.Template.Color == loaded.Template.Color && original.Template.Intensity == loaded.Template.Intensity,
                "restored light animation diverged from uninterrupted continuation");
        }
        Require(replay.CaptureSaved("new-runtime-id").Count == 0, "restored light queue did not finish");
        saved[0].Elapsed = double.NaN;
        try { LightAnimation.FromSaved(saved[0]); throw new Exception("Accepted nonfinite elapsed."); }
        catch (InvalidOperationException) { }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
