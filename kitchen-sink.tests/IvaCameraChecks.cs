using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using MeowSci.KitchenSinkLib;
using MeowSci.KsaAbstractions.Persistence;
using RenderCore.Input;

internal static class IvaCameraChecks
{
    private const string Id = "kitchen-sink-iva-camera";
    private static int _checks;

    public static void Run(Harmony harmony)
    {
        IvaCameraUnlock.Apply(harmony);
        try
        {
            var coordinator = new SceneSaveCoordinator(new KitchenSinkSubmod().SaveParticipants);
            var (vehicle, viewport, original) = World("Cabin");
            Require(original.Seat.HeadHidden(viewport), "native seat initially hides its head");
            Require(IvaCameraUnlock.Enable(), "valid IVA camera unlocks");
            var unlocked = viewport.IvaController;
            Require(unlocked is UnlockedIvaController && viewport.Mode == CameraMode.IVA,
                "controller remains IVA with native IVA identity for audio/rendering");
            Require(!original.Seat.HeadHidden(viewport), "real head patch reveals the detached seat occupant");
            var secondary = new GameViewport(new Camera { Following = vehicle });
            secondary.IvaController.Seat = original.Seat;
            Require(original.Seat.HeadHidden(secondary), "head patch leaves secondary viewports alone");
            IvaCameraUnlock.Speed = 2;
            unlocked.OnKey(new GlfwKeyEvent(true));
            unlocked.OnFrame(viewport, 1);
            Require(viewport.GetCamera().LocalPosition.X == 5, "movement delegates to native free controller boundary");
            ImGui.GetIO().WantTextInput = true;
            int frames = FlyController.Frames;
            unlocked.OnFrame(viewport, 1);
            ImGui.GetIO().WantTextInput = false;
            unlocked.OnFrame(viewport, 1);
            Require(FlyController.Frames == frames + 1 && viewport.GetCamera().LocalPosition.X == 5,
                "typing suspends movement/gamepad and clears held keys before focus returns");
            vehicle.Body2Cce = doubleQuat.CreateFromAxisAngle(double3.UnitZ, Math.PI / 2);
            unlocked.OnFrame(viewport, 0);
            Require(Math.Abs(viewport.GetCamera().LocalRotation.Z - vehicle.Body2Cce.Z) < 1e-9
                && viewport.GetCamera().LocalPosition.X == 5, "pose follows vessel rotation without accumulating translation");
            var saved = SaveJson.FromElement<SaveDocument>(SaveJson.ToElement(coordinator.Capture()));
            Require(saved.Features[Id].Version == 1 && State(saved).SeatIndex == 1, "separate record preserves exact seat and version");
            viewport.GetCamera().LocalPosition = new double3(99, 0, 0);
            IvaCameraUnlock.Speed = 4;
            Require(State(saved).PositionBody.X == 5 && State(saved).Speed == 2, "capture is detached from live changes");

            for (int i = 0; i < 3; i++)
            {
                coordinator.PrepareLoad(saved);
                Require(IvaCameraUnlock.Enabled, "prepare is non-mutating");
                coordinator.ResetWorld();
                Require(!IvaCameraUnlock.Enabled && ReferenceEquals(viewport.IvaController, original), "reset restores old controller before reconstruction");
                (vehicle, viewport, original) = World("Cabin");
                original.Seat = vehicle.Parts.Modules.Get<IVASeat>()[0];
                coordinator.RestoreWorld(); coordinator.FinishLoad();
                Require(IvaCameraUnlock.Enabled && !ReferenceEquals(viewport.IvaController, unlocked)
                    && viewport.GetCamera().LocalPosition.X == 5 && IvaCameraUnlock.Speed == 2,
                    "repeated replay rebinds new world and replaces pose without compounding");
                Require(ReferenceEquals(viewport.IvaController.Seat, vehicle.Parts.Modules.Get<IVASeat>()[1]), "replay resolves saved seat instead of native first seat");
            }
            IvaCameraUnlock.Disable();
            Require(ReferenceEquals(viewport.IvaController, original) && viewport.GetCamera().LocalPosition.X == 3
                && original.SwitchThisFrame && original.Seat.HeadHidden(viewport), "return restores seat pose, mouse state and head visibility");
            var off = coordinator.Capture();
            Load(coordinator, saved, "Cabin");
            Load(coordinator, off, "Cabin");
            Require(!IvaCameraUnlock.Enabled && IvaCameraUnlock.Speed == 2, "disabled save restores reusable speed");
            Load(coordinator, saved, "Cabin");
            Load(coordinator, new SaveDocument(), "Cabin");
            Require(!IvaCameraUnlock.Enabled && IvaCameraUnlock.Speed == IvaCameraUnlock.DefaultSpeed, "legacy records leave camera locked");
            Load(coordinator, saved, "Cabin");
            Load(coordinator, null, "Cabin");
            Require(!IvaCameraUnlock.Enabled, "vanilla load releases custom controller");
            Load(coordinator, saved, "Other");
            Require(!IvaCameraUnlock.Enabled && coordinator.RetainedCount == 1, "missing target is retained without controlled-vehicle fallback");
            Require(State(coordinator.Capture()).Enabled, "failed restore retains original camera record");
            coordinator.PrepareLoad(saved); coordinator.ResetWorld();
            World("Cabin");
            Universe.CurrentSystem!.All.UnsafeAsList().Add(new Vehicle("Cabin"));
            coordinator.RestoreWorld(); coordinator.FinishLoad();
            Require(!IvaCameraUnlock.Enabled && coordinator.Messages.Any(m => m.Contains("ambiguous")), "ambiguous identity is rejected");
            RejectWorld(coordinator, saved, () => KSA.Program.MainViewport.GetCamera().Following!.Parts.Root.Template.Id = "changed", "changed topology");
            RejectWorld(coordinator, saved, () => KSA.Program.MainViewport.GetCamera().Following!.IsDisposed = true, "disposed target");
            RejectWorld(coordinator, saved, () => ((GameViewport)KSA.Program.MainViewport).SetCameraMode(CameraMode.Free), "native mode mismatch");
            RejectWorld(coordinator, saved, () => KSA.Program.MainViewport.GetCamera().Following = new Vehicle("Fallback"), "native camera mismatch");

            foreach (Action<IvaCameraUnlock.SavedCamera> mutate in new Action<IvaCameraUnlock.SavedCamera>[]
            {
                s => s.Speed = -1, s => s.SeatIndex = -1, s => s.SeatIndex = 100,
                s => s.RotationBody = default, s => s.SeatPart = null,
                s => s.SeatPart!.TreePath = new[] { -1 }
            })
            {
                var bad = Clone(saved); var state = State(bad); mutate(state);
                bad.Features[Id].State = SaveJson.ToElement(state);
                Load(coordinator, bad, "Cabin");
                Require(!IvaCameraUnlock.Enabled && coordinator.RetainedCount == 1, "invalid/unresolved record rejected and retained");
            }
            var malformed = Clone(saved); malformed.Features[Id].State = SaveJson.ToElement(new { Enabled = true });
            Load(coordinator, malformed, "Cabin");
            Require(coordinator.RetainedCount == 1 && !IvaCameraUnlock.Enabled, "missing DTO properties are malformed");
            var future = Clone(saved); future.Features[Id].Version = 99;
            Load(coordinator, future, "Cabin");
            Require(coordinator.RetainedCount == 1 && !IvaCameraUnlock.Enabled, "unknown payload version retained");
            IvaCameraUnlock.Remove(harmony);
            Load(coordinator, saved, "Cabin");
            Require(coordinator.RetainedCount == 1 && !IvaCameraUnlock.Enabled, "unavailable patch reports and retains state");
            IvaCameraUnlock.Apply(harmony);
            Load(coordinator, saved, "Cabin");
            Require(IvaCameraUnlock.Enabled && coordinator.RetainedCount == 0, "later replay recovers retained record");
            var activeViewport = (GameViewport)KSA.Program.MainViewport;
            activeViewport.SetCameraMode(CameraMode.Free);
            Require(!IvaCameraUnlock.Enabled && activeViewport.IvaController is not UnlockedIvaController, "mode switch releases controller immediately");
            Load(coordinator, saved, "Cabin");
            KSA.Program.MainViewport.GetCamera().Following = new Vehicle("Other");
            IvaCameraUnlock.Update();
            Require(!IvaCameraUnlock.Enabled && KSA.Program.MainViewport.Mode == CameraMode.Orbit, "follow change exits invalid IVA safely");
            Load(coordinator, saved, "Cabin");
            activeViewport = (GameViewport)KSA.Program.MainViewport;
            var replacement = new IVAController(activeViewport.GetCamera());
            activeViewport.ReplaceIva(replacement);
            IvaCameraUnlock.Update();
            Require(!IvaCameraUnlock.Enabled && ReferenceEquals(activeViewport.IvaController, replacement)
                && activeViewport.Mode == CameraMode.IVA, "lost ownership never overwrites another controller or its mode");
            Load(coordinator, saved, "Cabin");
            IvaCameraUnlock.Remove(harmony);
            Require(!IvaCameraUnlock.Enabled && KSA.Program.MainViewport.IvaController is not UnlockedIvaController,
                "unload relinquishes controller and patch");
            Console.WriteLine($"PASS: {_checks} IVA camera ownership/persistence checks; native controls/rendering remain in-game.");
        }
        finally { IvaCameraUnlock.Remove(harmony); IvaCameraUnlock.Reset(); }
    }

