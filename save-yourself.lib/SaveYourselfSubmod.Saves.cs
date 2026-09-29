using System;
using System.Collections.Generic;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.SaveYourselfLib;

public sealed partial class SaveYourselfSubmod : ISaveParticipantSource
{
    /// <summary>Sidecar record: the auto-save settings (prefix, enabled, interval). Countdown and history are transient.</summary>
    public const string SaveId = "save-yourself";

    public IEnumerable<ISaveParticipant> SaveParticipants => new ISaveParticipant[]
    {
        new SaveParticipant<AutoSaveSettings>(SaveId,
            capture: () => Controller.Settings.Normalized(),
            reset: Controller.Reset,
            restore: (settings, context) => Controller.Configure(settings),
            validate: ValidateSettings)
    };

    private static void ValidateSettings(AutoSaveSettings settings)
    {
        if (settings == null || !settings.IsValid)
            throw new InvalidOperationException("Invalid saved auto-save settings.");
    }
}
