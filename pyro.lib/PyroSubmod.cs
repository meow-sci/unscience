using System;
using System.Collections.Generic;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.PyroLib;

/// <summary>
/// Pyro — standalone volumetric engine plumes. Each plume is welded to a vehicle part (or sub-part) with a
/// position/rotation offset and rendered through the game's own <see cref="VolumetricExhaustRenderer"/>,
/// so it looks, animates (startup/shutdown transients) and reacts to atmosphere exactly like a real engine.
/// This file holds state, lifecycle and the public API; the ImGui panels live in the partial files.
/// </summary>
public sealed partial class PyroSubmod : ISubmod
{
    public string Name => "Pyro - Engine Plumes";
    public string Tooltip => "Place standalone volumetric engine plumes welded to any vehicle part, no engine required.";

    public static PyroSubmod? Instance { get; private set; }

    private readonly List<PlumeEntry> _plumes = new();
    public IReadOnlyList<PlumeEntry> Plumes => _plumes;

    public void Initialize()
    {
        Instance = this;
        _presetManager.Initialize();
    }

    public void Update(double dt)
    {
        PruneDeadPlumes();
        double now = Universe.GetElapsedSeconds();
        foreach (var plume in _plumes) plume.Cycle.Update(now);
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##pyro_content");

        RenderCreateSection();

        if (_plumes.Count > 0)
        {
            ImGui.Spacing();
            ImGui.SeparatorText($"Active Plumes ( {_plumes.Count} )");
            RenderBulkToggles();

            PlumeEntry? toRemove = null;
            for (int i = 0; i < _plumes.Count; i++)
                RenderPlumeSection(_plumes[i], i, ref toRemove);
            if (toRemove != null)
                RemovePlume(toRemove);
        }

        ImGui.Spacing();
        RenderTemplateEditorSection();

        RenderPresetModals();

        SubmodUI.EndContentArea();
    }

    public void Dispose()
    {
        _plumes.Clear();
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    // ---- Render hook (called from the Harmony postfix, once per visible vehicle per frame) ----

    /// <summary>Submits every plume welded to <paramref name="vehicle"/> to the renderer for this frame.</summary>
    public void SubmitPlumes(Vehicle vehicle, Camera camera, VolumetricExhaustRenderer renderer, double frameDeltaTime)
    {
        if (_plumes.Count == 0 || renderer.Disabled) return;

        double simulationTime = Universe.GetElapsedSeconds();
        double simulationDeltaTime = frameDeltaTime * Universe.GetSimulationSpeed();
        float ambientPressurePa = PlumePhysics.AmbientPressurePa(camera);

        foreach (var plume in _plumes)
        {
            if (!ReferenceEquals(plume.Vehicle, vehicle)) continue;
            PlumeEmitter.Submit(plume, camera, renderer, simulationTime, simulationDeltaTime, ambientPressurePa);
        }
    }

    // ---- Public API ----

    /// <summary>Creates a plume welded to <paramref name="part"/> on <paramref name="vehicle"/>.</summary>
    public (PlumeEntry? Plume, string? Error) CreatePlume(Vehicle vehicle, Part part, string templateId,
        float3 position, float3 rotation, NozzleSettings? nozzle = null,
        float throttle = 1f, float absorptionDensityScale = 1f, float refractionIntensity = 1f)
    {
        var instance = PlumeTemplates.CreateInstance(templateId);
        if (instance == null)
            return (null, $"Unknown exhaust template '{templateId}'.");

        var plume = new PlumeEntry
        {
            Vehicle = vehicle,
            Part = part,
            TemplateId = templateId,
            Position = position,
            Rotation = rotation,
            Throttle = throttle,
            Nozzle = nozzle?.Clone() ?? new NozzleSettings(),
            AbsorptionDensityScale = absorptionDensityScale,
            RefractionIntensity = refractionIntensity,
            Instance = instance,
        };
        _plumes.Add(plume);
        Console.WriteLine($"pyro: created plume #{plume.Id} on {vehicle.Id}/{part.Id} [{templateId}]");
        return (plume, null);
    }

    /// <summary>Switches a plume to a different exhaust template (restarts its startup transient).</summary>
    public bool SetTemplate(PlumeEntry plume, string templateId)
    {
        var instance = PlumeTemplates.CreateInstance(templateId);
        if (instance == null) return false;
        plume.TemplateId = templateId;
        plume.Instance = instance;
        return true;
    }

    public PlumeEntry? FindPlume(int id)
    {
        foreach (var p in _plumes)
            if (p.Id == id) return p;
        return null;
    }

    public void RemovePlume(PlumeEntry plume)
    {
        if (_plumes.Remove(plume))
            Console.WriteLine($"pyro: removed plume #{plume.Id}");
    }

    public void SetAllEnabled(bool enabled)
    {
        foreach (var p in _plumes) SetEnabled(p, enabled);
    }

    /// <summary>Manual on/off takes control back from any running cycle.</summary>
    public void SetEnabled(PlumeEntry plume, bool enabled)
    {
        plume.Cycle.Stop();
        plume.Enabled = enabled;
    }

    // ---- Preset API ----

    public string[] GetPresetNames() => _presetManager.GetPresetNames();
    public PlumePreset? GetPreset(string name) => _presetManager.GetPreset(name);
    public bool PresetExists(string name) => _presetManager.PresetExists(name);
    public bool SavePreset(string name, PlumePreset preset) => _presetManager.SavePreset(name, preset);
    public bool DeletePreset(string name) => _presetManager.DeletePreset(name);

    /// <summary>Applies every preset setting to an existing plume. Returns false if the preset's
    /// template doesn't exist; a template change restarts the plume's startup transient.</summary>
    public bool ApplyPreset(PlumeEntry plume, PlumePreset preset)
    {
        if (preset.TemplateId != plume.TemplateId && !SetTemplate(plume, preset.TemplateId))
            return false;
        plume.Position = preset.Position;
        plume.Rotation = preset.Rotation;
        plume.Throttle = preset.Throttle;
        plume.Nozzle = preset.Nozzle.Clone();
        plume.AbsorptionDensityScale = preset.AbsorptionDensityScale;
        plume.RefractionIntensity = preset.RefractionIntensity;
        return true;
    }

    // ---- Internal ----

    /// <summary>Drops plumes whose vehicle no longer exists or whose part left the vehicle's tree.</summary>
    private void PruneDeadPlumes()
    {
        if (_plumes.Count == 0) return;
        var vehicles = VehicleProvider.GetAllVehicles();
        for (int i = _plumes.Count - 1; i >= 0; i--)
        {
            var plume = _plumes[i];
            if (!vehicles.Contains(plume.Vehicle) || !PartStillOnVehicle(plume))
            {
                Console.WriteLine($"pyro: plume #{plume.Id} lost its anchor ({plume.Vehicle.Id}/{plume.Part.Id}); removing");
                _plumes.RemoveAt(i);
            }
        }
    }

    private static bool PartStillOnVehicle(PlumeEntry plume)
    {
        var root = plume.Part;
        while (root.PartParent != null) root = root.PartParent;
        foreach (var top in plume.Vehicle.Parts.Parts)
            if (ReferenceEquals(top, root)) return true;
        return false;
    }
}
