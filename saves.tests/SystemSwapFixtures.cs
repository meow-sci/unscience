using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KSA
{
    public readonly record struct UniverseTime(double Seconds);
    public sealed class SystemTemplate(string id) { public string Id { get; } = id; }
    public enum SaveLoadPlan : byte { Load, LoadIntoRunning, Swap, NotInstalled }
    public enum SystemSwapRequest : byte { Applied, Queued, NotInstalled, WontFit }
    public enum SystemSwapResult : byte { Swapped, WontFit }

    /// <summary>
    /// Mirrors KSA 5916 <c>SaveSystemSwap</c>: a contained system loads in place, another installed one is
    /// queued and applied by <c>OnFrame</c> (native: after <c>ImGui.Render</c>), the latest request wins.
    /// </summary>
    public static class SaveSystemSwap
    {
        private static (SystemTemplate System, Action Apply)? _pending;
        public static readonly HashSet<string> Installed = new(StringComparer.OrdinalIgnoreCase) { "system", "contained", "other" };
        /// <summary>Systems whose bodies are all in the fixture's running system (SaveSystemCheck.Contains).</summary>
        public static readonly HashSet<string> Contained = new(StringComparer.OrdinalIgnoreCase) { "contained" };

        public static void Reset() => _pending = null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static SystemSwapRequest RequestLoad(string saveName, string savedSystemId, Action apply)
        {
            if (PlanLoad(savedSystemId) != SaveLoadPlan.LoadIntoRunning) return Request(savedSystemId, apply);
            apply();
            return SystemSwapRequest.Applied;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static SystemSwapRequest RequestJoin(string hostSystemId, Action apply) => Request(hostSystemId, apply);

        public static SaveLoadPlan PlanLoad(string savedSystemId)
        {
            if (Matches(savedSystemId)) return SaveLoadPlan.Load;
            if (!Installed.Contains(savedSystemId)) return SaveLoadPlan.NotInstalled;
            return Contained.Contains(savedSystemId) ? SaveLoadPlan.LoadIntoRunning : SaveLoadPlan.Swap;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void OnFrame()
        {
            if (_pending is not { } pending) return;
            _pending = null;
            if (Program.Instance.SwapSystem(pending.System) == SystemSwapResult.WontFit)
                Trace.Events.Add("native restart into " + pending.System.Id);
            else pending.Apply();
        }

        private static SystemSwapRequest Request(string systemId, Action apply)
        {
            if (Matches(systemId))
            {
                apply();
                return SystemSwapRequest.Applied;
            }
            if (!Installed.Contains(systemId)) return SystemSwapRequest.NotInstalled;
            _pending = (new SystemTemplate(systemId), apply);
            Trace.Events.Add("native swap queued");
            return SystemSwapRequest.Queued;
        }

        private static bool Matches(string systemId) => systemId.Length == 0
            || string.Equals(systemId, Universe.CurrentSystem?.Id ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
