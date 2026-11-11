using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;

namespace MeowSci.WingWalkerLib;

public static partial class WingWalkerPatches
{
    // ClearBuckets is shared by main, secondary and editor render passes. Submitting here
    // keeps native character resources in the correct viewport's current immediate buckets.
    private static void AfterClearBuckets(SuperMeshRenderSystem __instance, IViewport __0)
    {
        if (!IsApplied || !ReferenceEquals(__instance, Program.Instance.SuperMeshRenderSystem)
            || !ReferenceEquals(__0, Program.RenderedViewport) || !__0.Visible
            || __0.Is(ViewportType.CharacterPortrait)) return;
        try
        {
            int frame = Program.Instance.ResourceFrameIndex;
            double dt = Program.GetPlayerDeltaTime();
            VehicleEditor? editor = Program.Editor;
            if (editor != null)
            {
                double4x4 matrix = editor.EditingSpace.GetMatrixAsmb2Ego(__0.GetCamera());
                var visited = new HashSet<PartTree>();
                DrawEditorTree(editor.EditingSpace.Parts, visited, __0, frame, dt, in matrix);
                foreach (PartTree tree in editor.UnattachedPartTrees)
                    DrawEditorTree(tree, visited, __0, frame, dt, in matrix);
                return;
            }
            ReadOnlySpan<Vehicle> vehicles = Program.VehiclesInFrame;
            for (int i = 0; i < vehicles.Length; i++)
            {
                Vehicle vehicle = vehicles[i];
                // Stock submits the controlled craft after this callback; drawing it here too
                // would double-submit crew. A null controlled craft still draws all other seats.
                if (vehicle.IsDisposed || ReferenceEquals(vehicle, Program.ControlledVehicle)
                    || vehicle.Parts.IsDisposed || !vehicle.IsLargeEnoughToRender(__0.GetCamera())) continue;
                double4x4 matrix = vehicle.GetMatrixAsmb2Ego(__0.GetCamera());
                DrawSeats(vehicle.Parts, __0, frame, dt, in matrix);
            }
        }
        catch (Exception ex) { LogRenderError(ex); }
    }

    private static void DrawEditorTree(PartTree? tree, HashSet<PartTree> visited, IViewport viewport,
        int frame, double dt, in double4x4 matrix)
    {
        if (tree != null && !tree.IsDisposed && visited.Add(tree))
            DrawSeats(tree, viewport, frame, dt, in matrix);
    }

    private static void DrawSeats(PartTree tree, IViewport viewport, int frame, double dt, in double4x4 matrix)
    {
        Span<IVASeat> seats = tree.Modules.Get<IVASeat>();
        for (int i = 0; i < seats.Length; i++)
        {
            IVASeat seat = seats[i];
            if (!ExternalCrewParts.IsExternal(seat)) continue;
            // Unassigned seats must also run: native code releases their former occupant's avatar.
            try { seat.UpdateSeatedRenderable(viewport, frame, dt, in matrix); }
            catch (Exception ex) { LogRenderError(ex); }
        }
    }

    private static void LogRenderError(Exception error)
    {
        if (_renderErrorLogged) return;
        _renderErrorLogged = true;
        LastError = error.Message;
        Console.WriteLine($"wing-walker: crew rendering failed: {error}");
    }
}
