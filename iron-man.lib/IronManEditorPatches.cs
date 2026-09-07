using System;
using System.Collections.Generic;
using System.Reflection;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.IronManLib;

/// <summary>Opt-in editor support without replacing the EVA vehicle or its animated body.</summary>
public static class IronManEditorPatches
{
    private static readonly List<(MethodBase Target, MethodInfo Patch)> Patches = new();
    private static bool _renderErrorLogged;

    public static void Apply(Harmony harmony)
    {
        if (Patches.Count != 0) return;
        try
        {
            Patch(harmony, typeof(SuperMeshRenderSystem), nameof(SuperMeshRenderSystem.ClearBuckets),
                nameof(RenderCharacter), postfix: true);
            Patch(harmony, typeof(VehicleEditor), nameof(VehicleEditor.OnFrame), nameof(ClearRootSelection), postfix: true);
            Patch(harmony, typeof(VehicleEditor), nameof(VehicleEditor.OnMouseButton), nameof(ClearRootSelection));
            Patch(harmony, typeof(VehicleEditor), nameof(VehicleEditor.OnKey), nameof(ClearRootSelection));
            Patch(harmony, typeof(VehicleEditor), nameof(VehicleEditor.UpdateSelected), nameof(ClearRootSelection));
            Patch(harmony, typeof(VehicleEditor), nameof(VehicleEditor.DeletePart), nameof(AllowDelete));
            Patch(harmony, typeof(VehicleEditor), nameof(VehicleEditor.SetFocusedTree), nameof(AllowFocus));
            Patch(harmony, typeof(VehicleEditor), "DuplicateHighlightedPart", nameof(AllowDuplicate));
            Patch(harmony, typeof(VehicleEditor), "FinalizeNewVehicle", nameof(AllowNewVehicle));
            Patch(harmony, typeof(VehicleEditor), "RequestNewVehicle", nameof(AllowNewVehicle));
            MethodInfo create = AccessTools.Method(typeof(VehicleSaveData), nameof(VehicleSaveData.Create),
                new[] { typeof(string), typeof(PartTree) })
                ?? throw new MissingMethodException(typeof(VehicleSaveData).FullName, nameof(VehicleSaveData.Create));
            Add(harmony, create, nameof(PreserveCharacter), postfix: true);
        }
        catch
        {
            Remove(harmony);
            throw;
        }
    }

    public static void Remove(Harmony harmony)
    {
        for (int i = Patches.Count - 1; i >= 0; i--)
            harmony.Unpatch(Patches[i].Target, Patches[i].Patch);
        Patches.Clear();
        _renderErrorLogged = false;
    }

    private static void Patch(Harmony harmony, Type type, string method, string patch, bool postfix = false)
    {
        MethodInfo target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.FullName, method);
        Add(harmony, target, patch, postfix);
    }

    private static void Add(Harmony harmony, MethodInfo target, string patchName, bool postfix)
    {
        MethodInfo patch = AccessTools.Method(typeof(IronManEditorPatches), patchName)!;
        var method = new HarmonyMethod(patch);
        harmony.Patch(target, prefix: postfix ? null : method, postfix: postfix ? method : null);
        Patches.Add((target, patch));
    }

    private static KittenEva? GetKitten(VehicleEditor? editor)
    {
        return editor?.ExistingVehicle is KittenEva kitten && !kitten.IsDisposed
            && IronManSubmod.Instance?.IsConfigured(kitten) == true ? kitten : null;
    }

    private static bool IsBody(Part? part, KittenEva kitten)
    {
        return part != null && (ReferenceEquals(part, kitten.Parts.Root)
            || ReferenceEquals(part.FullPart, kitten.Parts.Root));
    }

    // The editor manipulates the existing PartTree in place. The character's collider/root must
    // remain its root: replacing it preserves KittenEva's type but corrupts character simulation.
    private static void ClearRootSelection(VehicleEditor __instance)
    {
        KittenEva? kitten = GetKitten(__instance);
        if (kitten == null) return;
        if (IsBody(__instance.Highlighted, kitten))
        {
            __instance.Highlighted!.Grabbed = false;
            __instance.Highlighted.Highlighted = false;
            __instance.Highlighted.Selected = false;
            __instance.Highlighted = null;
        }
        if (IsBody(__instance.Selected, kitten)) __instance.Selected = null;
    }

    private static bool AllowDelete(VehicleEditor __instance, Part part)
    {
        KittenEva? kitten = GetKitten(__instance);
        return kitten == null || !IsBody(part, kitten);
    }

    private static bool AllowFocus(VehicleEditor __instance, PartTree newFocus)
    {
        KittenEva? kitten = GetKitten(__instance);
        return kitten == null || ReferenceEquals(newFocus, kitten.Parts);
    }

    private static bool AllowDuplicate(VehicleEditor __instance)
    {
        KittenEva? kitten = GetKitten(__instance);
        return kitten == null || !IsBody(__instance.Highlighted, kitten);
    }

    private static bool AllowNewVehicle(VehicleEditor __instance) => GetKitten(__instance) == null;

    private static void PreserveCharacter(PartTree tree, VehicleSaveData __result)
    {
        // Connector persistence must keep its character identity even after explicit flight
        // support is disabled. Match a live kitten's actual root, never a copied equipment tree.
        if (IronManConnectors.GetOwned(tree.Root).Count == 0) return;
        foreach (Vehicle vehicle in VehicleProvider.GetAllVehicles(includeDebris: true))
        {
            if (vehicle is KittenEva kitten && !kitten.IsDisposed
                && ReferenceEquals(tree.Root, kitten.Parts.Root))
            {
                __result.Character = kitten.Character.Id;
                return;
            }
        }
    }

    // Program.RenderEditor clears the immediate character buckets after ordinary part render
    // data is prepared. Submit here, before the prepass; submitting in AfterGui would be erased.
    private static void RenderCharacter(SuperMeshRenderSystem __instance)
    {
        VehicleEditor? editor = Program.Editor;
        KittenEva? kitten = GetKitten(editor);
        if (kitten == null || !ReferenceEquals(__instance, Program.Instance.SuperMeshRenderSystem)
            || !ReferenceEquals(Program.RenderedViewport, Program.MainViewport)
            || !ReferenceEquals(editor!.EditingSpace.Parts, kitten.Parts)) return;
        try
        {
            IViewport viewport = Program.RenderedViewport;
            float4x4 transform = float4x4.Pack(editor.EditingSpace.GetMatrixAsmb2Ego(viewport.GetCamera()));
            LocomotionState locomotion = kitten.LocomotionState;
            KittenAnimInputs inputs = default;
            bool hideHead = kitten.Renderable.HideHead;
            try
            {
                kitten.Renderable.HideHead = false;
                kitten.Renderable.UpdateRenderData(viewport, Program.Instance.ResourceFrameIndex, 0.0,
                    transform, double3.Zero, double3.Zero, in locomotion, in inputs);
            }
            finally { kitten.Renderable.HideHead = hideHead; }
        }
        catch (Exception ex)
        {
            if (_renderErrorLogged) return;
            _renderErrorLogged = true;
            Console.WriteLine($"iron-man: editor character rendering failed: {ex}");
        }
    }
}
