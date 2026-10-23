using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.CameraControllerOverrideLib.Animation.Animations;

namespace MeowSci.CameraControllerOverrideLib.Animation;

/// <summary>
/// Runs multiple animations simultaneously by compositing their effects.
///
/// Position-contributing animations (Pan, Zoom, Orbit, etc.) each run in an isolated
/// virtual transform. Their offset-from-target deltas are summed to produce the
/// final composed position relative to the current target.
///
/// Rotation-only animations (Rotate, Shake) are handled specially: after LookAt rotation
/// is computed from the composed position, their yaw/pitch contributions are applied
/// on top using the current view axes.
///
/// All position tracking is target-relative so the group correctly follows a moving
/// spacecraft. The group's duration equals the longest child animation. Shorter
/// animations freeze at their final state when they complete.
/// </summary>
public class AnimationGroup : IKeyframeAnimation
{
    private readonly List<GroupEntry> _entries = new();
    private double3 _baseOffset;       // camera offset from target at init
    private doubleQuat _baseRotation;
    private string _description = "Empty group";

    // IKeyframeAnimation interface
    public string Name => "Group";
    public string Description => _description;
    public double DurationSeconds { get; private set; }
    public EasingType Easing => EasingType.Linear;
    public double EasingPowerStart => 1.0;
    public double EasingPowerEnd => 1.0;
    public Func<Controller, double3>? LookAtTargetProvider { get; set; }

    public int Count => _entries.Count;

    public IKeyframeAnimation GetAnimation(int index) => _entries[index].Animation;

    public void Add(IKeyframeAnimation animation)
    {
        _entries.Add(new GroupEntry(animation));
        RecalculateDuration();
        UpdateDescription();
    }

    public void Initialize(Controller controller, Transform3D transform)
    {
        _baseOffset = AnimationHelpers.GetTargetOffset(controller, transform);
        _baseRotation = transform.LocalRotation;

        foreach (var entry in _entries)
        {
            AnimationHelpers.SetTargetOffset(controller, transform, _baseOffset);
            transform.LocalRotation = _baseRotation;
            entry.Animation.Initialize(controller, transform);
            entry.VirtualOffset = _baseOffset;
            entry.VirtualRotation = _baseRotation;
            entry.IsFinalized = false;
        }

        AnimationHelpers.SetTargetOffset(controller, transform, _baseOffset);
        transform.LocalRotation = _baseRotation;

        Console.WriteLine($"[AnimationGroup] Initialize: {_entries.Count} animations, duration={DurationSeconds:F1}s, baseOffset={_baseOffset}");
    }

    public bool Update(Controller controller, Transform3D transform, double deltaTime, double elapsedTime)
    {
        // Children run relative to the follow target, or to a custom look-at point when set.
        double3 referenceOffset = AnimationHelpers.GetLookAtOffset(LookAtTargetProvider, controller);

        // Phase 1: Update position-contributing animations in virtual state, collect offset deltas
        double3 totalOffsetDelta = double3.Zero;

        foreach (var entry in _entries)
        {
            if (IsRotationOnly(entry.Animation))
                continue;

            if (entry.IsFinalized)
            {
                totalOffsetDelta += entry.VirtualOffset - _baseOffset;
                continue;
            }

            // Set virtual transform relative to current target
            AnimationHelpers.SetTargetOffset(controller, transform, referenceOffset + entry.VirtualOffset);
            transform.LocalRotation = entry.VirtualRotation;

            bool complete = entry.Animation.Update(controller, transform, deltaTime, elapsedTime);

            // Extract offset from current target
            entry.VirtualOffset = AnimationHelpers.GetTargetOffset(controller, transform) - referenceOffset;
            entry.VirtualRotation = transform.LocalRotation;

            if (complete)
                entry.IsFinalized = true;

            totalOffsetDelta += entry.VirtualOffset - _baseOffset;
        }

        // Phase 2: Apply composed position (target-relative)
        double3 composedOffset = referenceOffset + _baseOffset + totalOffsetDelta;
        AnimationHelpers.SetTargetOffset(controller, transform, composedOffset);

        // Phase 3: Compute LookAt rotation from composed position
        transform.LocalRotation = _baseRotation;
        AnimationHelpers.LookAtOffset(transform, composedOffset, referenceOffset);

        // Phase 4: Apply rotation animation contributions on top of LookAt
        ApplyRotationContributions(transform, elapsedTime);

        if (elapsedTime < deltaTime * 1.5)
            Console.WriteLine($"[AnimationGroup] First frame: offsetDelta={totalOffsetDelta}, pos={transform.PositionEcl}");

        if (elapsedTime >= DurationSeconds)
        {
            Console.WriteLine($"[AnimationGroup] Complete: finalPos={transform.PositionEcl}");
            return true;
        }

        return false;
    }

