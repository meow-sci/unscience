using System;
using System.Collections.Generic;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.GlassLib;

public sealed partial class GlassSubmod : ISaveParticipantSource
{
    public sealed record SavedLens(bool Enabled, float Degrees);
    public IEnumerable<ISaveParticipant> SaveParticipants => new[]
    {
        new SaveParticipant<SavedLens>("glass", () => new(FovController.IsOverrideActive, FovController.OverrideFovDegrees),
            () => { FovController.DisableOverride(); FovController.OverrideFovDegrees = 50; _fov = 50; _selectedPresetIndex = 0; },
            (state, _) =>
            {
                FovController.OverrideFovDegrees = state.Degrees;
                FovController.IsOverrideActive = state.Enabled;
                _fov = (int)state.Degrees;
                _selectedPresetIndex = FindPresetIndex(_fov);
                FovController.ApplyFov();
            }, order: 200, validate: state =>
            {
                if (!float.IsFinite(state.Degrees) || state.Degrees < 1 || state.Degrees > 179)
                    throw new InvalidOperationException("Invalid saved field of view.");
            })
    };
}
