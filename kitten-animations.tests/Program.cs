using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using KSA;
using MeowSci.KittenAnimationsLib;
using MeowSci.KittenAnimationsLib.Ui;
using MeowSci.KsaAbstractions.Persistence;
using Scene = MeowSci.KittenAnimationsLib.KittenAnimationsSubmod.SavedAnimations;
using SavedKitten = MeowSci.KittenAnimationsLib.KittenAnimationsSubmod.SavedKittenAnimation;

internal static class Checks
{
    private static int _checks;
    private static readonly List<string> Warnings = new();
    private static SaveRestoreContext Diagnostics() => new(Warnings.Add);
    private static AnimationUiContext Panel(KittenAnimationsSubmod mod) =>
        (AnimationUiContext)typeof(KittenAnimationsSubmod).GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mod)!;
    private static void Select(KittenAnimationsSubmod mod, string? id) => typeof(KittenAnimationsSubmod)
        .GetMethod("SelectTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(mod, new object?[] { id });
    private static double Pose(KittenEva kitten)
    {
        object[] args = { kitten.Renderable._characterAvatar.Core.CharacterModel, 0.5d };
        typeof(KittenAnimationPatches).GetMethod("UpdateAnimationPrefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        return (double)args[1];
    }
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }
    private static Scene Capture(ISaveParticipant participant) => SaveJson.FromElement<Scene>(participant.CaptureState());
    private static KittenEva Kitten(string id)
    {
        var kitten = new KittenEva(id);
        Universe.CurrentSystem!.All.Add(kitten);
        return kitten;
    }
    private static void Hold(AnimationUiContext ui)
    {
        ui.Driver.Play(ui.Catalog.Groups[0].Entries[0]);
        ui.Driver.Paused = true;
        ui.Driver.PlaybackRateScale = 0.4f;
        ui.Driver.OverrideEarWeight = true;
        ui.Driver.EarWeight = 0.25f;
        ui.Expressions.Latch = true;
        ui.Expressions.Trigger(ui.Avatar, KittenExpressionController.ExpressionType.Happy, 0, ui.Random);
    }

    public static void Main()
    {
        var a = Kitten("a");
        var b = Kitten("b");
        var rocket = new Vehicle("rocket");
        Universe.CurrentSystem!.All.Add(rocket);
        KSA.Program.ControlledVehicle = a;
        var mod = new KittenAnimationsSubmod();
        mod.Initialize();
        var participant = mod.SaveParticipants.Single();
        Check(participant.SaveId == "kitten-animations" && participant.SaveVersion == 1, "existing save contract retained");
        mod.Update(0);
        var first = Panel(mod);
        Hold(first);
        mod.Update(1);
        Check(Math.Abs(Pose(a) - 0.2) < 1e-6 && first.Avatar.Core.CharacterModel.FreezeAnimation, "forced frozen body and rate reach pose prefix");
        first.Avatar.Core.CharacterModel.Clock.TimeSinceTransition = 2.5f;
        Check(first.Expressions.CurrentWeight == 1, "expression reaches held peak");
        KSA.Program.ControlledVehicle = b;
        mod.Update(0.5);
        var second = Panel(mod);
        Check(!ReferenceEquals(first, second) && first.Expressions.IsAttached, "switch retains first processor and independent context");
        Check(first.Driver.OverrideActive && first.Driver.Paused && first.Expressions.CurrentWeight == 1, "switch retains body and facial holds");
        Check(!second.Driver.OverrideActive && !second.Expressions.Latch, "new kitten has independent defaults");
        Hold(second);
        second.Driver.PlaybackRateScale = 1.5f;
        second.Expressions.Trigger(second.Avatar, KittenExpressionController.ExpressionType.Angry, 0, second.Random);
        KSA.Program.ControlledVehicle = rocket;
        mod.Update(1);
        Check(Panel(mod) == null! && KittenAnimationPatches.Drivers!.Count == 2, "rocket control leaves both background targets registered");
        Check(Math.Abs(Pose(a) - 0.2) < 1e-6 && Pose(b) == 0.75, "prefix dispatches both independent background drivers");
        Check(first.Expressions.Current == KittenExpressionController.ExpressionType.Happy
            && second.Expressions.Current == KittenExpressionController.ExpressionType.Angry, "different expressions survive camera/control switch");
        Select(mod, "a");
        Check(ReferenceEquals(Panel(mod), first), "explicit selection recovers the same context");
        first.Driver.Release(); first.Expressions.Clear(); mod.Update(0); Pose(a);
        Check(!first.Avatar.Core.CharacterModel.FreezeAnimation && first.Expressions.CurrentWeight == 0, "explicit release clears only selected kitten");
        Check(second.Driver.OverrideActive && second.Expressions.CurrentWeight == 1, "other kitten hold stays enabled");
        Hold(first); mod.Update(1); Pose(a);
        first.Avatar.Core.CharacterModel.Clock.TimeSinceTransition = 2.5f;
        Select(mod, null);
        var snapshot = participant.CaptureState();
        var saved = SaveJson.FromElement<Scene>(snapshot);
        Check(saved.Kittens!.Count == 2 && saved.SelectedKittenId == null, "capture all held targets while controlling rocket");
        Check(saved.Kittens.Single(k => k.KittenId == "a").ClipPhase == 2.5f, "capture exact kitten clock");
        first.Driver.EarWeight = 0.9f;
        Check(SaveJson.FromElement<Scene>(snapshot).Kittens![0].EarWeight == 0.25f, "capture is detached from live state");

        participant.ResetState();
        Check(KittenAnimationPatches.Drivers!.Count == 0 && !first.Expressions.IsAttached && !second.Expressions.IsAttached, "reset releases all processors and model registrations");
        Check(a.Renderable._catEarAnim.ExpressionWeight == 0.8f && !a.Renderable._characterAvatar.Core.CharacterModel.FreezeAnimation, "reset restores target-owned baseline and freeze");
        Universe.CurrentSystem = new();
        a = Kitten("a"); b = Kitten("b");
        KSA.Program.ControlledVehicle = new Vehicle("other rocket");
        participant.PrepareRestore(snapshot, Diagnostics())();
        Check(!Warnings.Any() && KittenAnimationPatches.Drivers.Count == 2, "replay all targets after native-style reconstruction, regardless of control");
        Check(Capture(participant).Kittens!.Single(k => k.KittenId == "a").ClipPhase == 2.5f, "re-save before first visible frame keeps pending phase");
        Pose(a); Pose(b); Select(mod, "a");
        var restored = Panel(mod);
        Check(restored.Driver.Paused && restored.Driver.OverrideActive && restored.Avatar.Core.CharacterModel.Clock.TimeSinceTransition == 2.5f, "frozen phase restored to exact new avatar");
        Check(restored.Expressions.Current == KittenExpressionController.ExpressionType.Happy && restored.Expressions.CurrentWeight == 1, "held facial variant restored");
        participant.ResetState();
        participant.PrepareRestore(snapshot, Diagnostics())();
        Check(a.Renderable._characterAvatar.Core.CharacterModel.AnimProcessors.Count == 1 && KittenAnimationPatches.Drivers.Count == 2, "repeated load does not duplicate ownership");

        // Runtime absence and avatar replacement preserve durable intent, release old references.
        Select(mod, "a");
        var old = Panel(mod);
        a.IsDisposed = true;
        mod.Update(1);
        Check(!old.Expressions.IsAttached && !KittenAnimationPatches.Drivers.ContainsKey(old.Avatar.Core.CharacterModel), "disposed target detaches without fallback");
        Check(Capture(participant).Kittens!.Single(k => k.KittenId == "a").ClipActive, "unavailable target retains detached intent");
        Universe.CurrentSystem!.All.Remove(a);
        a = Kitten("a");
        mod.Update(0); Pose(a);
        Check(Panel(mod).Driver.OverrideActive && Panel(mod).Expressions.Latch, "same exact id returning rebinds hold");
        var previousAvatar = a.Renderable._characterAvatar;
        a.Renderable = new();
        mod.Update(0); Pose(a);
        Check(previousAvatar.Core.CharacterModel.AnimProcessors.Count == 0 && Panel(mod).Driver.OverrideActive, "avatar replacement releases old processor and resumes hold");

        participant.ResetState();
        Universe.CurrentSystem!.All.Remove(b);
        Warnings.Clear();
        participant.PrepareRestore(snapshot, Diagnostics())();
        Check(Warnings.Count == 1 && Warnings[0].Contains("b"), "missing saved kitten reported by exact id");
        Check(Capture(participant).Kittens!.Count == 2, "missing saved intent survives re-save");
        b = Kitten("b"); mod.Update(0); Pose(b);
        Check(KittenAnimationPatches.Drivers.Count == 2, "missing target resumes if exact identity returns");
        var duplicate = Kitten("b"); mod.Update(0);
        Check(KittenAnimationPatches.Drivers.Count == 1, "ambiguous identity releases binding instead of choosing a kitten");
        Universe.CurrentSystem!.All.Remove(duplicate);

        participant.ResetState();
        KSA.Program.ControlledVehicle = a;
        var legacy = new Scene { SelectedKittenId = "b", ClipLabel = "Walk", ClipSource = "walk", ClipActive = true,
            ClipPaused = true, ClipPhase = 1.25f, Latch = true, LatchedExpression = KittenExpressionController.ExpressionType.Angry,
            LatchedClipId = "angry", LatchedVariant = 0 };
        // Use an actual old-shaped JSON document, without the added array/id fields.
        var legacyJson = JsonDocument.Parse("""
            {"SelectedKittenId":"b","ClipLabel":"Walk","ClipSource":"walk","ClipActive":true,
             "ClipPaused":true,"ClipPhase":1.25,"Latch":true,"LatchedExpression":1,"LatchedClipId":"angry","LatchedVariant":0}
            """);
        participant.PrepareRestore(legacyJson.RootElement, Diagnostics())(); Pose(b);
        Check(Capture(participant).Kittens!.Any(k => k.KittenId == "b" && k.ClipPaused), "legacy pinned payload migrates");
        Select(mod, "b");
        Check(Panel(mod).Expressions.Current == KittenExpressionController.ExpressionType.Angry, "legacy facial variant migrates");
        participant.ResetState(); legacy.SelectedKittenId = null;
        participant.PrepareRestore(SaveJson.ToElement(legacy), Diagnostics())();
        Check(Capture(participant).Kittens!.Single().KittenId == "a", "legacy follow-controlled payload resolves once");

        foreach (var invalid in new[] {
            new Scene { Kittens = new() { new() { KittenId = "a" }, new() { KittenId = "a" } } },
            new Scene { Kittens = new() { new() { KittenId = "" } } },
            new Scene { Kittens = new() { new() { KittenId = "a", ClipActive = true } } },
            new Scene { Kittens = new() { new() { KittenId = "a", PeakWeight = 2 } } } })
        {
            bool refused = false;
            try { participant.PrepareRestore(SaveJson.ToElement(invalid), Diagnostics()); }
            catch (InvalidOperationException) { refused = true; }
            Check(refused, "invalid or ambiguous payload rejected before reset/replay");
        }
        participant.ResetState();
        participant.PrepareRestore(SaveJson.ToElement(new Scene { Kittens = new() }), Diagnostics())();
        Check(Capture(participant).Kittens!.All(k => !k.ClipActive && !k.Latch), "explicit empty record does not replay legacy root");
        participant.ResetState(); // vanilla/missing record reset, without replay
        mod.Update(0);
        Check(!Panel(mod).Driver.OverrideActive && !Panel(mod).Expressions.Latch, "vanilla/new scene restores defaults");
        Panel(mod).Expressions.Trigger(Panel(mod).Avatar, KittenExpressionController.ExpressionType.Sad, 0, Panel(mod).Random);
        snapshot = participant.CaptureState(); participant.ResetState(); participant.PrepareRestore(snapshot, Diagnostics())();
        Check(Panel(mod).Expressions.Current == KittenExpressionController.ExpressionType.None, "unlatched one-shot stays stopped on load");
        mod.Dispose();
        Check(KittenAnimationPatches.Drivers == null && a.Renderable._characterAvatar.Core.CharacterModel.AnimProcessors.Count == 0, "unload releases all references and processors");
        Console.WriteLine($"Kitten animation ownership and real save adapter: {_checks} checks passed.");
    }
}
