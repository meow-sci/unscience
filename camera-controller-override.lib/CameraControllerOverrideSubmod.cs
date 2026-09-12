using System;
using System.Collections.Generic;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.CameraControllerOverrideLib.Animation;
using MeowSci.CameraControllerOverrideLib.Animation.Animations;
using MeowSci.CameraControllerOverrideLib.UI;

namespace MeowSci.CameraControllerOverrideLib;

public partial class CameraControllerOverrideSubmod : ISubmod
{
    public string Name => "Camera Animations";
    public string Tooltip => "Animates the flight camera with zoom, orbit, shake, and spiral sequences.";

    private readonly KeyframeSequencePlayer _sequencePlayer = new();
    public KeyframeSequencePlayer SequencePlayer => _sequencePlayer;

    /// <summary>
    /// Static accessor for RPC integration. Set when the submod initializes, cleared on dispose.
    /// </summary>
    public static CameraControllerOverrideSubmod? Instance { get; private set; }

    // Zoom Out configuration
    private float _zoomOutSpeed = 25.0f;
    private float _zoomOutDuration = 5.0f;
    private int _zoomOutEasing = (int)EasingType.EaseOut;
    private float _zoomOutEasingPowerStart = 3.0f;
    private float _zoomOutEasingPowerEnd = 3.0f;

    // Zoom In configuration
    private float _zoomInSpeed = 25.0f;
    private float _zoomInDuration = 5.0f;
    private int _zoomInEasing = (int)EasingType.EaseOut;
    private float _zoomInEasingPowerStart = 3.0f;
    private float _zoomInEasingPowerEnd = 3.0f;

    // Zoom In To Offset configuration
    private float _zoomInOffsetSpeed = 25.0f;
    private float _zoomInOffsetDuration = 5.0f;
    private int _zoomInOffsetEasing = (int)EasingType.EaseOut;
    private float _zoomInOffsetEasingPowerStart = 3.0f;
    private float _zoomInOffsetEasingPowerEnd = 3.0f;
    private float _zoomInOffsetX = 0.0f;   // meters
    private float _zoomInOffsetY = 0.5f;   // meters (default: slightly above center)
    private float _zoomInOffsetZ = 0.0f;   // meters

    // Spiral Zoom In configuration
    private float _spiralZoomInSpeed = 25.0f;
    private float _spiralZoomInDuration = 5.0f;
    private int _spiralZoomInEasing = (int)EasingType.EaseOut;
    private float _spiralZoomInEasingPowerStart = 3.0f;
    private float _spiralZoomInEasingPowerEnd = 3.0f;
    private float _spiralZoomInDegrees = 360.0f;

    // Orbit configuration
    private float _orbitDegrees = 360.0f;
    private float _orbitDuration = 5.0f;
    private int _orbitEasing = (int)EasingType.EaseOut;
    private float _orbitEasingPowerStart = 3.0f;
    private float _orbitEasingPowerEnd = 3.0f;

    // Loopy Orbit configuration
    private float _loopyOrbitDegrees = 720.0f;
    private float _loopyLoopInterval = 90.0f;
    private float _loopyAmplitude = 50.0f;
    private float _loopyDuration = 8.0f;
    private int _loopyEasing = (int)EasingType.EaseOut;
    private float _loopyEasingPowerStart = 3.0f;
    private float _loopyEasingPowerEnd = 3.0f;

    // Shake configuration
    private float _shakeDuration = 2.0f;
    private int _shakeCount = 4;
    private float _shakeAmplitude = 5.0f;  // degrees
    private float _shakeSpeed = 1.0f;       // speed modifier
    private int _shakeEasing = (int)EasingType.EaseInOut;
    private float _shakeEasingPowerStart = 3.0f;
    private float _shakeEasingPowerEnd = 3.0f;

    // Spiral Zoom Out configuration
    private float _spiralZoomOutSpeed = 25.0f;
    private float _spiralZoomOutDuration = 5.0f;
    private int _spiralZoomOutEasing = (int)EasingType.EaseOut;
    private float _spiralZoomOutEasingPowerStart = 3.0f;
    private float _spiralZoomOutEasingPowerEnd = 3.0f;
    private float _spiralZoomOutDegrees = 360.0f;

