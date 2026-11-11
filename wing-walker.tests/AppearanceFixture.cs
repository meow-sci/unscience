using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Brutal.Numerics;

namespace KSA;

public sealed class AnimationAssetRef(string name)
{
    public string Name { get; } = name;
}

public sealed class CatEarAnim { public float ExpressionWeight; }
public sealed class CatConditionAnim { public bool PoseEars; }
public sealed class StaticMeshRenderable
{
    public bool Visible { get; set; } = true;
    public int DrawCalls { get; private set; }
    public void Draw() { if (Visible) DrawCalls++; }
}

public struct HelmetAttachment
{
    public bool Enabled;
    public StaticMeshRenderable? HelmetMesh;
    public StaticMeshRenderable? VisorMesh;
    public readonly bool HasAttachment => Enabled && HelmetMesh != null && VisorMesh != null;
}

public struct MmuAttachment { public bool Enabled; }

public struct CharacterAttachments
{
    public HelmetAttachment Helmet;
    public MmuAttachment Mmu;
}

public struct CharacterCore
{
    public AnimatedRenderable CharacterModel;
    public List<int> HeadMeshIndices;
    public CharacterCore(AnimatedRenderable characterModel)
    {
        CharacterModel = characterModel;
        HeadMeshIndices = [1, 4];
    }
}

public sealed class CharacterAvatar
{
    public CharacterCore Core = new(new AnimatedRenderable());
    public CharacterAttachments Attachments = new()
    {
        Helmet = new HelmetAttachment
        {
            Enabled = true,
            HelmetMesh = new StaticMeshRenderable(),
            VisorMesh = new StaticMeshRenderable()
        },
        Mmu = new MmuAttachment { Enabled = true }
    };
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
}

public readonly record struct RenderSnapshot(
    bool HelmetEnabled, bool HelmetVisible, bool VisorVisible, float EarWeight,
    bool PoseEars, bool HideMaskedMeshes, int MaskedMeshCount, bool Frozen);

public sealed class AnimatedRenderable
{
    private AnimationAssetRef? _previousAnimation;
    private float _blendElapsed, _blendDuration;
    private ulong _lastFrame = ulong.MaxValue;
    public AnimationAssetRef? CurrentAnimation { get; private set; }
    public AnimationAssetRef? EffectiveAnimation => _blendDuration <= 0 || _blendElapsed >= _blendDuration
        ? CurrentAnimation : _previousAnimation;
    public bool FreezeAnimation { get; set; }
    public bool HideMaskedMeshes { get; set; }
    public List<int> MaskedMeshIndices { get; } = new();
    public Func<RenderSnapshot>? SnapshotContext { get; set; }
    public Action? BeforeUpdateAnimation { get; set; }
    public bool ThrowOnUpdateAnimation { get; set; }
    public bool ThrowOnDraw { get; set; }
    public int PlayAnimationCalls { get; private set; }
    public int SetAnimationCalls { get; private set; }
    public int UpdateAnimationCalls { get; private set; }
    public int AnimationAdvanceCalls { get; private set; }
    public int DrawCalls { get; private set; }
    public RenderSnapshot? AtUpdateAnimation { get; private set; }
    public RenderSnapshot? AtDraw { get; private set; }

    public void PlayAnimation(AnimationAssetRef animation, float blendTime = 0f)
    {
        PlayAnimationCalls++;
        if (CurrentAnimation == null) blendTime = 0f;
        _previousAnimation = EffectiveAnimation;
        CurrentAnimation = animation;
        _blendElapsed = 0;
        _blendDuration = blendTime;
    }

    public void SetAnimation(AnimationAssetRef animation, float blendTime = 0.2f)
    {
        SetAnimationCalls++;
        if (!ReferenceEquals(CurrentAnimation, animation)) PlayAnimation(animation, blendTime);
    }

    public void UpdateAnimation(double dt)
    {
        UpdateAnimationCalls++;
        AtUpdateAnimation = SnapshotContext?.Invoke();
        BeforeUpdateAnimation?.Invoke();
        if (ThrowOnUpdateAnimation) throw new InvalidOperationException("fixture animation update failure");
        if (_lastFrame == Program.FrameNumber) return;
        _lastFrame = Program.FrameNumber;
        if (!FreezeAnimation)
        {
            AnimationAdvanceCalls++;
            _blendElapsed += (float)dt;
        }
    }

