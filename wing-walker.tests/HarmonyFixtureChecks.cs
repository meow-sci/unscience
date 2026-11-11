using System;
using HarmonyLib;
using KSA;
using MeowSci.WingWalkerLib;
using Brutal.Numerics;

internal static class HarmonyFixtureChecks
{
    private static int _foreignClearCalls;
    private static readonly AnimationAssetRef Seated = new("seated-idle");
    private static readonly AnimationAssetRef Ground = new("ground-idle");
    private static readonly double4x4 Matrix = double4x4.Identity;
    private static readonly float3 Zero = float3.Zero;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Wing Walker fixture: " + message);
    }

    public static void Run()
    {
        CheckPartMarking();
        CheckHarmonyLifecycleAndRendering();
        CheckStandingAnimationScopes();
        Console.WriteLine("PASS: external-seat marking, standing origin and posture, render scope, filtering, and Harmony lifecycle");
    }

    private static void CheckPartMarking()
    {
        var standing = Seat(ExternalCrewParts.StandingPartId);
        var seated = Seat(ExternalCrewParts.SeatedPartId);
        var stock = Seat("CoreCommandSeat");
        var wrongSeatKey = Seat(ExternalCrewParts.StandingPartId, "CrewSeatClone");
        var similarPart = Seat("Unscience_WingWalker_Standing_Copy");
        Require(ExternalCrewParts.IsExternal(standing) && ExternalCrewParts.IsStanding(standing), "marked stand is external and standing");
        Require(ExternalCrewParts.IsExternal(seated) && !ExternalCrewParts.IsStanding(seated), "marked seat is external and seated");
        Require(!ExternalCrewParts.IsExternal(stock) && !ExternalCrewParts.IsExternal(wrongSeatKey)
            && !ExternalCrewParts.IsExternal(similarPart), "native, mismatched-key, and name-similar seats remain untouched");
    }

    private static void CheckOriginAdjustment()
    {
        var standing = Seat(ExternalCrewParts.StandingPartId);
        standing.PositionAsmb = new double3(4.25, -8, 3.5);
        standing.ForwardAxisAsmb = new double3(0, 1, 0);
        standing.UpAxisAsmb = new double3(0, 0, -1);
        float3 seatOffset = new(0.23f, -0.31f, 0.42f);
        double heightAboveModelOrigin = 1.17;
        double3 actual = standing.GetSeatedModelPointAsmb(in seatOffset, heightAboveModelOrigin);
        double3 expected = standing.PositionAsmb + new double3(0, 1, 0) * seatOffset.X
            + new double3(-1, 0, 0) * seatOffset.Y
            + new double3(0, 0, -1) * (heightAboveModelOrigin - ExternalCrewParts.StandingEyeHeight);
        Require(Near(actual, expected), $"standing origin uses world seat axes, custom offset, and eye-height correction ({actual} vs {expected})");

        var seated = Seat(ExternalCrewParts.SeatedPartId);
        seated.PositionAsmb = standing.PositionAsmb;
        seated.ForwardAxisAsmb = standing.ForwardAxisAsmb;
        seated.UpAxisAsmb = standing.UpAxisAsmb;
        double3 unmodified = seated.GetSeatedModelPointAsmb(in seatOffset, heightAboveModelOrigin);
        double3 native = seated.PositionAsmb + new double3(0, 1, 0) * seatOffset.X
            + new double3(-1, 0, 0) * seatOffset.Y
            + new double3(0, 0, -1) * (seatOffset.Z + heightAboveModelOrigin);
        Require(Near(unmodified, native), "seated seat keeps the native model-origin calculation");
    }

    private static void CheckHarmonyLifecycleAndRendering()
    {
        var harmony = new Harmony("wing-walker.fixture");
        var foreign = new Harmony("wing-walker.fixture.foreign");
        var viewport = new TestViewport();
        var renderer = new SuperMeshRenderSystem();
        Program.Instance = new GameProgram { SuperMeshRenderSystem = renderer, ResourceFrameIndex = 17 };
        Program.RenderedViewport = viewport;
        Program.Editor = null;
        Program.ControlledVehicle = null;

        // Warm patched methods before installing Harmony, and prove other owners survive removal.
        renderer.ClearBuckets(viewport);
        var warm = Seat(ExternalCrewParts.StandingPartId);
        warm.UpdateSeatedRenderable(viewport, 0, 0.01, in Matrix);
        warm.GetSeatedModelPointAsmb(in Zero, 0);
        Avatar().SelectAnimation(0.01);
        BoardingFixtureChecks.Warm();
        foreign.Patch(AccessTools.Method(typeof(SuperMeshRenderSystem), nameof(SuperMeshRenderSystem.ClearBuckets)),
            postfix: new HarmonyMethod(typeof(HarmonyFixtureChecks), nameof(ForeignClearPostfix)));

        for (int cycle = 0; cycle < 2; cycle++)
        {
            Program.Instance = new GameProgram { SuperMeshRenderSystem = renderer, ResourceFrameIndex = 17 };
            Program.RenderedViewport = viewport;
            WingWalkerPatches.Apply(harmony);
            WingWalkerPatches.Apply(harmony);
            Require(WingWalkerPatches.IsApplied && WingWalkerPatches.LastError == null, "Apply is idempotent");
            BoardingFixtureChecks.Run();
            CheckOriginAdjustment();

            var external = Seat(ExternalCrewParts.StandingPartId, renderable: Avatar());
            var seated = Seat(ExternalCrewParts.SeatedPartId, renderable: Avatar());
            var stock = Seat("CoreSeat", renderable: Avatar());
            var remote = VehicleWith(external, seated, stock);
            var controlledSeat = Seat(ExternalCrewParts.StandingPartId, renderable: Avatar());
            var controlled = VehicleWith(controlledSeat);
            Program.VehiclesInFrame = [remote, controlled];
            Program.ControlledVehicle = controlled;
            int remoteCalls = external.RenderCalls + seated.RenderCalls + stock.RenderCalls;
            Require(remoteCalls == 0, "flight fixture starts without submitted crew");
            renderer.ClearBuckets(viewport);
            Require(external.RenderCalls == 1 && seated.RenderCalls == 1 && stock.RenderCalls == 0,
                "flight bucket pass draws only marked seats in noncontrolled vehicles");
            Require(controlledSeat.RenderCalls == 0, "bucket pass leaves controlled vehicle to the native path");
            int controlledCalls = controlled.SeatRenders;
            controlled.UpdateSeatedCrewRenderData(viewport, 17, 0.016);
            Require(controlled.SeatRenders == controlledCalls + 1 && controlledSeat.RenderCalls == 1,
                "stock path submits controlled crew once");

            Program.ControlledVehicle = null;
            var noControl = Seat(ExternalCrewParts.SeatedPartId, renderable: Avatar());
            Program.VehiclesInFrame = [VehicleWith(noControl)];
            int beforeNoControl = noControl.RenderCalls;
            renderer.ClearBuckets(viewport);
            Require(noControl.RenderCalls == beforeNoControl + 1, "render pass handles a scene with no controlled vehicle");

            var portrait = new TestViewport(portrait: true);
            Program.RenderedViewport = portrait;
            Program.VehiclesInFrame = [VehicleWith(Seat(ExternalCrewParts.SeatedPartId, renderable: Avatar()))];
            var portraitSeat = Program.VehiclesInFrame[0].Parts.Modules.Get<IVASeat>()[0];
            renderer.ClearBuckets(portrait);
            Require(portraitSeat.RenderCalls == 0, "extension skips portrait viewport; native portrait path owns it");

            CheckEditorTrees(renderer, viewport);
            CheckDisposedAndCulled(renderer, viewport);
            CheckSceneReplacement(renderer, viewport);
            Program.Instance = new GameProgram { SuperMeshRenderSystem = renderer, ResourceFrameIndex = 17 };
            Program.RenderedViewport = viewport;

            WingWalkerPatches.Remove(harmony);
            Require(!WingWalkerPatches.IsApplied, "Remove clears patch state");
            int beforeUnpatched = external.RenderCalls;
            Program.RenderedViewport = viewport;
            Program.VehiclesInFrame = [remote];
            renderer.ClearBuckets(viewport);
            Require(external.RenderCalls == beforeUnpatched, "Remove stops the feature renderer");
        }

        int foreignBefore = _foreignClearCalls;
        renderer.ClearBuckets(viewport);
        Require(_foreignClearCalls == foreignBefore + 1, "removing the mod keeps a foreign owner's postfix installed");
        foreign.UnpatchAll(foreign.Id);
        Program.Editor = null;
        Program.VehiclesInFrame = Array.Empty<Vehicle>();
        Program.ControlledVehicle = null;
    }

    private static void CheckEditorTrees(SuperMeshRenderSystem renderer, TestViewport viewport)
    {
        var focused = Seat(ExternalCrewParts.StandingPartId, renderable: Avatar());
        var unattachedA = Seat(ExternalCrewParts.SeatedPartId, renderable: Avatar());
        var unattachedB = Seat(ExternalCrewParts.StandingPartId, renderable: Avatar());
        var unattachedTreeA = new PartTree([unattachedA]);
        var unattachedTreeB = new PartTree([unattachedB]);
        var editor = new VehicleEditor
        {
            EditingSpace = VehicleWith(focused),
            UnattachedPartTrees = [unattachedTreeA, unattachedTreeB, unattachedTreeA]
        };
        Program.Editor = editor;
        Program.RenderedViewport = viewport;
        Program.VehiclesInFrame = [VehicleWith(Seat(ExternalCrewParts.StandingPartId, renderable: Avatar()))];
        renderer.ClearBuckets(viewport);
        Require(focused.RenderCalls == 1 && unattachedA.RenderCalls == 1 && unattachedB.RenderCalls == 1,
            "focused editor and each distinct unattached tree render external seats once");
        Require(Program.VehiclesInFrame[0].Parts.Modules.Get<IVASeat>()[0].RenderCalls == 0,
            "editor rendering does not also draw flight-scene vehicles");
        Program.Editor = null;
    }

    private static void CheckDisposedAndCulled(SuperMeshRenderSystem renderer, TestViewport viewport)
    {
        var disposedSeat = Seat(ExternalCrewParts.StandingPartId, renderable: Avatar());
        var disposedVehicle = VehicleWith(disposedSeat);
        disposedVehicle.IsDisposed = true;
        var disposedTreeSeat = Seat(ExternalCrewParts.StandingPartId, renderable: Avatar());
        var disposedTree = new PartTree([disposedTreeSeat]) { IsDisposed = true };
        var culledSeat = Seat(ExternalCrewParts.StandingPartId, renderable: Avatar());
        var culled = VehicleWith(culledSeat);
        culled.LargeEnough = false;
        var disposedPartsSeat = Seat(ExternalCrewParts.StandingPartId, renderable: Avatar());
        var disposedParts = VehicleWith(disposedPartsSeat);
        disposedParts.Parts.IsDisposed = true;
        Program.VehiclesInFrame = [disposedVehicle, culled, disposedParts];
        renderer.ClearBuckets(viewport);
        Require(disposedSeat.RenderCalls == 0 && culledSeat.RenderCalls == 0 && disposedPartsSeat.RenderCalls == 0,
            "disposed, culled, and disposed-part vehicles are skipped");

        Program.Editor = new VehicleEditor { UnattachedPartTrees = [disposedTree] };
        renderer.ClearBuckets(viewport);
        Require(disposedTreeSeat.RenderCalls == 0, "disposed editor tree is skipped");
        Program.Editor = null;

        var hidden = new TestViewport(visible: false);
        Program.RenderedViewport = hidden;
        int beforeHidden = culledSeat.RenderCalls;
        renderer.ClearBuckets(hidden);
        Require(culledSeat.RenderCalls == beforeHidden, "invisible viewport does not submit crew");
    }

    private static void CheckSceneReplacement(SuperMeshRenderSystem oldRenderer, TestViewport oldViewport)
    {
        var replacementRenderer = new SuperMeshRenderSystem();
        var replacementViewport = new TestViewport();
        Program.Instance = new GameProgram { SuperMeshRenderSystem = replacementRenderer, ResourceFrameIndex = 41 };
        Program.RenderedViewport = replacementViewport;
        var replacementSeat = Seat(ExternalCrewParts.SeatedPartId, renderable: Avatar());
        Program.VehiclesInFrame = [VehicleWith(replacementSeat)];
        oldRenderer.ClearBuckets(oldViewport);
        Require(replacementSeat.RenderCalls == 0, "stale renderer/viewport is ignored after scene replacement");
        replacementRenderer.ClearBuckets(replacementViewport);
        Require(replacementSeat.RenderCalls == 1, "new scene renderer binds without stale cached state");
    }

    private static void CheckStandingAnimationScopes()
    {
        var harmony = new Harmony("wing-walker.fixture.animation");
        var renderer = new SuperMeshRenderSystem();
        var viewport = new TestViewport();
        Program.Instance = new GameProgram { SuperMeshRenderSystem = renderer };
        Program.RenderedViewport = viewport;
        WingWalkerPatches.Apply(harmony);

        var standingAvatar = Avatar();
        var standing = Seat(ExternalCrewParts.StandingPartId, renderable: standingAvatar);
        standing.UpdateSeatedRenderable(viewport, 1, 0.01, in Matrix);
        Require(ReferenceEquals(standingAvatar.Selected, Ground), "standing occupant selects the ground idle clip");
        Require(ReferenceEquals(standingAvatar.SeatedIdle, Seated), "standing animation restores original seated field");

        var seatedAvatar = Avatar();
        var seated = Seat(ExternalCrewParts.SeatedPartId, renderable: seatedAvatar);
        seated.UpdateSeatedRenderable(viewport, 1, 0.01, in Matrix);
        Require(ReferenceEquals(seatedAvatar.Selected, Seated) && ReferenceEquals(seatedAvatar.SeatedIdle, Seated),
            "external seated occupant keeps native seated animation");

        var nativeAvatar = Avatar();
        Seat("CoreSeat", renderable: nativeAvatar).UpdateSeatedRenderable(viewport, 1, 0.01, in Matrix);
        Require(ReferenceEquals(nativeAvatar.Selected, Seated), "native seat animation is unaffected");

        var outerAvatar = Avatar();
        var innerAvatar = Avatar();
        var outer = Seat(ExternalCrewParts.StandingPartId, renderable: outerAvatar);
        var inner = Seat(ExternalCrewParts.SeatedPartId, renderable: innerAvatar);
        outerAvatar.BeforeSelect = () => inner.UpdateSeatedRenderable(viewport, 1, 0.01, in Matrix);
        outer.UpdateSeatedRenderable(viewport, 1, 0.01, in Matrix);
        Require(ReferenceEquals(outerAvatar.Selected, Ground) && ReferenceEquals(innerAvatar.Selected, Seated),
            "nested seat update restores the outer standing scope");

        var exceptionAvatar = Avatar();
        exceptionAvatar.ThrowOnSelect = true;
        var exceptionSeat = Seat(ExternalCrewParts.StandingPartId, renderable: exceptionAvatar);
        bool threw = false;
        try { exceptionSeat.UpdateSeatedRenderable(viewport, 1, 0.01, in Matrix); }
        catch (InvalidOperationException) { threw = true; }
        Require(threw && ReferenceEquals(exceptionAvatar.SeatedIdle, Seated), "animation finalizer restores original field after an exception");

        var followingAvatar = Avatar();
        var following = Seat(ExternalCrewParts.SeatedPartId, renderable: followingAvatar);
        following.UpdateSeatedRenderable(viewport, 1, 0.01, in Matrix);
        Require(ReferenceEquals(followingAvatar.Selected, Seated), "seat finalizer clears standing state after an exception");

        var noGroundAvatar = new KittenRenderable(Seated, null);
        Seat(ExternalCrewParts.StandingPartId, renderable: noGroundAvatar).UpdateSeatedRenderable(viewport, 1, 0.01, in Matrix);
        Require(ReferenceEquals(noGroundAvatar.Selected, Seated), "missing ground idle safely retains native seated animation");
        WingWalkerPatches.Remove(harmony);
    }

    private static IVASeat Seat(string partId, string seatKey = "CrewSeat", KittenRenderable? renderable = null) =>
        new(partId, seatKey) { Renderable = renderable };

    private static KittenRenderable Avatar() => new(Seated, Ground);
    private static Vehicle VehicleWith(params IVASeat[] seats) => new(new PartTree(seats));
    private static bool Near(double3 left, double3 right) =>
        Math.Abs(left.X - right.X) < 1e-9 && Math.Abs(left.Y - right.Y) < 1e-9 && Math.Abs(left.Z - right.Z) < 1e-9;
    private static void ForeignClearPostfix() => _foreignClearCalls++;
}
