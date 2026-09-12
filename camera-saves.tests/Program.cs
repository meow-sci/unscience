using System;
using System.Linq;
using MeowSci.CameraControllerOverrideLib.Animation;
using MeowSci.CameraControllerOverrideLib.Animation.Animations;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

internal static class CameraSaveChecks
{
    public static void Main()
    {
        IKeyframeAnimation[] animations =
        {
            new ZoomInAnimation(12, 5, EasingType.EaseIn), new ZoomOutAnimation(13, 6, EasingType.EaseOut),
            new ZoomInToOffsetAnimation(14, 7, EasingType.Linear, 1, 2, 3),
            new SpiralZoomInAnimation(15, 8, EasingType.EaseInOut, 270),
            new SpiralZoomOutAnimation(16, 9, EasingType.Linear, 450), new OrbitAnimation(120, 3, EasingType.Linear),
            new LoopyOrbitAnimation(180, 30, 5, 8, EasingType.EaseIn), new ShakeAnimation(2, 6, 8, 3, EasingType.EaseOut),
            new PanAnimation(1, 2, 3, 5, EasingType.EaseIn), new RotateAnimation(25, -15, 6, EasingType.EaseInOut)
        };
        foreach (var original in animations)
        {
            var before = SavedAnimation.Capture(original);
            var recipe = SaveJson.FromElement<SavedAnimation>(SaveJson.ToElement(before));
            var after = SavedAnimation.Capture(recipe.Create());
            if (before.Kind != after.Kind || before.Duration != after.Duration || before.Easing != after.Easing
                || !before.Parameters.SequenceEqual(after.Parameters)) throw new Exception("Camera parameters lost: " + before.Kind);
        }
        var inner = new AnimationGroup(); inner.Add(animations[0]); inner.Add(animations[1]);
        var outer = new AnimationGroup(); outer.Add(inner); outer.Add(animations[2]);
        var restored = (AnimationGroup)SaveJson.FromElement<SavedAnimation>(SaveJson.ToElement(SavedAnimation.Capture(outer))).Create();
        if (restored.Count != 2 || restored.GetAnimation(0) is not AnimationGroup child || child.Count != 2)
            throw new Exception("Camera nested groups lost.");
        bool rejected = false;
        try { _ = new SavedAnimation { Kind = "arbitrary", Duration = 4 }.Create(); } catch (InvalidOperationException) { rejected = true; }
        if (!rejected) throw new Exception("Unknown camera kind accepted.");
        Console.WriteLine("Camera save recipes: 12 checks passed.");
    }
}