    public void Draw()
    {
        DrawCalls++;
        AtDraw = SnapshotContext?.Invoke();
        if (ThrowOnDraw) throw new InvalidOperationException("fixture avatar draw failure");
    }
}

public struct LocomotionState { }
public struct KittenAnimInputs { }

public sealed class KittenRenderable
{
    private CharacterAvatar _characterAvatar = null!;
    private CatEarAnim _catEarAnim = new();
    private CatConditionAnim _catConditionAnim = new();
    private AnimationAssetRef? _seatedIdleAnim;
    private AnimationAssetRef? _groundIdleAnim;
    private bool _seated;
    public bool HideHead;
    public bool IsDisposed { get; private set; }
    public CharacterAvatar Avatar => _characterAvatar;
    public CatEarAnim Ear => _catEarAnim;
    public CatConditionAnim Condition => _catConditionAnim;
    public AnimationAssetRef? SeatedIdle => _seatedIdleAnim;
    public AnimationAssetRef? GroundIdle => _groundIdleAnim;
    public AnimationAssetRef? InitialAnimation { get; private set; }
    public int FurDrawCalls { get; private set; }

    public KittenRenderable(AnimationAssetRef? seatedIdle, AnimationAssetRef? groundIdle, bool seated = true)
    {
        _seated = seated;
        _seatedIdleAnim = seated ? seatedIdle : null;
        _groundIdleAnim = groundIdle;
        InitialAnimation = seatedIdle ?? groundIdle;
        _characterAvatar = new CharacterAvatar();
        if (seated)
        {
            _characterAvatar.Attachments.Mmu.Enabled = false;
            _characterAvatar.Attachments.Helmet.Enabled = false;
            _catEarAnim.ExpressionWeight = 0f;
            _catConditionAnim.PoseEars = true;
        }
        foreach (int headIndex in _characterAvatar.Core.HeadMeshIndices)
            _characterAvatar.Core.CharacterModel.MaskedMeshIndices.Add(headIndex);
        _characterAvatar.Core.CharacterModel.SnapshotContext = SnapshotAppearance;
        _characterAvatar.Core.CharacterModel.SetAnimation(_seatedIdleAnim ?? _groundIdleAnim ?? new AnimationAssetRef("mmu-idle"));
    }

    private RenderSnapshot SnapshotAppearance()
    {
        HelmetAttachment helmet = _characterAvatar.Attachments.Helmet;
        return new RenderSnapshot(helmet.Enabled, helmet.HelmetMesh?.Visible ?? false, helmet.VisorMesh?.Visible ?? false,
            _catEarAnim.ExpressionWeight, _catConditionAnim.PoseEars,
            _characterAvatar.Core.CharacterModel.HideMaskedMeshes,
            _characterAvatar.Core.CharacterModel.MaskedMeshIndices.Count,
            _characterAvatar.Core.CharacterModel.FreezeAnimation);
    }

    public void UpdateRenderData(IViewport viewport, int inFrameIndex, double dt, float4x4 transform,
        double3 acceleration, double3 angularAcceleration, in LocomotionState locomotionState, in KittenAnimInputs animInputs)
    {
        if (_seated) UpdateSeatedAnimationState(dt);
        _catConditionAnim.PoseEars = !_characterAvatar.Attachments.Helmet.Enabled;
        AnimatedRenderable model = _characterAvatar.Core.CharacterModel;
        model.UpdateAnimation(dt);
        model.HideMaskedMeshes = HideHead;
        model.Draw();
        if (!HideHead) FurDrawCalls++;
        if (_characterAvatar.Attachments.Helmet.HasAttachment)
        {
            _characterAvatar.Attachments.Helmet.HelmetMesh?.Draw();
            _characterAvatar.Attachments.Helmet.VisorMesh?.Draw();
        }
    }

    private void UpdateSeatedAnimationState(double dt)
    {
        if (_seatedIdleAnim == null)
        {
            if (_groundIdleAnim != null) _characterAvatar.Core.CharacterModel.SetAnimation(_groundIdleAnim, 0.2f);
            return;
        }
        _characterAvatar.Core.CharacterModel.SetAnimation(_seatedIdleAnim, 0.2f);
    }

    public void Dispose()
    {
        IsDisposed = true;
        _characterAvatar.Dispose();
    }
}