    // Pan configuration
    private float _panOffsetX = 0.0f;
    private float _panOffsetY = 0.0f;
    private float _panOffsetZ = 0.0f;
    private float _panDuration = 5.0f;
    private int _panEasing = (int)EasingType.EaseInOut;
    private float _panEasingPowerStart = 3.0f;
    private float _panEasingPowerEnd = 3.0f;

    // Rotate configuration
    private float _rotateYaw = 0.0f;
    private float _rotatePitch = 0.0f;
    private float _rotateDuration = 3.0f;
    private int _rotateEasing = (int)EasingType.EaseInOut;
    private float _rotateEasingPowerStart = 3.0f;
    private float _rotateEasingPowerEnd = 3.0f;

    // Animation group mode
    private bool _groupMode;
    private readonly List<IKeyframeAnimation> _pendingGroupAnimations = new();

    private static readonly string[] EasingNames = { "Linear", "Ease In", "Ease Out", "Ease In-Out" };

    public void Initialize()
    {
        Instance = this;
        Console.WriteLine("camera-controller-override.lib: CameraControllerOverrideSubmod initialized");
    }

    public void Update(double dt) { }

    private void AddAnimation(IKeyframeAnimation animation)
    {
        if (_groupMode)
            _pendingGroupAnimations.Add(animation);
        else
            _sequencePlayer.AddKeyframe(animation);
    }

    private bool RenderAddButton(string id)
    {
        string label = _groupMode
            ? $" + Add to Group ##{id}"
            : $" + Add to Sequence ##{id}";
        return ImGui.Button(label);
    }

