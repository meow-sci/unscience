using System;
using System.Linq;
using MeowSci.KsaAbstractions;
using MeowSci.CameraControllerOverrideLib.Animation.Animations;

namespace MeowSci.CameraControllerOverrideLib.Animation;

/// <summary>Explicit, version-one recipe. Excludes delegates and captured camera transforms.</summary>
public sealed class SavedAnimation
{
    public string Kind { get; set; } = "";
    public double Duration { get; set; }
    public EasingType Easing { get; set; }
    public double PowerStart { get; set; } = 3;
    public double PowerEnd { get; set; } = 3;
    public double[] Parameters { get; set; } = Array.Empty<double>();
    public SavedAnimation[] Children { get; set; } = Array.Empty<SavedAnimation>();

    public static SavedAnimation Capture(IKeyframeAnimation animation, int depth = 0)
    {
        if (depth > 8) throw new InvalidOperationException("Camera groups exceed supported nesting depth.");
        if (animation.LookAtTargetProvider != null) throw new InvalidOperationException("Custom camera target delegates cannot be saved.");
        var data = new SavedAnimation { Duration = animation.DurationSeconds, Easing = animation.Easing,
            PowerStart = animation.EasingPowerStart, PowerEnd = animation.EasingPowerEnd };
        (data.Kind, data.Parameters) = animation switch
        {
            ZoomInAnimation a => ("zoom-in", new[] { a.SpeedMetersPerSecond }),
            ZoomOutAnimation a => ("zoom-out", new[] { a.SpeedMetersPerSecond }),
            ZoomInToOffsetAnimation a => ("zoom-offset", new[] { a.SpeedMetersPerSecond, a.OffsetX, a.OffsetY, a.OffsetZ }),
            SpiralZoomInAnimation a => ("spiral-in", new[] { a.SpeedMetersPerSecond, a.SpiralDegrees }),
            SpiralZoomOutAnimation a => ("spiral-out", new[] { a.SpeedMetersPerSecond, a.SpiralDegrees }),
            OrbitAnimation a => ("orbit", new[] { a.Degrees }),
            LoopyOrbitAnimation a => ("loopy", new[] { a.Degrees, a.LoopIntervalDegrees, a.AmplitudeMeters }),
            ShakeAnimation a => ("shake", new[] { (double)a.ShakeCount, a.AmplitudeDegrees, a.ShakeSpeed }),
            PanAnimation a => ("pan", new[] { a.OffsetX, a.OffsetY, a.OffsetZ }),
            RotateAnimation a => ("rotate", new[] { a.YawDegrees, a.PitchDegrees }),
            AnimationGroup => ("group", Array.Empty<double>()),
            _ => throw new InvalidOperationException($"Unsupported camera animation {animation.GetType().Name}.")
        };
        if (animation is AnimationGroup group)
            data.Children = Enumerable.Range(0, group.Count).Select(i => Capture(group.GetAnimation(i), depth + 1)).ToArray();
        return data;
    }

    public IKeyframeAnimation Create(int depth = 0)
    {
        if (depth > 8 || Parameters == null || Children == null || Children.Length > 128 || Parameters.Any(p => !double.IsFinite(p))
            || !double.IsFinite(Duration) || Duration < 0 || !Enum.IsDefined(Easing)
            || !double.IsFinite(PowerStart) || !double.IsFinite(PowerEnd) || PowerStart <= 0 || PowerEnd <= 0)
            throw new InvalidOperationException("Invalid camera animation recipe.");
        var p = Parameters;
        if (Kind == "group")
        {
            var group = new AnimationGroup();
            foreach (var child in Children) group.Add(child.Create(depth + 1));
            return group;
        }
        return (Kind, p.Length) switch
        {
            ("zoom-in", 1) => new ZoomInAnimation(p[0], Duration, Easing, PowerStart, PowerEnd),
            ("zoom-out", 1) => new ZoomOutAnimation(p[0], Duration, Easing, PowerStart, PowerEnd),
            ("zoom-offset", 4) => new ZoomInToOffsetAnimation(p[0], Duration, Easing, p[1], p[2], p[3], PowerStart, PowerEnd),
            ("spiral-in", 2) => new SpiralZoomInAnimation(p[0], Duration, Easing, p[1], PowerStart, PowerEnd),
            ("spiral-out", 2) => new SpiralZoomOutAnimation(p[0], Duration, Easing, p[1], PowerStart, PowerEnd),
            ("orbit", 1) => new OrbitAnimation(p[0], Duration, Easing, PowerStart, PowerEnd),
            ("loopy", 3) when p[1] != 0 => new LoopyOrbitAnimation(p[0], p[1], p[2], Duration, Easing, PowerStart, PowerEnd),
            ("shake", 3) when p[0] >= 1 && p[0] <= 100000 && p[0] == Math.Truncate(p[0]) => new ShakeAnimation(Duration, (int)p[0], p[1], p[2], Easing, PowerStart, PowerEnd),
            ("pan", 3) => new PanAnimation(p[0], p[1], p[2], Duration, Easing, PowerStart, PowerEnd),
            ("rotate", 2) => new RotateAnimation(p[0], p[1], Duration, Easing, PowerStart, PowerEnd),
            _ => throw new InvalidOperationException($"Invalid or unsupported camera recipe '{Kind}'.")
        };
    }
}
