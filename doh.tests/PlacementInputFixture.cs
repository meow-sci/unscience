using System.Runtime.CompilerServices;
using Brutal.GlfwApi;

namespace Brutal.GlfwApi
{
    public enum GlfwMouseButton { Number1, Number2, Number3 }
    public enum GlfwButtonAction { Release, Press }
    public enum GlfwKey { Escape, A }
    public enum GlfwKeyAction { Release, Press, Repeat }
}

namespace Brutal.Numerics
{
    public readonly record struct float2(float X, float Y);
}

namespace Brutal.ImGuiApi
{
    internal sealed class PlacementIO { public bool WantCaptureMouse; }
    internal static class ImGui
    {
        public static readonly PlacementIO IO = new();
        public static PlacementIO GetIO() => IO;
    }
}

namespace KSA
{
    internal enum CameraMode { Orbit, Map }
    internal sealed class PlacementViewport { public CameraMode Mode; }
    internal sealed class PlacementConsole { public bool IsOpen; }
    internal static class Cursor
    {
        public static Brutal.Numerics.float2 GetPosition(PlacementViewport _) => new(32, 64);
    }
    internal sealed partial class Program
    {
        public static bool DrawUI = true;
        public static bool EditorFlag;
        public static bool HasMouseInputLatch;
        public static readonly PlacementViewport MainViewport = new();
        public static PlacementViewport HoveredViewport = MainViewport;
        public static readonly PlacementConsole ConsoleWindow = new();
        public int NativeMouseCalls, NativeKeyCalls;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnMouseButton(GlfwMouseButton button, GlfwButtonAction action)
        {
            NativeMouseCalls++;
            HasMouseInputLatch = action == GlfwButtonAction.Press;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnKey(GlfwKey key, GlfwKeyAction action) => NativeKeyCalls++;
    }
}
