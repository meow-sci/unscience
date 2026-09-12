using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KSA;

namespace MeowSci.HumbleArteestLib;

/// <summary>Gates only the helmet visor's draw submission, including its depth prepass.</summary>
public static class KittenVisorPatches
{
    private static MethodInfo? _original;
    private static MethodInfo? _transpiler;

    public static bool IsApplied { get; private set; }
    public static bool Hidden { get; set; }
    public static string? LastError { get; private set; }

    public static void Apply(Harmony harmony)
    {
        if (IsApplied) return;
        LastError = null;
        try
        {
            _original = AccessTools.Method(typeof(KittenRenderable), nameof(KittenRenderable.UpdateRenderData))
                ?? throw new MissingMethodException("KittenRenderable.UpdateRenderData not found.");
            _transpiler = AccessTools.Method(typeof(KittenVisorPatches), nameof(Transpile));
            harmony.Patch(_original, transpiler: new HarmonyMethod(_transpiler));
            IsApplied = true;
            Console.WriteLine("humble-arteest: Kitten visor toggle patch applied");
        }
        catch (Exception ex)
        {
            Hidden = false;
            LastError = "Visor toggle unavailable: " + ex.GetBaseException().Message;
            Console.WriteLine($"humble-arteest: {LastError}");
        }
    }

    public static void Remove(Harmony harmony)
    {
        Hidden = false;
        if (_original != null && _transpiler != null)
            harmony.Unpatch(_original, _transpiler);
        IsApplied = false;
        _original = null;
        _transpiler = null;
        LastError = null;
    }

    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var code = new List<CodeInstruction>(instructions);
        var visorField = AccessTools.Field(typeof(CharacterAvatar.Helmet), nameof(CharacterAvatar.Helmet.VisorMesh));
        var draw = AccessTools.Method(typeof(StaticMeshRenderable), nameof(StaticMeshRenderable.Draw), Type.EmptyTypes);
        var replacement = AccessTools.Method(typeof(KittenVisorPatches), nameof(DrawVisor));
        int matchIndex = -1;
        int matches = 0;

        // Match the receiver, not just Draw(): the adjacent helmet draw must stay stock.
        for (int i = 1; i < code.Count; i++)
        {
            if (code[i - 1].opcode == OpCodes.Ldfld && Equals(code[i - 1].operand, visorField)
                && code[i].Calls(draw))
            {
                matchIndex = i;
                matches++;
            }
        }

        if (matches != 1)
            throw new InvalidOperationException($"Expected one VisorMesh.Draw call, found {matches}.");

        // Same stack effect: consume the mesh reference, return void. Preserve labels/blocks.
        code[matchIndex].opcode = OpCodes.Call;
        code[matchIndex].operand = replacement;
        return code;
    }

    private static void DrawVisor(StaticMeshRenderable visor)
    {
        if (!Hidden)
            visor.Draw();
    }
}