    private void RenderGroupModeUI()
    {
        if (!_groupMode)
        {
            if (ImGui.Button(" Start Building Group ##cco_grp"))
            {
                _groupMode = true;
                _pendingGroupAnimations.Clear();
            }
            ImGui.SameLine();
            ImGui.TextColored(new float4(0.6f, 0.6f, 0.6f, 1.0f),
                "Combine animations to play simultaneously");
            return;
        }

        ImGui.TextColored(new float4(1.0f, 0.9f, 0.3f, 1.0f),
            "GROUP MODE ACTIVE — use Add to Group buttons above");
        ImGui.Spacing();

        if (_pendingGroupAnimations.Count == 0)
        {
            ImGui.TextColored(new float4(0.6f, 0.6f, 0.6f, 1.0f),
                "No animations added yet.");
        }
        else
        {
            ImGui.Text($"Pending ({_pendingGroupAnimations.Count}):");
            for (int i = 0; i < _pendingGroupAnimations.Count; i++)
            {
                var anim = _pendingGroupAnimations[i];
                ImGui.BulletText($"{anim.Name} ({anim.DurationSeconds:F1}s)");
                ImGui.SameLine();
                if (ImGui.SmallButton($"x##grp_rm_{i}"))
                {
                    _pendingGroupAnimations.RemoveAt(i);
                    i--;
                }
            }
        }

        ImGui.Spacing();

        bool canFinish = _pendingGroupAnimations.Count >= 2;
        if (!canFinish) ImGui.BeginDisabled();
        if (ImGui.Button(" Finish Group ##cco_grp_fin"))
        {
            var group = new AnimationGroup();
            foreach (var anim in _pendingGroupAnimations)
                group.Add(anim);
            _sequencePlayer.AddKeyframe(group);
            _pendingGroupAnimations.Clear();
            _groupMode = false;
        }
        if (!canFinish) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(" Cancel ##cco_grp_cancel"))
        {
            _pendingGroupAnimations.Clear();
            _groupMode = false;
        }
    }

    public void Dispose()
    {
        Console.WriteLine("camera-controller-override.lib: CameraControllerOverrideSubmod disposed");
        Instance = null;
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##cco_content");

        ImGui.SeparatorText("Zoom Animations");

        if (ImGui.CollapsingHeader("Zoom Out"))
        {
            if (RenderZoomParamsTable("zoomout", ref _zoomOutSpeed, ref _zoomOutDuration, ref _zoomOutEasing, ref _zoomOutEasingPowerStart, ref _zoomOutEasingPowerEnd))
                AddAnimation(new ZoomOutAnimation(
                    speedMetersPerSecond: _zoomOutSpeed,
                    durationSeconds: _zoomOutDuration,
                    easing: (EasingType)_zoomOutEasing,
                    easingPowerStart: _zoomOutEasingPowerStart,
                    easingPowerEnd: _zoomOutEasingPowerEnd));
        }

        if (ImGui.CollapsingHeader("Zoom In"))
        {
            if (RenderZoomParamsTable("zoomin", ref _zoomInSpeed, ref _zoomInDuration, ref _zoomInEasing, ref _zoomInEasingPowerStart, ref _zoomInEasingPowerEnd))
                AddAnimation(new ZoomInAnimation(
                    speedMetersPerSecond: _zoomInSpeed,
                    durationSeconds: _zoomInDuration,
                    easing: (EasingType)_zoomInEasing,
                    easingPowerStart: _zoomInEasingPowerStart,
                    easingPowerEnd: _zoomInEasingPowerEnd));
        }

        if (ImGui.CollapsingHeader("Zoom In To Offset"))
        {
            if (RenderZoomInToOffsetSection("zoomoffset", ref _zoomInOffsetSpeed, ref _zoomInOffsetDuration, ref _zoomInOffsetEasing, ref _zoomInOffsetEasingPowerStart, ref _zoomInOffsetEasingPowerEnd, ref _zoomInOffsetX, ref _zoomInOffsetY, ref _zoomInOffsetZ))
                AddAnimation(new ZoomInToOffsetAnimation(
                    speedMetersPerSecond: _zoomInOffsetSpeed,
                    durationSeconds: _zoomInOffsetDuration,
                    easing: (EasingType)_zoomInOffsetEasing,
                    offsetX: _zoomInOffsetX,
                    offsetY: _zoomInOffsetY,
                    offsetZ: _zoomInOffsetZ,
                    easingPowerStart: _zoomInOffsetEasingPowerStart,
                    easingPowerEnd: _zoomInOffsetEasingPowerEnd));
        }

        if (ImGui.CollapsingHeader("Spiral Zoom Out"))
        {
            if (RenderSpiralZoomParamsTable("spiralout", ref _spiralZoomOutSpeed, ref _spiralZoomOutDuration, ref _spiralZoomOutEasing, ref _spiralZoomOutEasingPowerStart, ref _spiralZoomOutEasingPowerEnd, ref _spiralZoomOutDegrees))
                AddAnimation(new SpiralZoomOutAnimation(
                    speedMetersPerSecond: _spiralZoomOutSpeed,
                    durationSeconds: _spiralZoomOutDuration,
                    easing: (EasingType)_spiralZoomOutEasing,
                    spiralDegrees: _spiralZoomOutDegrees,
                    easingPowerStart: _spiralZoomOutEasingPowerStart,
                    easingPowerEnd: _spiralZoomOutEasingPowerEnd));
        }

        if (ImGui.CollapsingHeader("Spiral Zoom In"))
        {
            if (RenderSpiralZoomParamsTable("spiralin", ref _spiralZoomInSpeed, ref _spiralZoomInDuration, ref _spiralZoomInEasing, ref _spiralZoomInEasingPowerStart, ref _spiralZoomInEasingPowerEnd, ref _spiralZoomInDegrees))
                AddAnimation(new SpiralZoomInAnimation(
                    speedMetersPerSecond: _spiralZoomInSpeed,
                    durationSeconds: _spiralZoomInDuration,
                    easing: (EasingType)_spiralZoomInEasing,
                    spiralDegrees: _spiralZoomInDegrees,
                    easingPowerStart: _spiralZoomInEasingPowerStart,
                    easingPowerEnd: _spiralZoomInEasingPowerEnd));
        }

        ImGui.SeparatorText("Orbit Animations");

        if (ImGui.CollapsingHeader("Orbit"))
        {
            if (RenderOrbitParamsTable("orbit", ref _orbitDegrees, ref _orbitDuration, ref _orbitEasing, ref _orbitEasingPowerStart, ref _orbitEasingPowerEnd))
                AddAnimation(new OrbitAnimation(
                    degrees: _orbitDegrees,
                    durationSeconds: _orbitDuration,
                    easing: (EasingType)_orbitEasing,
                    easingPowerStart: _orbitEasingPowerStart,
                    easingPowerEnd: _orbitEasingPowerEnd));
        }

        if (ImGui.CollapsingHeader("Loopy Orbit"))
        {
            if (RenderLoopyOrbitParamsTable("loopy", ref _loopyOrbitDegrees, ref _loopyLoopInterval, ref _loopyAmplitude, ref _loopyDuration, ref _loopyEasing, ref _loopyEasingPowerStart, ref _loopyEasingPowerEnd))
                AddAnimation(new LoopyOrbitAnimation(
                    degrees: _loopyOrbitDegrees,
                    loopIntervalDegrees: _loopyLoopInterval,
                    amplitudeMeters: _loopyAmplitude,
                    durationSeconds: _loopyDuration,
                    easing: (EasingType)_loopyEasing,
                    easingPowerStart: _loopyEasingPowerStart,
                    easingPowerEnd: _loopyEasingPowerEnd));
        }

        ImGui.SeparatorText("Effects");

        if (ImGui.CollapsingHeader("Shake"))
        {
            if (RenderShakeParamsTable("shake", ref _shakeDuration, ref _shakeCount, ref _shakeAmplitude, ref _shakeSpeed, ref _shakeEasing, ref _shakeEasingPowerStart, ref _shakeEasingPowerEnd))
                AddAnimation(new ShakeAnimation(
                    durationSeconds: _shakeDuration,
                    shakeCount: _shakeCount,
                    amplitudeDegrees: _shakeAmplitude,
                    shakeSpeed: _shakeSpeed,
                    easing: (EasingType)_shakeEasing,
                    easingPowerStart: _shakeEasingPowerStart,
                    easingPowerEnd: _shakeEasingPowerEnd));
        }

        ImGui.SeparatorText("Movement Animations");

        if (ImGui.CollapsingHeader("Pan"))
        {
            if (RenderPanParamsTable("pan", ref _panOffsetX, ref _panOffsetY, ref _panOffsetZ,
                ref _panDuration, ref _panEasing, ref _panEasingPowerStart, ref _panEasingPowerEnd))
                AddAnimation(new PanAnimation(
                    offsetX: _panOffsetX,
                    offsetY: _panOffsetY,
                    offsetZ: _panOffsetZ,
                    durationSeconds: _panDuration,
                    easing: (EasingType)_panEasing,
                    easingPowerStart: _panEasingPowerStart,
                    easingPowerEnd: _panEasingPowerEnd));
        }

        if (ImGui.CollapsingHeader("Rotate"))
        {
            if (RenderRotateParamsTable("rotate", ref _rotateYaw, ref _rotatePitch,
                ref _rotateDuration, ref _rotateEasing, ref _rotateEasingPowerStart, ref _rotateEasingPowerEnd))
                AddAnimation(new RotateAnimation(
                    yawDegrees: _rotateYaw,
                    pitchDegrees: _rotatePitch,
                    durationSeconds: _rotateDuration,
                    easing: (EasingType)_rotateEasing,
                    easingPowerStart: _rotateEasingPowerStart,
                    easingPowerEnd: _rotateEasingPowerEnd));
        }

        ImGui.SeparatorText("Animation Group");
        RenderGroupModeUI();

        ImGui.SeparatorText("Keyframe Sequence");
        KeyframeSequencePanel.Render(_sequencePlayer);

        SubmodUI.EndContentArea();
    }

    private bool RenderZoomParamsTable(string id, ref float speed, ref float duration, ref int easing, ref float powerStart, ref float powerEnd)
    {
        var tableFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable($"##cco_zoom_{id}", 2, tableFlags))
        {
            ImGui.TableSetupColumn("##lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Speed (m/s)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##speed_{id}", ref speed, 1f, 1f, 250f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Duration (s)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##dur_{id}", ref duration, 0.1f, 1f, 30f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Easing");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.Combo($"##eas_{id}", ref easing, EasingNames, EasingNames.Length);

            var easingType = (EasingType)easing;
            if (easingType == EasingType.EaseIn || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (Start)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##ps_{id}", ref powerStart, 0.1f, 1f, 6f);
            }
            if (easingType == EasingType.EaseOut || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (End)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##pe_{id}", ref powerEnd, 0.1f, 1f, 6f);
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        ImGui.Spacing();
        return RenderAddButton(id);
    }

    private bool RenderOrbitParamsTable(string id, ref float degrees, ref float duration, ref int easing, ref float powerStart, ref float powerEnd)
    {
        var tableFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable($"##cco_orbit_{id}", 2, tableFlags))
        {
            ImGui.TableSetupColumn("##lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Degrees");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##deg_{id}", ref degrees, 1f, 90f, 1080f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Duration (s)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##dur_{id}", ref duration, 0.1f, 1f, 30f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Easing");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.Combo($"##eas_{id}", ref easing, EasingNames, EasingNames.Length);

            var easingType = (EasingType)easing;
            if (easingType == EasingType.EaseIn || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (Start)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##ps_{id}", ref powerStart, 0.1f, 1f, 6f);
            }
            if (easingType == EasingType.EaseOut || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (End)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##pe_{id}", ref powerEnd, 0.1f, 1f, 6f);
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        ImGui.Spacing();
        return RenderAddButton(id);
    }

    private bool RenderLoopyOrbitParamsTable(string id, ref float degrees, ref float loopInterval, ref float amplitude, ref float duration, ref int easing, ref float powerStart, ref float powerEnd)
    {
        var tableFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable($"##cco_loopy_{id}", 2, tableFlags))
        {
            ImGui.TableSetupColumn("##lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Degrees");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##deg_{id}", ref degrees, 1f, 90f, 1080f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Loop Interval (°)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##li_{id}", ref loopInterval, 1f, 10f, 180f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Amplitude (m)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##amp_{id}", ref amplitude, 1f, 10f, 200f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Duration (s)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##dur_{id}", ref duration, 0.1f, 1f, 30f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Easing");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.Combo($"##eas_{id}", ref easing, EasingNames, EasingNames.Length);

            var easingType = (EasingType)easing;
            if (easingType == EasingType.EaseIn || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (Start)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##ps_{id}", ref powerStart, 0.1f, 1f, 6f);
            }
            if (easingType == EasingType.EaseOut || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (End)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##pe_{id}", ref powerEnd, 0.1f, 1f, 6f);
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        ImGui.Spacing();
        return RenderAddButton(id);
    }

    private bool RenderShakeParamsTable(string id, ref float duration, ref int count, ref float amplitude, ref float speed, ref int easing, ref float powerStart, ref float powerEnd)
    {
        var tableFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable($"##cco_shake_{id}", 2, tableFlags))
        {
            ImGui.TableSetupColumn("##lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Duration (s)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##dur_{id}", ref duration, 0.1f, 1f, 10f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Count");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragInt($"##cnt_{id}", ref count, 1f, 1, 20);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Amplitude (°)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##amp_{id}", ref amplitude, 0.1f, 1f, 45f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Speed");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##spd_{id}", ref speed, 0.05f, 0.5f, 3f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Easing");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.Combo($"##eas_{id}", ref easing, EasingNames, EasingNames.Length);

            var easingType = (EasingType)easing;
            if (easingType == EasingType.EaseIn || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (Start)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##ps_{id}", ref powerStart, 0.1f, 1f, 6f);
            }
            if (easingType == EasingType.EaseOut || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (End)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##pe_{id}", ref powerEnd, 0.1f, 1f, 6f);
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        ImGui.Spacing();
        return RenderAddButton(id);
    }

    private bool RenderSpiralZoomParamsTable(string id, ref float speed, ref float duration, ref int easing, ref float powerStart, ref float powerEnd, ref float spiralDegrees)
    {
        var tableFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable($"##cco_spiral_{id}", 2, tableFlags))
        {
            ImGui.TableSetupColumn("##lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Speed (m/s)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##speed_{id}", ref speed, 1f, 1f, 250f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Duration (s)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##dur_{id}", ref duration, 0.1f, 1f, 30f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Easing");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.Combo($"##eas_{id}", ref easing, EasingNames, EasingNames.Length);

            var easingType = (EasingType)easing;
            if (easingType == EasingType.EaseIn || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (Start)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##ps_{id}", ref powerStart, 0.1f, 1f, 6f);
            }
            if (easingType == EasingType.EaseOut || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (End)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##pe_{id}", ref powerEnd, 0.1f, 1f, 6f);
            }

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Spiral Degrees");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##sdeg_{id}", ref spiralDegrees, 1f, -1080f, 1080f);

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        ImGui.Spacing();
        return RenderAddButton(id);
    }

    private bool RenderZoomInToOffsetSection(string id, ref float speed, ref float duration, ref int easing, ref float powerStart, ref float powerEnd, ref float offsetX, ref float offsetY, ref float offsetZ)
    {
        var tableFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable($"##cco_offset_{id}", 2, tableFlags))
        {
            ImGui.TableSetupColumn("##lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Speed (m/s)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##speed_{id}", ref speed, 1f, 1f, 250f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Duration (s)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##dur_{id}", ref duration, 0.1f, 1f, 30f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Easing");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.Combo($"##eas_{id}", ref easing, EasingNames, EasingNames.Length);

            var easingType = (EasingType)easing;
            if (easingType == EasingType.EaseIn || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (Start)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##ps_{id}", ref powerStart, 0.1f, 1f, 6f);
            }
            if (easingType == EasingType.EaseOut || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (End)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##pe_{id}", ref powerEnd, 0.1f, 1f, 6f);
            }

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("X Offset (m)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##ox_{id}", ref offsetX, 0.1f, -20f, 20f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Y Offset (m)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##oy_{id}", ref offsetY, 0.1f, -20f, 20f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Z Offset (m)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##oz_{id}", ref offsetZ, 0.1f, -20f, 20f);

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        ImGui.Spacing();
        return RenderAddButton(id);
    }

    private bool RenderPanParamsTable(string id, ref float offsetX, ref float offsetY, ref float offsetZ,
        ref float duration, ref int easing, ref float powerStart, ref float powerEnd)
    {
        var tableFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable($"##cco_pan_{id}", 2, tableFlags))
        {
            ImGui.TableSetupColumn("##lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Offset X (m)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##ox_{id}", ref offsetX, 0.5f, -500f, 500f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Offset Y (m)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##oy_{id}", ref offsetY, 0.5f, -500f, 500f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Offset Z (m)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##oz_{id}", ref offsetZ, 0.5f, -500f, 500f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Duration (s)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##dur_{id}", ref duration, 0.1f, 1f, 30f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Easing");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.Combo($"##eas_{id}", ref easing, EasingNames, EasingNames.Length);

            var easingType = (EasingType)easing;
            if (easingType == EasingType.EaseIn || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (Start)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##ps_{id}", ref powerStart, 0.1f, 1f, 6f);
            }
            if (easingType == EasingType.EaseOut || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (End)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##pe_{id}", ref powerEnd, 0.1f, 1f, 6f);
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        ImGui.Spacing();
        return RenderAddButton(id);
    }

    private bool RenderRotateParamsTable(string id, ref float yaw, ref float pitch,
        ref float duration, ref int easing, ref float powerStart, ref float powerEnd)
    {
        var tableFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable($"##cco_rotate_{id}", 2, tableFlags))
        {
            ImGui.TableSetupColumn("##lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Yaw (°)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##yaw_{id}", ref yaw, 1f, -360f, 360f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Pitch (°)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##pitch_{id}", ref pitch, 1f, -90f, 90f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Duration (s)");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.DragFloat($"##dur_{id}", ref duration, 0.1f, 1f, 30f);

            ImGui.TableNextRow(); ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Easing");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            ImGui.Combo($"##eas_{id}", ref easing, EasingNames, EasingNames.Length);

            var easingType = (EasingType)easing;
            if (easingType == EasingType.EaseIn || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (Start)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##ps_{id}", ref powerStart, 0.1f, 1f, 6f);
            }
            if (easingType == EasingType.EaseOut || easingType == EasingType.EaseInOut)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding(); ImGui.Text("Power (End)");
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.DragFloat($"##pe_{id}", ref powerEnd, 0.1f, 1f, 6f);
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

        ImGui.Spacing();
        return RenderAddButton(id);
    }
}