    public void Reset()
    {
        foreach (var entry in _entries)
        {
            entry.Animation.Reset();
            entry.VirtualOffset = double3.Zero;
            entry.VirtualRotation = doubleQuat.Identity;
            entry.IsFinalized = false;
        }
        _baseOffset = double3.Zero;
        _baseRotation = doubleQuat.Identity;
    }

    public Dictionary<string, string> GetDisplayProperties()
    {
        var names = new List<string>();
        foreach (var entry in _entries)
            names.Add(entry.Animation.Name);

        return new Dictionary<string, string>
        {
            { "Animations", string.Join(", ", names) },
            { "Count", $"{_entries.Count}" },
            { "Duration", $"{DurationSeconds:F1}s" }
        };
    }

    /// <summary>
    /// Recompute yaw/pitch contributions from rotation-only animations
    /// and apply them on top of the current LookAt rotation.
    /// </summary>
    private void ApplyRotationContributions(Transform3D transform, double elapsedTime)
    {
        foreach (var entry in _entries)
        {
            if (entry.Animation is RotateAnimation rotate)
            {
                double childT = rotate.DurationSeconds > 0
                    ? Math.Min(1.0, elapsedTime / rotate.DurationSeconds)
                    : 1.0;
                if (elapsedTime >= rotate.DurationSeconds)
                    childT = 1.0;
                double easedT = AnimationHelpers.ApplyEasing(
                    childT, rotate.Easing, rotate.EasingPowerStart, rotate.EasingPowerEnd);

                double yawRad = rotate.YawDegrees * easedT * Math.PI / 180.0;
                double pitchRad = rotate.PitchDegrees * easedT * Math.PI / 180.0;

                double3 up = double3.UnitY.Transform(transform.LocalRotation);
                double3 right = double3.UnitX.Transform(transform.LocalRotation);

                var yawQuat = doubleQuat.CreateFromAxisAngle(up, yawRad);
                var pitchQuat = doubleQuat.CreateFromAxisAngle(right, pitchRad);

                transform.LocalRotation = yawQuat * pitchQuat * transform.LocalRotation;
            }
            else if (entry.Animation is ShakeAnimation shake)
            {
                if (elapsedTime >= shake.DurationSeconds)
                    continue;

                double phase = (elapsedTime / shake.DurationSeconds)
                    * shake.ShakeCount * 2.0 * Math.PI * shake.ShakeSpeed;
                double yawOffset = Math.Sin(phase) * shake.AmplitudeDegrees;
                double yawRad = yawOffset * Math.PI / 180.0;

                double3 up = double3.UnitY.Transform(transform.LocalRotation);
                var yawQuat = doubleQuat.CreateFromAxisAngle(up, yawRad);

                transform.LocalRotation = yawQuat * transform.LocalRotation;
            }
        }
    }

    private static bool IsRotationOnly(IKeyframeAnimation anim)
        => anim is RotateAnimation or ShakeAnimation;

    private void RecalculateDuration()
    {
        double max = 0;
        foreach (var entry in _entries)
            if (entry.Animation.DurationSeconds > max)
                max = entry.Animation.DurationSeconds;
        DurationSeconds = max;
    }

    private void UpdateDescription()
    {
        var names = new List<string>();
        foreach (var entry in _entries)
            names.Add(entry.Animation.Name);
        _description = names.Count > 0
            ? string.Join(" + ", names)
            : "Empty group";
    }

    /// <summary>
    /// Tracks per-animation virtual state for offset-delta extraction.
    /// </summary>
    private sealed class GroupEntry
    {
        public IKeyframeAnimation Animation { get; }
        public double3 VirtualOffset { get; set; }       // offset from target
        public doubleQuat VirtualRotation { get; set; }
        public bool IsFinalized { get; set; }

        public GroupEntry(IKeyframeAnimation animation)
        {
            Animation = animation;
        }
    }
}
