using System;
using System.Collections.Generic;
using System.Linq;
using KSA;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.ZippoLib;

public sealed class SavedDiscoLight
{
    public SavedPartReference Target { get; set; } = new();
    public DiscoRecipe Recipe { get; set; } = new();
    public bool Paused { get; set; }
    public bool Enabled { get; set; }
    public bool OriginalSwitch { get; set; }
    public bool OwnsSwitch { get; set; }
    public double Elapsed { get; set; }
    public uint Seed { get; set; }
    public double ColorPhase { get; set; }
    public double ActuationPhase { get; set; }
    public double SpreadPhase { get; set; }
    public List<SavedDiscoActuator> Actuators { get; set; } = new();
}

public sealed class SavedDiscoActuator
{
    public int ModuleIndex { get; set; }
    public float OriginalGoal { get; set; }
}

internal sealed partial class DiscoLight
{
    internal SavedDiscoLight CaptureSaved(Vehicle vehicle)
    {
        var modules = Part.FullPart.Modules.Get<KeyframeAnimationModule>().ToArray();
        return new()
        {
            Target = SavedPartReference.Capture(vehicle, Part), Recipe = Recipe.Clone(), Paused = Paused,
            Enabled = IsEnabled, OriginalSwitch = _originalSwitchState, OwnsSwitch = _writtenSwitchState.HasValue,
            Elapsed = Elapsed, Seed = _seed, ColorPhase = _colorPhaseOffset,
            ActuationPhase = _actuationPhaseOffset, SpreadPhase = _spreadPhaseOffset,
            Actuators = Actuators.Select(module => new SavedDiscoActuator
            {
                ModuleIndex = Array.IndexOf(modules, module), OriginalGoal = _goals[module].Original
            }).ToList()
        };
    }

    internal DiscoLight(Part part, SavedDiscoLight saved) : this(part, saved.Recipe)
    {
        _seed = saved.Seed; _colorPhaseOffset = saved.ColorPhase;
        _actuationPhaseOffset = saved.ActuationPhase; _spreadPhaseOffset = saved.SpreadPhase;
        _originalSwitchState = saved.OriginalSwitch;
        Elapsed = saved.Elapsed;
        if (saved.OwnsSwitch) SetEnabled(saved.Enabled);
        else if (_lightSwitch != null) _lightSwitch.LightIsActive = saved.Enabled;
        var modules = part.FullPart.Modules.Get<KeyframeAnimationModule>();
        foreach (var actuator in saved.Actuators)
        {
            if (actuator.ModuleIndex < 0 || actuator.ModuleIndex >= modules.Length)
                throw new InvalidOperationException("Disco actuator layout changed.");
            var module = modules[actuator.ModuleIndex];
            AddActuator(module);
            _goals[module] = (actuator.OriginalGoal, module.TimeGoal);
        }
        Update(0); // Recreate the saved sample even when playback is paused.
        Paused = saved.Paused;
    }
}
