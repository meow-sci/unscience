using System;
using System.Collections.Generic;
using System.Linq;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.CameraControllerOverrideLib.Animation;

namespace MeowSci.CameraControllerOverrideLib;

public partial class CameraControllerOverrideSubmod : ISaveParticipantSource
{
    public sealed class SavedCameraSequence
    {
        public SavedAnimation[] Keyframes { get; set; } = Array.Empty<SavedAnimation>();
        public SavedAnimation[] PendingGroup { get; set; } = Array.Empty<SavedAnimation>();
        public bool ReturnToStart { get; set; }
        public double ReturnDuration { get; set; } = 3;
        public EasingType ReturnEasing { get; set; } = EasingType.EaseInOut;
        public double PowerStart { get; set; } = 3;
        public double PowerEnd { get; set; } = 3;
    }

    public IEnumerable<ISaveParticipant> SaveParticipants => new[]
    {
        new SaveParticipant<SavedCameraSequence>("camera-controller-override", () => new()
        {
            Keyframes = _sequencePlayer.Keyframes.Select(k => SavedAnimation.Capture(k.Animation)).ToArray(),
            PendingGroup = _pendingGroupAnimations.Select(a => SavedAnimation.Capture(a)).ToArray(),
            ReturnToStart = _sequencePlayer.ReturnToStartEnabled, ReturnDuration = _sequencePlayer.ReturnToStartDuration,
            ReturnEasing = _sequencePlayer.ReturnToStartEasing, PowerStart = _sequencePlayer.ReturnToStartEasingPowerStart,
            PowerEnd = _sequencePlayer.ReturnToStartEasingPowerEnd
        }, () => { _sequencePlayer.Clear(); _pendingGroupAnimations.Clear(); _groupMode = false; },
        (state, _) =>
        {
            foreach (var recipe in state.Keyframes) _sequencePlayer.AddKeyframe(recipe.Create());
            foreach (var recipe in state.PendingGroup) _pendingGroupAnimations.Add(recipe.Create());
            _groupMode = state.PendingGroup.Length > 0;
            _sequencePlayer.ReturnToStartEnabled = state.ReturnToStart;
            _sequencePlayer.ReturnToStartDuration = state.ReturnDuration;
            _sequencePlayer.ReturnToStartEasing = state.ReturnEasing;
            _sequencePlayer.ReturnToStartEasingPowerStart = state.PowerStart;
            _sequencePlayer.ReturnToStartEasingPowerEnd = state.PowerEnd;
        }, order: 210, validate: state =>
        {
            if (state.Keyframes == null || state.PendingGroup == null || state.Keyframes.Length > 1024 || state.PendingGroup.Length > 128
                || !double.IsFinite(state.ReturnDuration) || state.ReturnDuration < 1 || state.ReturnDuration > 10
                || !Enum.IsDefined(state.ReturnEasing) || !double.IsFinite(state.PowerStart) || state.PowerStart <= 0
                || !double.IsFinite(state.PowerEnd) || state.PowerEnd <= 0)
                throw new InvalidOperationException("Invalid camera sequence settings.");
            foreach (var recipe in state.Keyframes.Concat(state.PendingGroup)) _ = recipe.Create();
        })
    };
}
