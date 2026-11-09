using System.Collections.Generic;
using System.Reflection;
using KSA;
using MeowSci.KittenAnimationsLib.Ui;

namespace Brutal.ImGuiApi
{
    public sealed class ImInputString(int size) { public int Size => size; }
    public static class ImGui
    {
        public static void Spacing() { }
        public static void TextDisabled(string value) { }
    }
}
namespace MeowSci.KsaAbstractions
{
    public interface ISubmod { }
    public static class SubmodUI
    {
        public static void BeginContentArea(string id) { }
        public static void EndContentArea() { }
    }
    public static class ReflectionHelpers
    {
        public static object? GetFieldValue(object target, string name) => target.GetType()
            .GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);
    }
}
namespace MeowSci.KittenAnimationsLib
{
    public sealed class AnimationEntry
    {
        public required string Label { get; init; }
        public required string Source { get; init; }
        public required IAnimation Animation { get; init; }
    }
    public sealed class AnimationGroup { public List<AnimationEntry> Entries = new(); }
    public sealed class KittenAnimationCatalog
    {
        public List<AnimationGroup> Groups = new();
        public List<string> UnresolvedFields = new();
        public static KittenAnimationCatalog Build(CharacterAvatar avatar, KittenRenderable renderable) => new()
        {
            Groups = new() { new() { Entries = new() { new()
                { Label = "Walk", Source = "walk", Animation = renderable.Clip } } } },
        };
    }
}
namespace MeowSci.KittenAnimationsLib.Ui
{
    public static class TargetSection
    {
        public static bool Render(IReadOnlyList<KittenEva> kittens, KittenEva? controlled, string? selected,
            Brutal.ImGuiApi.ImInputString filter, out string? newSelected)
        { newSelected = selected; return false; }
    }
    public static class PlaybackSection { public static void Render(AnimationUiContext ctx) { } }
    public static class AnimationLibrarySection { public static void Render(AnimationUiContext ctx) { } }
    public static class ExpressionSection { public static void Render(AnimationUiContext ctx) { } }
    public static class StrengthSection { public static void Render(AnimationUiContext ctx) { } }
    public static class TuningSection { public static void Render(AnimationUiContext ctx) { } }
}
