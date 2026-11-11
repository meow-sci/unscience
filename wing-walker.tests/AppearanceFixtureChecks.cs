using System;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using MeowSci.WingWalkerLib;

internal static class AppearanceFixtureChecks
{
    private static readonly AnimationAssetRef Seated = new("seated-idle");
    private static readonly AnimationAssetRef Ground = new("ground-idle");
    private static readonly double4x4 Matrix = double4x4.Identity;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Wing Walker appearance fixture: " + message);
    }

    public static void Run()
    {
        var harmony = new Harmony("wing-walker.fixture.appearance");
        WingWalkerPatches.Apply(harmony);
        Program.FrameNumber = 101;
        CheckPublicZeroDeltaFirstDraw();
        CheckDirectUnscopedNativeRender();
        CheckPreHiddenAttachmentVisibility();
        CheckPosturesAndHelmetRestoration();
        CheckFirstPersonThenExteriorInSameFrame();
        CheckAvatarReassignmentAndMissingGround();
        CheckNestedScopeAndExceptionRestoration();
        CheckAppearanceMarkerResetOnReapply(harmony);
        WingWalkerPatches.Remove(harmony);
    }

    private static void CheckPublicZeroDeltaFirstDraw()
    {
        KittenRenderable? created = null;
        int constructorPlayCalls = 0;
        IVASeat seat = ExternalSeat(ExternalCrewParts.StandingPartId, "stand-first",
            () =>
            {
                created = new KittenRenderable(Seated, Ground);
                constructorPlayCalls = created.Avatar.Core.CharacterModel.PlayAnimationCalls;
                return created;
            });
        seat.UpdateSeatedRenderable(new TestViewport(), 1, 0, in Matrix);
        KittenRenderable avatar = created ?? throw new Exception("standing avatar was not lazily created");
        AnimatedRenderable model = avatar.Avatar.Core.CharacterModel;
        Require(ReferenceEquals(avatar.InitialAnimation, Seated), "native construction begins with the seated clip");
        Require(ReferenceEquals(model.EffectiveAnimation, Ground) && model.PlayAnimationCalls == constructorPlayCalls + 1,
            $"public render prefix selects a fully standing pose on the first dt=0 draw (effective={model.EffectiveAnimation?.Name}, play calls={constructorPlayCalls}->{model.PlayAnimationCalls})");
        Require(ReferenceEquals(avatar.SeatedIdle, Seated), "standing clip override is restored after public rendering");
        Require(model.AtUpdateAnimation is { HelmetEnabled: true, EarWeight: 1f, PoseEars: false, MaskedMeshCount: 2 },
            "helmet state and head mask animation are active before native UpdateAnimation");
        Require(model.AtDraw is { HelmetEnabled: true } && avatar.Avatar.Attachments.Helmet.HelmetMesh!.DrawCalls == 1,
            "helmet remains available through draw, then scope restores native attachment state");
        Require(!avatar.Avatar.Attachments.Helmet.Enabled && avatar.Ear.ExpressionWeight == 0f,
            "helmet and ear-mask fields return to native seated values");

        Program.FrameNumber++;
        seat.UpdateSeatedRenderable(new TestViewport(), 2, 0, in Matrix);
        Require(ReferenceEquals(model.EffectiveAnimation, Ground) && model.PlayAnimationCalls == constructorPlayCalls + 1,
            "later zero-delta renders retain the first standing animation phase");
    }

    private static void CheckDirectUnscopedNativeRender()
    {
        Program.FrameNumber++;
        KittenRenderable avatar = new(Seated, Ground);
        AnimatedRenderable model = avatar.Avatar.Core.CharacterModel;
        int constructorPlayCalls = model.PlayAnimationCalls;

        RenderDirect(avatar, new TestViewport(), 0);

        Require(model.AtUpdateAnimation is
            { HelmetEnabled: false, EarWeight: 0f, PoseEars: true, HideMaskedMeshes: false },
            "a direct public render outside an external-seat scope keeps native seated appearance");
        Require(model.AtDraw is { HelmetEnabled: false } && avatar.Avatar.Attachments.Helmet.HelmetMesh!.DrawCalls == 0
            && model.PlayAnimationCalls == constructorPlayCalls,
            "unscoped native rendering neither draws the helmet nor changes the seated animation");
    }

    private static void CheckPreHiddenAttachmentVisibility()
    {
        Program.FrameNumber++;
        KittenRenderable? created = null;
        IVASeat seat = ExternalSeat(ExternalCrewParts.SeatedPartId, "prehidden-helmet", () =>
        {
            created = new KittenRenderable(Seated, Ground);
            created.Avatar.Attachments.Helmet.HelmetMesh!.Visible = false;
            created.Avatar.Attachments.Helmet.VisorMesh!.Visible = false;
            return created;
        });

        seat.UpdateSeatedRenderable(new TestViewport(), 20, 0, in Matrix);

        KittenRenderable avatar = created!;
        AnimatedRenderable model = avatar.Avatar.Core.CharacterModel;
        Require(model.AtUpdateAnimation is { HelmetEnabled: true, HelmetVisible: false, VisorVisible: false, EarWeight: 1f },
            "external helmet posing preserves both meshes when another owner hid them");
        Require(avatar.Avatar.Attachments.Helmet.HelmetMesh!.DrawCalls == 0
            && avatar.Avatar.Attachments.Helmet.VisorMesh!.DrawCalls == 0
            && !avatar.Avatar.Attachments.Helmet.HelmetMesh.Visible
            && !avatar.Avatar.Attachments.Helmet.VisorMesh.Visible,
            "rendering and finalization do not reveal pre-hidden helmet or visor meshes");
    }

    private static void CheckPosturesAndHelmetRestoration()
    {
        Program.FrameNumber++;
        var standing = ExternalSeat(ExternalCrewParts.StandingPartId, "stand-posture");
        var seated = ExternalSeat(ExternalCrewParts.SeatedPartId, "seat-posture");
        var native = ExternalSeat("CoreCommandSeat", "stock-posture");
        var viewport = new TestViewport();
        standing.UpdateSeatedRenderable(viewport, 3, 0, in Matrix);
        seated.UpdateSeatedRenderable(viewport, 3, 0, in Matrix);
        native.UpdateSeatedRenderable(viewport, 3, 0, in Matrix);

        AnimatedRenderable standingModel = standing.Renderable!.Avatar.Core.CharacterModel;
        AnimatedRenderable seatedModel = seated.Renderable!.Avatar.Core.CharacterModel;
        AnimatedRenderable nativeModel = native.Renderable!.Avatar.Core.CharacterModel;
        Require(ReferenceEquals(standingModel.EffectiveAnimation, Ground), "Standing Mount alone switches to ground idle");
        Require(ReferenceEquals(seatedModel.EffectiveAnimation, Seated), "seated external part preserves seated posture");
        Require(ReferenceEquals(nativeModel.EffectiveAnimation, Seated), "stock seat preserves native seated posture");
        Require(standingModel.AtUpdateAnimation?.HelmetEnabled == true && seatedModel.AtUpdateAnimation?.HelmetEnabled == true,
            "both external part IDs enable helmet before animation update");
        Require(nativeModel.AtUpdateAnimation?.HelmetEnabled == false && nativeModel.AtUpdateAnimation?.EarWeight == 0f,
            "stock crew appearance remains unchanged");
        Require(standing.Renderable!.Avatar.Attachments.Helmet.HelmetMesh!.DrawCalls == 1
            && seated.Renderable!.Avatar.Attachments.Helmet.HelmetMesh!.DrawCalls == 1
            && native.Renderable!.Avatar.Attachments.Helmet.HelmetMesh!.DrawCalls == 0,
            "both external parts draw helmets while stock seats keep the native helmet off");
        Require(!standing.Renderable!.Avatar.Attachments.Helmet.Enabled
            && !seated.Renderable!.Avatar.Attachments.Helmet.Enabled,
            "external appearance state is scoped and restored after each draw");
    }

    private static void CheckFirstPersonThenExteriorInSameFrame()
    {
        Program.FrameNumber++;
        KittenRenderable? created = null;
        IVASeat seat = ExternalSeat(ExternalCrewParts.StandingPartId, "iva-seat",
            () => created = new KittenRenderable(Seated, Ground));
        var iva = new TestViewport(iva: true);
        iva.IvaController.Seat = seat;
        seat.UpdateSeatedRenderable(iva, 4, 0, in Matrix);

        KittenRenderable avatar = created!;
        AnimatedRenderable model = avatar.Avatar.Core.CharacterModel;
        Require(avatar.HideHead && model.AtUpdateAnimation is
            { HelmetEnabled: true, HelmetVisible: false, VisorVisible: false, EarWeight: 1f, PoseEars: false, MaskedMeshCount: 2 },
            "own-IVA pass keeps helmet pose enabled while hiding both helmet meshes and preparing head masks");
        Require(model.AtDraw is { HideMaskedMeshes: true } && avatar.FurDrawCalls == 0,
            "own-IVA draw hides head meshes and fur");
        Require(avatar.Avatar.Attachments.Helmet.HelmetMesh!.DrawCalls == 0
            && avatar.Avatar.Attachments.Helmet.VisorMesh!.DrawCalls == 0,
            "own-IVA pass skips visible helmet geometry");
        Require(!avatar.Avatar.Attachments.Helmet.Enabled && avatar.Avatar.Attachments.Helmet.HelmetMesh.Visible,
            "finalizer restores helmet enablement and mesh visibility");

        var exterior = new TestViewport();
        seat.UpdateSeatedRenderable(exterior, 5, 0, in Matrix);
        Require(!avatar.HideHead && model.AtDraw is { HideMaskedMeshes: false },
            "next exterior view unhides head meshes for the same avatar");
        Require(model.AnimationAdvanceCalls == 1 && ReferenceEquals(model.EffectiveAnimation, Ground),
            "same-frame viewport reuse keeps the standing pose cached at dt=0");
        Require(avatar.Avatar.Attachments.Helmet.HelmetMesh!.DrawCalls == 1
            && avatar.Avatar.Attachments.Helmet.VisorMesh!.DrawCalls == 1,
            "exterior view restores helmet and visor drawing after own-IVA suppression");
    }

    private static void CheckAvatarReassignmentAndMissingGround()
    {
        Program.FrameNumber++;
        KittenRenderable? latest = null;
        int creations = 0;
        int latestConstructorPlayCalls = 0;
        IVASeat seat = ExternalSeat(ExternalCrewParts.StandingPartId, "crew-a", () =>
        {
            creations++;
            latest = new KittenRenderable(Seated, Ground);
            latestConstructorPlayCalls = latest.Avatar.Core.CharacterModel.PlayAnimationCalls;
            return latest;
        });
        seat.UpdateSeatedRenderable(new TestViewport(), 6, 0, in Matrix);
        KittenRenderable oldAvatar = latest!;
        Require(ReferenceEquals(oldAvatar.Avatar.Core.CharacterModel.EffectiveAnimation, Ground)
            && oldAvatar.Avatar.Core.CharacterModel.PlayAnimationCalls == latestConstructorPlayCalls + 1,
            "first occupant gets the standing pose");

        Program.FrameNumber++;
        seat.AssignedKittenHash = KeyHash.Make("crew-b");
        seat.UpdateSeatedRenderable(new TestViewport(), 7, 0, in Matrix);
        KittenRenderable newAvatar = latest!;
        Require(creations == 2 && !ReferenceEquals(oldAvatar, newAvatar) && oldAvatar.IsDisposed && oldAvatar.Avatar.IsDisposed,
            "crew reassignment disposes and replaces the lazy native renderable");
        Require(ReferenceEquals(newAvatar.InitialAnimation, Seated)
            && ReferenceEquals(newAvatar.Avatar.Core.CharacterModel.EffectiveAnimation, Ground)
            && newAvatar.Avatar.Core.CharacterModel.PlayAnimationCalls == latestConstructorPlayCalls + 1,
            "newly constructed avatar also starts standing at zero delta");

        Program.FrameNumber++;
        KittenRenderable? missing = null;
        IVASeat noGroundSeat = ExternalSeat(ExternalCrewParts.StandingPartId, "no-ground",
            () => missing = new KittenRenderable(Seated, null));
        noGroundSeat.UpdateSeatedRenderable(new TestViewport(), 8, 0, in Matrix);
        Require(ReferenceEquals(missing!.Avatar.Core.CharacterModel.EffectiveAnimation, Seated),
            "missing ground clip retains native seated pose");
        Require(missing.Avatar.Core.CharacterModel.AtUpdateAnimation?.HelmetEnabled == true
            && missing.Avatar.Attachments.Helmet.HelmetMesh!.DrawCalls == 1,
            "missing ground clip does not remove the external-seat helmet");
    }

    private static void CheckNestedScopeAndExceptionRestoration()
    {
        Program.FrameNumber++;
        KittenRenderable? outerAvatar = null;
        var outer = ExternalSeat(ExternalCrewParts.StandingPartId, "outer-standing",
            () => outerAvatar = new KittenRenderable(Seated, Ground));
        var inner = ExternalSeat(ExternalCrewParts.SeatedPartId, "inner-seated");
        AnimatedRenderable outerModel = null!;
        outerAvatar ??= new KittenRenderable(Seated, Ground);
        outer.RenderableFactory = () => outerAvatar;
        outerModel = outerAvatar.Avatar.Core.CharacterModel;
        outerModel.BeforeUpdateAnimation = () =>
        {
            outerModel.BeforeUpdateAnimation = null;
            inner.UpdateSeatedRenderable(new TestViewport(), 9, 0, in Matrix);
            outerAvatar.Avatar.Attachments.Helmet.Enabled = false;
            outerAvatar.Avatar.Attachments.Helmet.HelmetMesh!.Visible = true;
            outerAvatar.Avatar.Attachments.Helmet.VisorMesh!.Visible = true;
            outerAvatar.Ear.ExpressionWeight = 0.25f;
            outerAvatar.Condition.PoseEars = true;
            RenderDirect(outerAvatar, new TestViewport(), 0);
        };
        outer.UpdateSeatedRenderable(new TestViewport(), 9, 0, in Matrix);
        Require(outerModel.AtUpdateAnimation is { HelmetEnabled: true, EarWeight: 1f, PoseEars: false },
            "nested seat render restores the outer external scope before its public render call");
        Require(!outerAvatar.Avatar.Attachments.Helmet.Enabled && outerAvatar.Ear.ExpressionWeight == 0f
            && outerAvatar.Condition.PoseEars,
            "outer finalizer restores appearance values even after nested public rendering");

        Program.FrameNumber++;
        KittenRenderable? broken = null;
        IVASeat throwingSeat = ExternalSeat(ExternalCrewParts.StandingPartId, "throwing-standing", () =>
        {
            broken = new KittenRenderable(Seated, Ground);
            broken.Ear.ExpressionWeight = 0.3f;
            broken.Condition.PoseEars = true;
            broken.Avatar.Core.CharacterModel.FreezeAnimation = true;
            broken.Avatar.Core.CharacterModel.ThrowOnUpdateAnimation = true;
            return broken;
        });
        bool threw = false;
        try { throwingSeat.UpdateSeatedRenderable(new TestViewport(), 10, 0, in Matrix); }
        catch (InvalidOperationException) { threw = true; }
        KittenRenderable brokenAvatar = broken!;
        Require(threw && brokenAvatar.Avatar.Core.CharacterModel.AtUpdateAnimation is
            { HelmetEnabled: true, EarWeight: 1f, PoseEars: false, Frozen: false },
            "public render scope applies helmet pose before an exception in native UpdateAnimation");
        Require(!brokenAvatar.Avatar.Attachments.Helmet.Enabled && brokenAvatar.Avatar.Attachments.Helmet.HelmetMesh!.Visible
            && brokenAvatar.Avatar.Attachments.Helmet.VisorMesh!.Visible && brokenAvatar.Ear.ExpressionWeight == 0.3f
            && brokenAvatar.Condition.PoseEars && brokenAvatar.Avatar.Core.CharacterModel.FreezeAnimation
            && ReferenceEquals(brokenAvatar.SeatedIdle, Seated),
            "finalizer restores helmet, mesh visibility, ear/condition state, freeze flag, and seated clip after error");

        brokenAvatar.Avatar.Core.CharacterModel.ThrowOnUpdateAnimation = false;
        Program.FrameNumber++;
        RenderDirect(brokenAvatar, new TestViewport(), 0);
        Require(brokenAvatar.Avatar.Core.CharacterModel.AtUpdateAnimation is
            { HelmetEnabled: false, EarWeight: 0.3f, PoseEars: true },
            "seat finalizer clears external scope so direct native rendering stays stock");
    }

    private static void CheckAppearanceMarkerResetOnReapply(Harmony harmony)
    {
        Program.FrameNumber++;
        KittenRenderable? avatar = null;
        int constructorPlayCalls = 0;
        IVASeat seat = ExternalSeat(ExternalCrewParts.StandingPartId, "reapply-standing",
            () =>
            {
                avatar = new KittenRenderable(Seated, Ground);
                constructorPlayCalls = avatar.Avatar.Core.CharacterModel.PlayAnimationCalls;
                return avatar;
            });
        seat.UpdateSeatedRenderable(new TestViewport(), 11, 0, in Matrix);
        AnimatedRenderable model = avatar!.Avatar.Core.CharacterModel;
        int initializedPlayCalls = constructorPlayCalls + 1;
        Require(model.PlayAnimationCalls == initializedPlayCalls,
            "initial patch application starts standing once after native construction");
        WingWalkerPatches.Remove(harmony);
        WingWalkerPatches.Apply(harmony);
        Program.FrameNumber++;
        seat.UpdateSeatedRenderable(new TestViewport(), 12, 0, in Matrix);
        Require(model.PlayAnimationCalls == initializedPlayCalls + 1 && ReferenceEquals(model.EffectiveAnimation, Ground),
            "reapply clears the weak first-pose marker and initializes the existing avatar again");
    }

    private static IVASeat ExternalSeat(string partId, string kittenId, Func<KittenRenderable>? factory = null) =>
        new(partId)
        {
            AssignedKittenHash = KeyHash.Make(kittenId),
            RenderableFactory = factory ?? (() => new KittenRenderable(Seated, Ground))
        };

    private static void RenderDirect(KittenRenderable avatar, IViewport viewport, double dt)
    {
        LocomotionState locomotion = default;
        KittenAnimInputs inputs = default;
        avatar.UpdateRenderData(viewport, 0, dt, float4x4.Identity, double3.Zero, double3.Zero, in locomotion, in inputs);
    }
}
