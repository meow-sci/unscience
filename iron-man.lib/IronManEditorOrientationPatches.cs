using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>Uses the kitten's up axis for the whole editor view without changing saved geometry.</summary>
public static class IronManEditorOrientationPatches
{
    private static MethodInfo? _frameTarget;
    private static MethodInfo? _scrollTarget;
    private static bool _applied;

    public static void Apply(Harmony harmony)
    {
        if (_applied) return;
        _frameTarget = AccessTools.Method(typeof(OrbitController), "GetFrame2Ecl",
            new[] { typeof(IFollowable), typeof(CameraReferenceFrame) })
            ?? throw new MissingMethodException("OrbitController.GetFrame2Ecl(IFollowable, CameraReferenceFrame)");
        _scrollTarget = AccessTools.Method(typeof(OrbitController), "EditorOnScroll")
            ?? throw new MissingMethodException("OrbitController.EditorOnScroll");
        try
        {
            harmony.Patch(_frameTarget, postfix: new HarmonyMethod(typeof(IronManEditorOrientationPatches), nameof(FramePostfix)));
            harmony.Patch(_scrollTarget, transpiler: new HarmonyMethod(typeof(IronManEditorOrientationPatches), nameof(ScrollTranspiler)));
            _applied = true;
        }
        catch
        {
            Remove(harmony);
            throw;
        }
    }

    public static void Remove(Harmony harmony)
    {
        _applied = false;
        if (_frameTarget != null)
            harmony.Unpatch(_frameTarget, AccessTools.Method(typeof(IronManEditorOrientationPatches), nameof(FramePostfix)));
        if (_scrollTarget != null)
            harmony.Unpatch(_scrollTarget, AccessTools.Method(typeof(IronManEditorOrientationPatches), nameof(ScrollTranspiler)));
        _frameTarget = null;
        _scrollTarget = null;
    }

    private static VehicleEditingSpace? GetEditingSpace()
    {
        if (!_applied) return null;
        var editor = Program.Editor;
        return editor?.ExistingVehicle is KittenEva kitten && !kitten.IsDisposed
            && IronManSubmod.Instance?.IsEnabled(kitten) == true ? editor.EditingSpace : null;
    }

    private static void FramePostfix(IFollowable focused, CameraReferenceFrame referenceFrame, ref doubleQuat __result)
    {
        var space = GetEditingSpace();
        if (space == null || !ReferenceEquals(focused, space) || referenceFrame != CameraReferenceFrame.Editor) return;
        // Stock rotates camera-frame +Z to rocket assembly +X. A kitten's up is -Z.
        // Changing only this camera basis rotates the complete viewed assembly together;
        // Asmb2Ecl and every body, equipment, connector, picking and gizmo transform stay intact.
        __result = doubleQuat.Concatenate(doubleQuat.CreateFromAxisAngle(double3.UnitX, Math.PI), space.Asmb2Ecl);
    }

    private static double3 PanAxisBody() => GetEditingSpace() == null ? double3.UnitX : -double3.UnitZ;

    private static double PanCoordinate(double3 offset)
    {
        var space = GetEditingSpace();
        if (space == null) return offset.X;
        double3 axis = (-double3.UnitZ).Transform(space.Asmb2Ecl);
        return offset.X * axis.X + offset.Y * axis.Y + offset.Z * axis.Z;
    }

    private static IEnumerable<CodeInstruction> ScrollTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        var unitX = AccessTools.PropertyGetter(typeof(double3), nameof(double3.UnitX));
        var offset = AccessTools.Field(typeof(VehicleEditor), nameof(VehicleEditor.CameraOffset));
        var x = AccessTools.Field(typeof(double3), nameof(double3.X));
        var axisHelper = AccessTools.Method(typeof(IronManEditorOrientationPatches), nameof(PanAxisBody));
        var coordinateHelper = AccessTools.Method(typeof(IronManEditorOrientationPatches), nameof(PanCoordinate));
        int axes = 0;
        int coordinates = 0;
        for (int i = 0; i < codes.Count; i++)
        {
            if (codes[i].Calls(unitX))
            {
                codes[i].operand = axisHelper;
                axes++;
            }
            if (i + 1 < codes.Count && codes[i].opcode == OpCodes.Ldflda && Equals(codes[i].operand, offset)
                && codes[i + 1].opcode == OpCodes.Ldfld && Equals(codes[i + 1].operand, x))
            {
                // Replace CameraOffset.X with projection onto the same up axis used to pan.
                // Keep instruction objects (including labels/exception blocks) in place.
                codes[i].opcode = OpCodes.Ldfld;
                codes[i + 1].opcode = OpCodes.Call;
                codes[i + 1].operand = coordinateHelper;
                coordinates++;
            }
        }
        if (axes != 2 || coordinates != 2)
            throw new InvalidOperationException($"iron-man: unexpected editor pan layout ({axes} axes, {coordinates} bounds); expected two of each.");
        return codes;
    }
}
