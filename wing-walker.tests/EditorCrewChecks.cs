using System;
using System.Linq;
using Brutal.GlfwApi;
using HarmonyLib;
using KSA;
using MeowSci.WingWalkerLib;

internal static class EditorCrewChecks
{
    private static int _checks;

    internal static void Run()
    {
        var harmony = new Harmony("wing-walker.tests.editor-crew");
        try
        {
            var native = Setup(Seat(ExternalCrewParts.SeatedPartId, "native-copy"));
            Require(PartCloner.CaptureForCopy(native.Seats[0].Parent.FullPart)[0] == KeyHash.Make("native-copy"),
                "unpatched cloning reproduces copied crew identity");
            WingWalkerPatches.Apply(harmony);
            CheckInteraction();
            CheckSubtreeAndCopy();
            CheckScopeAndLifecycle(harmony);
        }
        finally
        {
            WingWalkerPatches.Remove(harmony);
            Program.Editor = null;
            EditorCrewTrace.Clear();
        }
        Console.WriteLine($"PASS: { _checks } Wing Walker editor crew selection/copy checks");
    }

    private static void CheckInteraction()
    {
        foreach (string template in new[] { ExternalCrewParts.SeatedPartId, ExternalCrewParts.StandingPartId })
        {
            IVASeat seat = Seat(template, "selected");
            var scene = Setup(seat);
            Part root = seat.Parent.FullPart;
            scene.Editor.Highlighted = root;
            Click(scene.Editor, null);
            Require(seat.AssignedKittenHash != KeyHash.Zero, "hover and ignored mouse events retain crew");
            Click(scene.Editor, () => scene.Editor.Selected = root);
            Require(seat.AssignedKittenHash == KeyHash.Zero, "selecting either external mount unassigns its kitten");
            Require(EditorCrewTrace.Released.Single() == (KeyHash.Make("selected"), "draft", false),
                "native unassignment releases the exact kitten with draft ownership");
            Require(EditorCrewTrace.History.SequenceEqual(new[] { "before", "release", "after" }),
                "crew change synchronizes and marks native editor history");

            seat.AssignedKittenHash = KeyHash.Make("reassigned");
            Click(scene.Editor, null);
            Require(seat.AssignedKittenHash != KeyHash.Zero,
                "crew can be assigned after selection without being cleared every frame");
            Click(scene.Editor, () => scene.Editor.GizmoGrabbed = true);
            Require(seat.AssignedKittenHash == KeyHash.Zero, "starting a gizmo move releases reassigned crew");
            seat.AssignedKittenHash = KeyHash.Make("picked-up");
            Click(scene.Editor, () => root.Grabbed = true);
            Require(seat.AssignedKittenHash == KeyHash.Zero, "picking up an occupied mount releases its crew");
            int releases = EditorCrewTrace.Released.Count;
            Click(scene.Editor, null);
            Require(EditorCrewTrace.Released.Count == releases, "repeated interaction with an empty mount is idempotent");
        }
    }

    private static void CheckSubtreeAndCopy()
    {
        IVASeat seated = Seat(ExternalCrewParts.SeatedPartId, "seated");
        IVASeat standing = Seat(ExternalCrewParts.StandingPartId, "standing");
        IVASeat stock = Seat("CoreCommandSeat", "stock");
        IVASeat sibling = Seat(ExternalCrewParts.SeatedPartId, "sibling");
        IVASeat similar = Seat("Unscience_WingWalker_Seated_Copy", "similar");
        var scene = Setup(seated, standing, stock, sibling, similar);
        Part parent = new() { Tree = scene.Tree };
        seated.Parent.FullPart.TreeParent = parent;
        standing.Parent.FullPart.TreeParent = seated.Parent.FullPart;
        stock.Parent.FullPart.TreeParent = parent;
        similar.Parent.FullPart.TreeParent = parent;
        Click(scene.Editor, () => scene.Editor.Selected = parent);
        Require(seated.AssignedKittenHash == KeyHash.Zero && standing.AssignedKittenHash == KeyHash.Zero,
            "selecting an ordinary parent releases external mounts throughout its subtree");
        Require(stock.AssignedKittenHash == KeyHash.Make("stock")
            && sibling.AssignedKittenHash == KeyHash.Make("sibling")
            && similar.AssignedKittenHash == KeyHash.Make("similar"),
            "stock seats, siblings and similar template names retain their crew");

        seated.AssignedKittenHash = KeyHash.Make("reassigned-before-symmetry");
        standing.AssignedKittenHash = KeyHash.Make("standing-again");
        KeyHash[] cloneData = PartCloner.CaptureForCopy(parent);
        Require(cloneData.Count(hash => hash == KeyHash.Zero) == 2,
            "symmetry/copy captures empty external seats even when reassigned after selection");
        Require(!cloneData.Contains(KeyHash.Make("reassigned-before-symmetry")),
            "clones cannot carry the source external kitten's name");
        Require(seated.AssignedKittenHash == KeyHash.Zero && standing.AssignedKittenHash == KeyHash.Zero,
            "copy guard releases the source crew through normal assignment ownership");
        int releases = EditorCrewTrace.Released.Count;
        PartCloner.CaptureForCopy(parent);
        Require(EditorCrewTrace.Released.Count == releases, "repeated symmetry copies do not release crew twice");

        seated.AssignedKittenHash = KeyHash.Make("restored-by-undo-or-load");
        Require(PartCloner.CaptureForCopy(parent).Count(hash => hash == KeyHash.Zero) == 2,
            "restored native seat assignments are guarded again without stale identity caches");
        Require(stock.AssignedKittenHash == KeyHash.Make("stock") && sibling.AssignedKittenHash == KeyHash.Make("sibling"),
            "copy guard leaves other occupants intact");
    }

