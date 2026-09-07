using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using MeowSci.IronManLib;

internal static class OrientationChecks
{
    private static void Require(bool ok, string message)
    {
        if (!ok) throw new Exception("Orientation: " + message);
    }

    private static void Near(double3 actual, double3 expected, string message) =>
        Require((actual - expected).Length() < 1e-10, message);

    private static void SameRotation(doubleQuat actual, doubleQuat expected, string message)
    {
        Near(double3.UnitX.Transform(actual), double3.UnitX.Transform(expected), message + " X");
        Near(double3.UnitY.Transform(actual), double3.UnitY.Transform(expected), message + " Y");
        Near(double3.UnitZ.Transform(actual), double3.UnitZ.Transform(expected), message + " Z");
    }

    public static void Run()
    {
        var harmony = new Harmony("iron-man.fixture.orientation");
        var submod = new IronManSubmod();
        IronManSubmod.Instance = submod;
        var kitten = new KittenEva();
        var untouched = new KittenEva();
        var vessel = new Vehicle();
        var controller = new OrbitController();
        var editor = new VehicleEditor { ExistingVehicle = kitten };
        var backpack = kitten.Parts.Root;
        backpack.Template.Id = IronManConnectors.BackpackTemplateId;
        var jets = new ThrusterController(backpack)
        {
            ManualControlMap = ThrusterMapFlags.PitchUp,
            GeometricMap = ThrusterMapFlags.RollRight | ThrusterMapFlags.TranslateForward
        };
        KSA.Program.Editor = editor;
        // The nullable choice getter intentionally has no NoInlining attribute, like the game.
        SameRotation(kitten.Ctrl2Body, doubleQuat.Identity, "warm stock control frame");
        controller.GetFrame(editor.EditingSpace, CameraReferenceFrame.Editor);
        controller.Scroll(1); jets.RecomputeDynamicData(); editor.CameraOffset = double3.Zero;

        for (int cycle = 0; cycle < 2; cycle++)
        {
            IronManControlFramePatches.Apply(harmony);
            IronManEditorOrientationPatches.Apply(harmony);
            IronManRcsOrientationPatches.Apply(harmony);
            SameRotation(kitten.Ctrl2Body, doubleQuat.Identity, "default-off control frame");
            jets.RecomputeDynamicData();
            Require(jets.ResolvedMap == ThrusterMapFlags.PitchUp, "default-off authored jet map");
            submod.Enabled.Add(kitten);
            CheckControlFrame(kitten, untouched, vessel);
            CheckEditorFrame(controller, editor, kitten, untouched, submod);
            CheckBackpackMapping(kitten, jets, submod);

            submod.Enabled.Remove(kitten);
            submod.Configured.Add(kitten);
            SameRotation(kitten.Ctrl2Body, doubleQuat.Identity, "disable restores stock control frame");
            jets.RecomputeDynamicData();
            Require(jets.ResolvedMap == ThrusterMapFlags.PitchUp, "disable restores original authored map");
            var configuredFrame = controller.GetFrame(editor.EditingSpace, CameraReferenceFrame.Editor);
            Near(double3.UnitZ.Transform(configuredFrame), (-double3.UnitZ).Transform(editor.EditingSpace.Asmb2Ecl),
                "configured EVA mode retains upright editor independently of rocket controls");
            editor.CameraOffset = double3.Zero;
            controller.Scroll(1);
            Near(editor.CameraOffset, (-double3.UnitZ).Transform(editor.EditingSpace.Asmb2Ecl),
                "configured EVA mode retains upright editor pan");
            submod.Configured.Remove(kitten);
            var stockFrame = controller.GetFrame(editor.EditingSpace, CameraReferenceFrame.Editor);
            Near(double3.UnitZ.Transform(stockFrame), double3.UnitX.Transform(editor.EditingSpace.Asmb2Ecl),
                "disable restores rocket editor up");
            submod.Enabled.Add(kitten);
            IronManControlFramePatches.Remove(harmony);
            IronManEditorOrientationPatches.Remove(harmony);
            IronManRcsOrientationPatches.Remove(harmony);
            SameRotation(kitten.Ctrl2Body, doubleQuat.Identity, "unload restores getter despite membership");
            jets.RecomputeDynamicData();
            Require(jets.ResolvedMap == ThrusterMapFlags.PitchUp, "unload restores authored map");
            Near(double3.UnitZ.Transform(controller.GetFrame(editor.EditingSpace, CameraReferenceFrame.Editor)),
                double3.UnitX.Transform(editor.EditingSpace.Asmb2Ecl), "unload restores whole editor frame");
            submod.Enabled.Clear();
        }
        CheckTranspilerGuards();
        KSA.Program.Editor = null;
        Console.WriteLine("PASS: rocket control basis, native choice precedence, scoped whole-editor frame/pan bounds, geometric backpack-map dispatch, cache invalidation, disable/unload/reapply, and IL guards");
    }

