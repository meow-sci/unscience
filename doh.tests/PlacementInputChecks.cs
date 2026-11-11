using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using HarmonyLib;
using KSA;
using MeowSci.DohLib.Placement;

internal static partial class Checks
{
    private static void PlacementInputChecks()
    {
        var harmony = new Harmony("doh.tests.placement");
        var program = new KSA.Program();
        object owner = new();
        int clicks = 0, cancellations = 0;
        SetWorld();
        PlacementInputPatches.Apply(harmony);
        void Claim() => PlacementInputPatches.Claim(owner, point =>
        {
            Require(point == new Brutal.Numerics.float2(32, 64), "input gate captures the original viewport cursor position");
            clicks++;
        }, () => { cancellations++; PlacementInputPatches.Release(owner); });
        void Press(GlfwMouseButton button = GlfwMouseButton.Number1) => program.OnMouseButton(button, GlfwButtonAction.Press);
        void Release(GlfwMouseButton button = GlfwMouseButton.Number1) => program.OnMouseButton(button, GlfwButtonAction.Release);
        try
        {
            Claim(); Press();
            Require(clicks == 1 && program.NativeMouseCalls == 0 && !KSA.Program.HasMouseInputLatch,
                "production Harmony prefix consumes placement before native mouse actions or latches");
            PlacementInputPatches.Release(owner); Release();
            Require(program.NativeMouseCalls == 0, "release remains consumed after a successful one-shot placement");
            Press(); Release();
            Require(program.NativeMouseCalls == 2, "ordinary mouse input resumes after placement");

            Claim(); ImGui.IO.WantCaptureMouse = true; Press(); Release();
            Require(clicks == 1 && program.NativeMouseCalls == 4, "clicking an ImGui window does not place a kitten");
            ImGui.IO.WantCaptureMouse = false;
            KSA.Program.HasMouseInputLatch = true; Press(); Release();
            Require(clicks == 1 && program.NativeMouseCalls == 6, "existing native drag ownership is preserved");
            KSA.Program.HoveredViewport = new PlacementViewport(); Press(); Release();
            Require(clicks == 1, "secondary viewports cannot trigger a main-viewport spawn");
            KSA.Program.HoveredViewport = KSA.Program.MainViewport;
            KSA.Program.MainViewport.Mode = CameraMode.Map; Press(); Release();
            Require(clicks == 1, "map clicks are excluded");
            KSA.Program.MainViewport.Mode = CameraMode.Orbit;
            KSA.Program.EditorFlag = true; Press(); Release();
            Require(clicks == 1, "editor clicks are excluded");
            KSA.Program.EditorFlag = false;
            KSA.Program.ConsoleWindow.IsOpen = true; Press(); Release();
            Require(clicks == 1, "console interaction is excluded");
            KSA.Program.ConsoleWindow.IsOpen = false;
            KSA.Program.DrawUI = false; Press(); Release();
            Require(clicks == 1, "hidden-HUD clicks cannot commit an invisible tool");
            KSA.Program.DrawUI = true;

            Claim(); int before = program.NativeMouseCalls; Press(GlfwMouseButton.Number2); Release(GlfwMouseButton.Number2);
            Require(cancellations == 1 && before == program.NativeMouseCalls, "right-click cancellation suppresses the native context action and release");
            Claim(); program.OnKey(GlfwKey.Escape, GlfwKeyAction.Press);
            Require(cancellations == 2 && program.NativeKeyCalls == 0, "Escape cancels before native pause/input handling");
            program.OnKey(GlfwKey.A, GlfwKeyAction.Press);
            Require(program.NativeKeyCalls == 1, "other keyboard input remains native");
            Claim(); before = program.NativeMouseCalls; Press();
            program.OnKey(GlfwKey.Escape, GlfwKeyAction.Press); Release();
            Require(cancellations == 3 && program.NativeMouseCalls == before,
                "Escape between an owned press and release does not leak the release to native input");

            Claim(); PlacementInputPatches.Remove(harmony);
            Require(cancellations == 4 && !PlacementInputPatches.IsApplied, "unpatch cancels the active owner");
            before = program.NativeMouseCalls; Press(); Release();
            Require(program.NativeMouseCalls == before + 2, "targeted unpatch restores native mouse dispatch");
        }
        finally
        {
            PlacementInputPatches.Remove(harmony);
            KSA.Program.HasMouseInputLatch = false;
            KSA.Program.HoveredViewport = KSA.Program.MainViewport;
            KSA.Program.MainViewport.Mode = CameraMode.Orbit;
            KSA.Program.EditorFlag = false; KSA.Program.DrawUI = true;
            KSA.Program.ConsoleWindow.IsOpen = false; ImGui.IO.WantCaptureMouse = false;
        }
    }
}
