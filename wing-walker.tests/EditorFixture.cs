using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Brutal.GlfwApi
{
    public sealed class GlfwWindow { }
    public enum GlfwMouseButton { Number1, Number2 }
    public enum GlfwButtonAction { Press, Release }
    public enum GlfwModifier { None }
}

namespace KSA
{
    public enum EditorHistoryAction { PartSettings }

    public static class EditorCrewTrace
    {
        public static readonly List<string> History = new();
        public static readonly List<(KeyHash Kitten, string Vehicle, bool Launched)> Released = new();
        public static void Leave(KeyHash kitten, string vehicle, bool launched)
        {
            Released.Add((kitten, vehicle, launched));
            History.Add("release");
        }
        public static void Clear() { History.Clear(); Released.Clear(); }
    }

    public sealed partial class VehicleEditor
    {
        public Vehicle? ExistingVehicle;
        public Part? Selected;
        public Part? Highlighted;
        public bool GizmoGrabbed;
        public Action? MouseAction;

        // The native handler changes selection/grab state only for accepted input. Keeping the
        // state transition in the original body catches a guard that acts on mere hover/input.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool OnMouseButton(Brutal.GlfwApi.GlfwWindow window, Brutal.GlfwApi.GlfwMouseButton button,
            Brutal.GlfwApi.GlfwButtonAction action, Brutal.GlfwApi.GlfwModifier mods)
        {
            Action? pending = MouseAction;
            MouseAction = null;
            pending?.Invoke();
            return pending != null;
        }

        public static void SyncHistoryBeforeEditFor(PartTree? tree) => EditorCrewTrace.History.Add("before");
        public static void MarkHistoryStepFor(PartTree? tree, EditorHistoryAction action) =>
            EditorCrewTrace.History.Add("after");
    }

    public static class PartCloner
    {
        private static KeyHash[] _capturedCrew = Array.Empty<KeyHash>();

        // Both native symmetry batches and Alt/copy-tree reach Gather before serializing seats.
        public static KeyHash[] CaptureForCopy(Part source)
        {
            Gather(source, 1);
            return _capturedCrew;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Gather(Part source, uint firstLocalId)
        {
            _capturedCrew = source.Tree!.Modules.Get<IVASeat>().ToArray()
                .Where(seat => IsBelow(seat.Parent.FullPart, source))
                .Select(seat => seat.AssignedKittenHash).ToArray();
        }

        private static bool IsBelow(Part part, Part root)
        {
            for (Part? ancestor = part; ancestor != null; ancestor = ancestor.TreeParent)
                if (ReferenceEquals(ancestor, root)) return true;
            return false;
        }
    }
}
