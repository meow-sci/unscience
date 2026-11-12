using System;
using KSA;

namespace MeowSci.WingWalkerLib;

public static partial class WingWalkerPatches
{
    private readonly record struct EditorInteraction(Part? Selected, Part? Grabbed, bool GizmoGrabbed);

    private static void BeginEditorInteraction(VehicleEditor __instance, out EditorInteraction __state) =>
        __state = new(__instance.Selected,
            __instance.Highlighted?.Grabbed == true ? __instance.Highlighted : null, __instance.GizmoGrabbed);

    private static void FinishEditorInteraction(VehicleEditor __instance, EditorInteraction __state)
    {
        if (!ReferenceEquals(Program.Editor, __instance)) return;
        Part? selected = __instance.Selected;
        if (selected != null && (!ReferenceEquals(selected, __state.Selected)
            || (__instance.GizmoGrabbed && !__state.GizmoGrabbed)))
            UnassignEditorSubtree(__instance, selected);

        Part? grabbed = __instance.Highlighted;
        if (grabbed?.Grabbed == true && !ReferenceEquals(grabbed, __state.Grabbed))
            UnassignEditorSubtree(__instance, grabbed);
    }

    // Gather captures module save data before any symmetry/Alt-copy clones are built. Clearing
    // here also covers a seat reassigned after selection and mounts below an ordinary parent.
    private static void BeforeEditorClone(Part __0)
    {
        if (Program.Editor is VehicleEditor editor) UnassignEditorSubtree(editor, __0);
    }

    private static void UnassignEditorSubtree(VehicleEditor editor, Part part)
    {
        Part root = part.FullPart;
        PartTree? tree = root.Tree;
        if (tree == null || tree.IsDisposed || (!ReferenceEquals(tree, editor.EditingSpace.Parts)
            && !editor.UnattachedPartTrees.Contains(tree))) return;

        bool changed = false;
        Span<IVASeat> seats = tree.Modules.Get<IVASeat>();
        for (int i = 0; i < seats.Length; i++)
        {
            IVASeat seat = seats[i];
            if (seat.AssignedKittenHash == KeyHash.Zero || !ExternalCrewParts.IsExternal(seat)
                || !IsInEditorSubtree(seat.Parent.FullPart, root)) continue;
            if (!changed) VehicleEditor.SyncHistoryBeforeEditFor(tree);
            Vehicle.SetSeatCrew(seat, KeyHash.Zero, editor.ExistingVehicle?.Id ?? editor.EditingSpace.Id,
                editor.ExistingVehicle?.HasLaunched ?? false);
            changed = true;
        }
        if (changed) VehicleEditor.MarkHistoryStepFor(tree, EditorHistoryAction.PartSettings);
    }

    private static bool IsInEditorSubtree(Part part, Part root)
    {
        for (Part? current = part; current != null; current = current.TreeParent)
            if (ReferenceEquals(current, root)) return true;
        return false;
    }
}
