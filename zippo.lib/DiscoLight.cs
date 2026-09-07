using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using KSA;

namespace MeowSci.ZippoLib;

/// <summary>
/// Runs one Disco recipe against module-local light templates and restores every value it owns.
/// </summary>
internal sealed class DiscoLight : IDisposable
{
    private readonly List<(LightModule Module, LightModule.TemplateData Original, LightModule.TemplateData Owned)> _lights = new();
    private readonly Dictionary<KeyframeAnimationModule, (float Original, float Written)> _goals = new();
    private readonly PowerConsumer? _lightSwitch;
    private readonly bool _originalSwitchState;
    private bool? _writtenSwitchState;
    private readonly uint _seed;
    private readonly double _colorPhaseOffset;
    private readonly double _actuationPhaseOffset;
    private readonly double _spreadPhaseOffset;

    public DiscoLight(Part part, DiscoRecipe recipe)
    {
        Part = part;
        Recipe = recipe.Clone();
        Recipe.Validate();
        _seed = (uint)Random.Shared.Next();
        _colorPhaseOffset = Random.Shared.NextSingle() * Recipe.PhaseJitter;
        _actuationPhaseOffset = Random.Shared.NextSingle() * Recipe.PhaseJitter;
        _spreadPhaseOffset = Random.Shared.NextSingle() * Recipe.PhaseJitter;
        _lightSwitch = part.LightSwitch ?? part.FullPart.LightSwitch;
        _originalSwitchState = _lightSwitch?.LightIsActive ?? true;

        foreach (var module in part.Modules.Get<LightModule>())
        {
            var original = module.Template;
            var owned = new LightModule.TemplateData
            {
                Id = original.Id,
                Type = original.Type,
                Transform = original.Transform,
                Range = original.Range,
                Intensity = original.Intensity,
                ColorRgb = original.ColorRgb,
                InnerAngle = original.InnerAngle,
                OuterAngle = original.OuterAngle,
                RayTracing = original.RayTracing,
                DisableInIva = original.DisableInIva,
            };

            if (Recipe.Color)
            {
                owned.ColorRgb = new ColorRgbReference((float3)original.ColorRgb);
                owned.ColorRgb.OnDataLoad(null!);
            }

            if (Recipe.Spread && owned.Type == LightModule.TemplateData.LightType.Spot)
            {
                owned.InnerAngle = new FloatReference(original.InnerAngle.Value);
                owned.OuterAngle = new FloatReference(original.OuterAngle.Value);
            }

            _lights.Add((module, original, owned));
            module.Template = owned;
        }
    }

    public Part Part { get; }
    public DiscoRecipe Recipe { get; }
    public bool Paused;
    public double Elapsed { get; private set; }
    public List<KeyframeAnimationModule> Actuators { get; } = new();
    public int SpotCount => _lights.Count(light => light.Owned.Type == LightModule.TemplateData.LightType.Spot);
    public bool OwnsTemplates => _lights.All(light => ReferenceEquals(light.Module.Template, light.Owned));
    public bool HasLightSwitch => _lightSwitch != null;
    public bool IsEnabled => _lightSwitch?.LightIsActive ?? true;

    public void SetEnabled(bool enabled)
    {
        if (_lightSwitch == null) return;
        _lightSwitch.LightIsActive = enabled;
        _writtenSwitchState = enabled;
    }

    public void AddActuator(KeyframeAnimationModule module)
    {
        Actuators.Add(module);
        _goals[module] = (module.TimeGoal, module.TimeGoal);
    }

    public void ReleaseActuator(KeyframeAnimationModule module)
    {
        if (!_goals.Remove(module, out var goal)) return;
        if (module.TimeGoal == goal.Written) module.TimeGoal = goal.Original;
        Actuators.Remove(module);
    }

    public void Update(double dt)
    {
        if (Paused) return;
        if (double.IsFinite(dt) && dt > 0d) Elapsed += dt;

        var (colorStep, colorMix) = Recipe.ColorTiming.Sample(Elapsed + _colorPhaseOffset);
        float3 start = ColorAt(colorStep);
        float3 end = ColorAt(colorStep + 1);
        float3 color = start + (end - start) * colorMix;

        var (spreadStep, spreadMix) = Recipe.SpreadTiming.Sample(Elapsed + _spreadPhaseOffset);
        if (spreadStep % 2 != 0) spreadMix = 1f - spreadMix;

        foreach (var (module, _, owned) in _lights)
        {
            // Another feature may deliberately replace the module template. Do not overwrite it.
            if (!ReferenceEquals(module.Template, owned)) continue;

            if (Recipe.Color)
            {
                owned.ColorRgb.R = color.X;
                owned.ColorRgb.G = color.Y;
                owned.ColorRgb.B = color.Z;
                owned.ColorRgb.IndexedColor = IndexedColor.Invalid;
                owned.ColorRgb.OnDataLoad(null!);
            }

            if (Recipe.Spread && owned.Type == LightModule.TemplateData.LightType.Spot)
            {
                owned.InnerAngle.Value = Lerp(Recipe.InnerMin, Recipe.InnerMax, spreadMix) * MathF.PI / 180f;
                owned.OuterAngle.Value = Lerp(Recipe.OuterMin, Recipe.OuterMax, spreadMix) * MathF.PI / 180f;
            }
        }

        var (actuationStep, actuationMix) = Recipe.ActuationTiming.Sample(Elapsed + _actuationPhaseOffset);
        if (actuationStep % 2 != 0) actuationMix = 1f - actuationMix;
        foreach (var actuator in Actuators)
        {
            float goal = Lerp(Recipe.ActuationMin, Recipe.ActuationMax, actuationMix) * actuator.Shared.Duration;
            actuator.TimeGoal = goal;
            _goals[actuator] = (_goals[actuator].Original, goal);
        }
    }

    private float3 ColorAt(long step)
    {
        if (!Recipe.RandomColors) return Recipe.Palette[(int)(step % Recipe.Palette.Count)];

        // Stable per-step random hue: frame rate and skipped frames cannot alter the sequence.
        uint hash = unchecked((uint)step * 747796405u + _seed + 2891336453u);
        hash = ((hash >> (int)((hash >> 28) + 4)) ^ hash) * 277803737u;
        float hue = ((hash >> 22) ^ hash) / (float)uint.MaxValue * 6f;
        float x = 1f - MathF.Abs(hue % 2f - 1f);
        return ((int)hue % 6) switch
        {
            0 => new float3(1f, x, 0f),
            1 => new float3(x, 1f, 0f),
            2 => new float3(0f, 1f, x),
            3 => new float3(0f, x, 1f),
            4 => new float3(x, 0f, 1f),
            _ => new float3(1f, 0f, x),
        };
    }

    private static float Lerp(float start, float end, float mix) => start + (end - start) * mix;

    public void Dispose()
    {
        foreach (var (module, original, owned) in _lights)
        {
            if (ReferenceEquals(module.Template, owned)) module.Template = original;
        }

        foreach (var (module, goal) in _goals)
        {
            if (module.TimeGoal == goal.Written) module.TimeGoal = goal.Original;
        }

        if (_lightSwitch != null && _writtenSwitchState.HasValue
            && _lightSwitch.LightIsActive == _writtenSwitchState.Value)
        {
            _lightSwitch.LightIsActive = _originalSwitchState;
        }

        _lights.Clear();
        Actuators.Clear();
        _goals.Clear();
    }
}