    private static void CheckScopeAndLifecycle(Harmony harmony)
    {
        IVASeat seat = Seat(ExternalCrewParts.StandingPartId, "unattached");
        var scene = Setup(seat);
        scene.Editor.EditingSpace = new Vehicle();
        scene.Editor.UnattachedPartTrees.Add(scene.Tree);
        scene.Editor.ExistingVehicle = new Vehicle { Id = "existing", HasLaunched = true };
        Part root = seat.Parent.FullPart;
        PartCloner.CaptureForCopy(root);
        Require(seat.AssignedKittenHash == KeyHash.Zero
            && EditorCrewTrace.Released.Single() == (KeyHash.Make("unattached"), "existing", true),
            "unattached editor trees use the existing vehicle's native mission status");

        seat.AssignedKittenHash = KeyHash.Make("flight");
        scene.Editor.UnattachedPartTrees.Clear();
        Require(PartCloner.CaptureForCopy(root)[0] == KeyHash.Make("flight"),
            "flight and foreign trees are not cleared while another tree is edited");
        Program.Editor = null;
        Require(PartCloner.CaptureForCopy(root)[0] == KeyHash.Make("flight"),
            "cloning outside the editor preserves native crew state");
        Program.Editor = new VehicleEditor();
        Click(scene.Editor, () => scene.Editor.Selected = root);
        Require(seat.AssignedKittenHash != KeyHash.Zero, "a stale editor instance cannot clear the new scene");
        Program.Editor = scene.Editor;
        scene.Editor.UnattachedPartTrees.Add(scene.Tree);
        scene.Tree.IsDisposed = true;
        PartCloner.CaptureForCopy(root);
        Require(seat.AssignedKittenHash != KeyHash.Zero, "disposed trees are untouched");
        scene.Tree.IsDisposed = false;
        WingWalkerPatches.Remove(harmony);
        Require(PartCloner.CaptureForCopy(root)[0] == KeyHash.Make("flight"), "unload removes the cloning guard");
        WingWalkerPatches.Apply(harmony);
        Require(PartCloner.CaptureForCopy(root)[0] == KeyHash.Zero, "reapply installs the guard without stale selection state");
    }

    private static (VehicleEditor Editor, PartTree Tree, IVASeat[] Seats) Setup(params IVASeat[] seats)
    {
        var tree = new PartTree(seats);
        foreach (IVASeat seat in seats) seat.Parent.FullPart.Tree = tree;
        var editor = new VehicleEditor { EditingSpace = new Vehicle(tree) { Id = "draft" } };
        Program.Editor = editor;
        EditorCrewTrace.Clear();
        return (editor, tree, seats);
    }

    private static IVASeat Seat(string template, string kitten) => new(template) { AssignedKittenHash = KeyHash.Make(kitten) };

    private static void Click(VehicleEditor editor, Action? action)
    {
        editor.MouseAction = action;
        editor.OnMouseButton(new GlfwWindow(), GlfwMouseButton.Number1, GlfwButtonAction.Release, GlfwModifier.None);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Wing Walker editor crew: " + message);
        _checks++;
    }
}