    private static void CheckControlFrame(KittenEva kitten, KittenEva untouched, Vehicle vessel)
    {
        doubleQuat rocket = kitten.Ctrl2Body;
        Near(double3.UnitX.Transform(rocket), -double3.UnitZ, "rocket nose points to kitten head");
        Near(double3.UnitY.Transform(rocket), double3.UnitY, "rocket right matches kitten right");
        Near(double3.UnitZ.Transform(rocket), double3.UnitX, "rocket down matches kitten face");
        var vector = new double3(4, 5, 6);
        Near(vector.Transform(rocket).Transform(rocket.Inverse()), vector, "real numerics frame round trip");
        SameRotation(untouched.Ctrl2Body, doubleQuat.Identity, "other kitten frame untouched");
        SameRotation(vessel.Ctrl2Body, doubleQuat.Identity, "normal vessel frame untouched");
        var selected = new Part { Asmb2VehicleAsmb = doubleQuat.CreateFromAxisAngle(double3.UnitZ, 0.73) };
        kitten.ControlPart = selected;
        SameRotation(kitten.Ctrl2Body, selected.Asmb2VehicleAsmb, "explicit control part overrides default");
        var connector = new Part.Connector { Asmb2VehicleAsmb = doubleQuat.CreateFromAxisAngle(double3.UnitX, -0.24) };
        kitten.ControlConnector = connector;
        SameRotation(kitten.Ctrl2Body, connector.Asmb2VehicleAsmb, "connector wins over selected part");
        kitten.ControlPart = null;
        SameRotation(kitten.Ctrl2Body, connector.Asmb2VehicleAsmb, "explicit connector alone overrides default");
        kitten.ControlConnector = null;
        SameRotation(kitten.Ctrl2Body, rocket, "clear choices restores rocket frame");
    }

    private static void CheckEditorFrame(OrbitController controller, VehicleEditor editor, KittenEva kitten,
        KittenEva untouched, IronManSubmod submod)
    {
        var space = editor.EditingSpace;
        foreach (doubleQuat assembly in new[] { doubleQuat.Identity, doubleQuat.CreateFromAxisAngle(double3.UnitY, 0.6) })
        {
            space.Asmb2Ecl = assembly;
            var rootRotation = kitten.Parts.Root.Asmb2VehicleAsmb;
            doubleQuat frame = controller.GetFrame(space, CameraReferenceFrame.Editor);
            double3 up = (-double3.UnitZ).Transform(assembly);
            Near(double3.UnitZ.Transform(frame), up, "whole editor view uses kitten head as up");
            Near(double3.UnitX.Transform(frame), double3.UnitX.Transform(assembly), "whole editor view preserves face axis");
            SameRotation(space.Asmb2Ecl, assembly, "view leaves assembly transform untouched");
            SameRotation(kitten.Parts.Root.Asmb2VehicleAsmb, rootRotation, "view leaves root geometry untouched");
            editor.CameraOffset = double3.Zero;
            controller.Scroll(1); Near(editor.CameraOffset, up, "positive pan follows rotated kitten up");
            controller.Scroll(-1); Near(editor.CameraOffset, double3.Zero, "opposite pan returns to origin");
            editor.CameraOffset = up * 51;
            controller.Scroll(1); Near(editor.CameraOffset, up * 51, "positive pan stops at projected bound");
            controller.Scroll(-1); Near(editor.CameraOffset, up * 50, "negative pan moves away from positive bound");
            editor.CameraOffset = up * -51;
            controller.Scroll(-1); Near(editor.CameraOffset, up * -51, "negative pan stops at projected bound");
            controller.Scroll(1); Near(editor.CameraOffset, up * -50, "positive pan moves away from negative bound");
        }
        SameRotation(controller.GetFrame(space, CameraReferenceFrame.Body), doubleQuat.Identity, "non-editor frame unchanged");
        var otherSpace = new VehicleEditingSpace();
        Near(double3.UnitZ.Transform(controller.GetFrame(otherSpace, CameraReferenceFrame.Editor)), double3.UnitX,
            "another editing space does not inherit current editor override");
        editor.ExistingVehicle = untouched;
        Near(double3.UnitZ.Transform(controller.GetFrame(space, CameraReferenceFrame.Editor)),
            double3.UnitX.Transform(space.Asmb2Ecl), "unenabled kitten editor retains stock basis");
        editor.ExistingVehicle = kitten;
        kitten.IsDisposed = true;
        Near(double3.UnitZ.Transform(controller.GetFrame(space, CameraReferenceFrame.Editor)),
            double3.UnitX.Transform(space.Asmb2Ecl), "disposed kitten editor retains stock basis");
        kitten.IsDisposed = false;
        IronManSubmod.Instance = null;
        SameRotation(kitten.Ctrl2Body, doubleQuat.Identity, "missing mod state preserves normal control basis");
        IronManSubmod.Instance = submod;
        editor.CameraOffset = double3.Zero;
    }

