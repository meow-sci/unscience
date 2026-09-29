using System;
using System.Collections.Generic;

namespace MeowSci.SaveYourselfLib
{
    /// <summary>Native-free host: the real controller and adapter are linked; only game access is substituted.</summary>
    public sealed partial class SaveYourselfSubmod
    {
        public static readonly List<string> Written = new();
        public static bool GameCanSave = true;
        public static DateTime Clock = new(2026, 9, 28, 20, 30, 0);

        public AutoSaveController Controller { get; } = new(
            () => GameCanSave,
            name => { Written.Add(name); return true; },
            () => Clock);
    }
}
