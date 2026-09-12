using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.ZippoLib;

public sealed class ZippoSaveState
{
    public List<SavedLightTemplate> Templates { get; set; } = new();
    public List<SavedDiscoLight> Disco { get; set; } = new();
    public DiscoRecipe DraftRecipe { get; set; } = new();
    public List<SavedLightQueue> Queues { get; set; } = new();
    public List<SavedOriginalLightColor> OriginalColors { get; set; } = new();
}

public sealed class SavedLightQueue
{
    public SavedPartReference Target { get; set; } = new();
    public List<SavedLightAnimation> Animations { get; set; } = new();
}

public sealed class SavedOriginalLightColor
{
    public SavedPartReference Target { get; set; } = new();
    public float3 Color { get; set; }
}

public sealed partial class ZippoSubmod : ISaveParticipantSource
{
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return new SaveParticipant<ZippoSaveState>("zippo", CaptureLights, ResetSavedLights,
            RestoreLights, 80, ValidateLights); }
    }

    private ZippoSaveState CaptureLights()
    {
        var vehicles = VehicleProvider.GetAllVehicles(true);
        var result = new ZippoSaveState { Templates = LightController.CaptureTemplateChanges(), DraftRecipe = _disco.Clone() };
        foreach (var (part, live) in _discoLights)
        {
            var vehicle = vehicles.FirstOrDefault(v => PartHelpers.GetAllParts(v).Contains(part));
            if (vehicle != null) result.Disco.Add(live.CaptureSaved(vehicle));
        }
        foreach (var (part, color) in _originalColors)
        {
            var vehicle = vehicles.FirstOrDefault(v => PartHelpers.GetAllParts(v).Contains(part));
            if (vehicle != null) result.OriginalColors.Add(new() { Target = SavedPartReference.Capture(vehicle, part), Color = color });
        }
        foreach (var vehicle in vehicles)
            foreach (var part in LightController.GetLightParts(vehicle))
            {
                var animations = _animationManager.CaptureSaved(PartKey(part));
                if (animations.Count > 0) result.Queues.Add(new() { Target = SavedPartReference.Capture(vehicle, part), Animations = animations });
            }
        return result;
    }

    private void ResetSavedLights()
    {
        StopAllDisco(); _animationManager.Clear(); LightController.ResetTemplateChanges();
        _originalColors.Clear(); _vehicles.Clear(); ClearLightParts(); _disco = new();
        _vehicleComboIdx = 0; _vehicleComboItems = new[] { "(none)" };
    }

    private static void ValidateLights(ZippoSaveState state)
    {
        if (state.Templates == null || state.Disco == null || state.OriginalColors == null || state.DraftRecipe == null || state.Queues == null
            || state.Templates.Count > 100000 || state.Disco.Count > 100000 || state.OriginalColors.Count > 100000)
            throw new InvalidOperationException("Invalid saved light list.");
        // Unapplied authoring values can be incomplete; Start Disco validates them when used.
        if (state.OriginalColors.Any(c => c.Target == null || !Finite(c.Color)))
            throw new InvalidOperationException("Invalid saved original light color.");
        foreach (var t in state.Templates)
            if (string.IsNullOrWhiteSpace(t.TemplateId) || t.ComponentIndex < 0 || !Finite(t.Color) || !Finite(t.OriginalColor)
                || !float.IsFinite(t.Intensity) || !float.IsFinite(t.OriginalIntensity))
                throw new InvalidOperationException("Invalid saved light appearance.");
        foreach (var light in state.Disco)
        {
            light.Recipe.Validate();
            if (light.Target == null || light.Actuators == null || light.Actuators.Count > 1000
                || !double.IsFinite(light.Elapsed) || light.Elapsed < 0 || light.Elapsed > 1e12
                || !Phase(light.ColorPhase) || !Phase(light.ActuationPhase) || !Phase(light.SpreadPhase)
                || light.Actuators.Select(a => a.ModuleIndex).Distinct().Count() != light.Actuators.Count
                || light.Actuators.Any(a => a.ModuleIndex < 0 || !float.IsFinite(a.OriginalGoal)))
                throw new InvalidOperationException("Invalid saved Disco timing or actuator baseline.");
        }
        if (state.Queues.Count > 100000) throw new InvalidOperationException("Too many saved light queues.");
        foreach (var queue in state.Queues)
        {
            if (queue.Target == null || queue.Animations == null || queue.Animations.Count > LightAnimationManager.MaxQueueDepth + 1)
                throw new InvalidOperationException("Invalid saved light queue.");
            foreach (var animation in queue.Animations) animation.Validate();
        }
    }

    private static bool Phase(double value) => double.IsFinite(value) && value >= 0 && value <= 3600;
    private static bool Finite(float3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private void RestoreLights(ZippoSaveState state, SaveRestoreContext context)
    {
        _disco = state.DraftRecipe.Clone();
        foreach (var template in state.Templates)
        {
            try { LightController.RestoreTemplateChange(template); }
            catch (Exception ex) { context.Warn($"{template.TemplateId}: {ex.Message}"); }
        }
        foreach (var original in state.OriginalColors)
        {
            var part = original.Target.Resolve();
            if (part == null) context.Warn($"{original.Target.VehicleId}: original light-color target is unavailable.");
            else _originalColors[part] = original.Color;
        }
        var claimed = new HashSet<KeyframeAnimationModule>();
        foreach (var saved in state.Disco)
        {
            try
            {
                var part = saved.Target.Resolve();
                context.Require(part != null && part.Modules.Get<LightModule>().Length > 0, "Disco target light is unavailable.");
                context.Require(!_discoLights.ContainsKey(part!), "Duplicate saved Disco target.");
                var modules = part!.FullPart.Modules.Get<KeyframeAnimationModule>().ToArray();
                context.Require(saved.Actuators.All(a => a.ModuleIndex < modules.Length
                    && !claimed.Contains(modules[a.ModuleIndex])), "Disco actuator is missing or owned by another light.");
                var live = new DiscoLight(part, saved);
                _discoLights.Add(part, live);
                foreach (var actuator in live.Actuators) claimed.Add(actuator);
            }
            catch (Exception ex) { context.Warn($"{saved.Target.VehicleId}: {ex.Message}"); }
        }
        foreach (var queue in state.Queues)
        {
            try
            {
                var part = queue.Target.Resolve();
                context.Require(part != null && LightController.HasLights(part.Template), "Queued light target is unavailable.");
                context.Require(!_discoLights.ContainsKey(part!), "A saved light cannot run Disco and a transition queue together.");
                _animationManager.RestoreSaved(PartKey(part!), queue.Animations);
            }
            catch (Exception ex) { context.Warn($"{queue.Target.VehicleId}: {ex.Message}"); }
        }
    }
}