    private static (Vehicle, GameViewport, IVAController) World(string id)
    {
        var vehicle = new Vehicle(id);
        vehicle.Parts.Modules.Add(new IVASeat { Parent = vehicle.Parts.Root, PositionAsmb = new double3(1, 0, 0) });
        vehicle.Parts.Modules.Add(new IVASeat { Parent = vehicle.Parts.Root, PositionAsmb = new double3(3, 0, 0) });
        Universe.CurrentSystem = new CelestialSystem(); Universe.CurrentSystem.All.UnsafeAsList().Add(vehicle);
        var viewport = new GameViewport(new Camera { Following = vehicle, LocalPosition = new double3(3, 0, 0) });
        viewport.IvaController.Seat = vehicle.Parts.Modules.Get<IVASeat>()[1];
        viewport.IvaController.LastFollowing = vehicle;
        KSA.Program.MainViewport = viewport; KSA.Program.InputViewport = viewport;
        KSA.Program.ControlledVehicle = vehicle;
        return (vehicle, viewport, viewport.IvaController);
    }
    private static void Load(SceneSaveCoordinator coordinator, SaveDocument? saved, string id)
    {
        coordinator.PrepareLoad(saved); coordinator.ResetWorld(); World(id);
        coordinator.RestoreWorld(); coordinator.FinishLoad();
    }
    private static void RejectWorld(SceneSaveCoordinator coordinator, SaveDocument saved, Action mutate, string message)
    {
        coordinator.PrepareLoad(saved); coordinator.ResetWorld(); World("Cabin"); mutate();
        coordinator.RestoreWorld(); coordinator.FinishLoad();
        Require(!IvaCameraUnlock.Enabled && coordinator.RetainedCount == 1, message);
    }
    private static IvaCameraUnlock.SavedCamera State(SaveDocument saved) => SaveJson.FromElement<IvaCameraUnlock.SavedCamera>(saved.Features[Id].State);
    private static SaveDocument Clone(SaveDocument saved) => SaveJson.FromElement<SaveDocument>(SaveJson.ToElement(saved));
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
}