    private static void CheckBackpackMapping(KittenEva kitten, ThrusterController jets, IronManSubmod submod)
    {
        int calls = jets.GeometricMapCalls;
        jets.RecomputeDynamicData();
        Require(jets.ResolvedMap == jets.GeometricMap && jets.GeometricMapCalls == calls + 1,
            "enabled root backpack takes native geometric-map path");
        Require(jets.ManualControlMap == ThrusterMapFlags.PitchUp, "authored map field never mutated");
        kitten.ControlPart = new Part();
        jets.RecomputeDynamicData();
        Require(jets.ResolvedMap == jets.GeometricMap, "explicit control part still permits geometric jet mapping");
        kitten.ControlPart = null;
        string id = kitten.Parts.Root.Template.Id;
        kitten.Parts.Root.Template.Id = "other-template";
        jets.RecomputeDynamicData();
        Require(jets.ResolvedMap == ThrusterMapFlags.PitchUp, "other root templates preserve authored maps");
        kitten.Parts.Root.Template.Id = id;
        var attachment = new Part { Tree = kitten.Parts };
        attachment.Template.Id = id;
        var attachedJet = new ThrusterController(attachment) { ManualControlMap = ThrusterMapFlags.PitchUp };
        attachedJet.RecomputeDynamicData();
        Require(attachedJet.ResolvedMap == ThrusterMapFlags.PitchUp, "same-template attached part is not treated as root backpack");
        var owner = kitten.Parts.OwningVehicle;
        kitten.Parts.OwningVehicle = new Vehicle();
        jets.RecomputeDynamicData();
        Require(jets.ResolvedMap == ThrusterMapFlags.PitchUp, "ordinary vessel owner preserves authored maps");
        kitten.Parts.OwningVehicle = owner;
        IronManSubmod.Instance = null;
        jets.RecomputeDynamicData();
        Require(jets.ResolvedMap == ThrusterMapFlags.PitchUp, "missing mod state preserves authored maps");
        IronManSubmod.Instance = submod;
        kitten.Parts.States.Global.CacheMarker = 123;
        IronManRcsOrientationPatches.InvalidateCache(kitten);
        Require(kitten.Parts.States.Global.CacheMarker == 0, "activation invalidation resets native cached global state");
        Require(jets.ManualControlMap == ThrusterMapFlags.PitchUp, "cache reset leaves authored maps untouched");
    }

    private static void CheckTranspilerGuards()
    {
        var mapField = AccessTools.Field(typeof(ThrusterController), nameof(ThrusterController.ManualControlMap));
        var label = new DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
        var original = new CodeInstruction(OpCodes.Ldfld, mapField);
        original.labels.Add(label);
        original.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        var changed = IronManRcsOrientationPatches.ReplaceManualControlMap([original]).Single();
        Require(changed.opcode == OpCodes.Call && changed.labels.Contains(label) && changed.blocks.Count == 1,
            "map substitution retains branch/exception metadata");
        Require(original.opcode == OpCodes.Ldfld, "transpiler does not mutate incoming instructions");
        foreach (var codes in new[] { Array.Empty<CodeInstruction>(), new[] { original, new CodeInstruction(original) } })
        {
            bool rejected = false;
            try { IronManRcsOrientationPatches.ReplaceManualControlMap(codes).ToArray(); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "map transpiler rejects missing or duplicate field reads");
        }
        var scroll = AccessTools.DeclaredMethod(typeof(IronManEditorOrientationPatches), "ScrollTranspiler");
        var unitX = AccessTools.PropertyGetter(typeof(double3), nameof(double3.UnitX));
        var offset = AccessTools.Field(typeof(VehicleEditor), nameof(VehicleEditor.CameraOffset));
        var x = AccessTools.Field(typeof(double3), nameof(double3.X));
        CodeInstruction[] panCodes =
        [
            new(OpCodes.Call, unitX), new(OpCodes.Ldflda, offset), new(OpCodes.Ldfld, x),
            new(OpCodes.Call, unitX), new(OpCodes.Ldflda, offset), new(OpCodes.Ldfld, x)
        ];
        panCodes[1].labels.Add(label);
        panCodes[2].blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
        var panChanged = ((IEnumerable<CodeInstruction>)scroll.Invoke(null, [panCodes])!).ToArray();
        Require(panChanged[1].opcode == OpCodes.Ldfld && panChanged[1].labels.Contains(label)
            && panChanged[2].opcode == OpCodes.Call && panChanged[2].blocks.Count == 1,
            "editor pan substitutions retain branch and exception boundaries");
        Require(panCodes[1].opcode == OpCodes.Ldflda && panCodes[2].opcode == OpCodes.Ldfld,
            "editor transpiler does not mutate incoming instructions");
        bool scrollRejected = false;
        try { ((IEnumerable<CodeInstruction>)scroll.Invoke(null, [Array.Empty<CodeInstruction>()])!).ToArray(); }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { scrollRejected = true; }
        Require(scrollRejected, "scroll transpiler rejects unexpected source layout");
    }
}
