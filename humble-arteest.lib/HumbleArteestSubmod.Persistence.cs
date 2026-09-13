using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.HumbleArteestLib;

public sealed partial class HumbleArteestSubmod
{
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return new SaveParticipant<SavedPaint>("humble-arteest", CapturePaint, ResetPaint, RestorePaint, order: 80, validate: ValidatePaint); }
    }

    private static void ValidatePaint(SavedPaint saved)
    {
        if (!Enum.IsDefined(saved.Blend) || saved.Templates == null || saved.Parts == null
            || saved.MaterialColors == null || saved.Emissive == null || saved.Templates.Count > 10000
            || saved.Parts.Count > 100000 || saved.MaterialColors.Count > 100000 || saved.Emissive.Count > 100000
            || saved.Templates.Keys.Concat(saved.MaterialColors.Keys).Any(k => string.IsNullOrWhiteSpace(k) || k.Length > 4096)
            || saved.Parts.Any(p => p == null || p.Part == null)
            || saved.Emissive.Any(p => p == null || p.Part == null || p.ModuleIndex is < 0 or > 10000))
            throw new InvalidOperationException("Invalid saved paint/material records.");
        foreach (var part in saved.Parts.Select(p => p.Part).Concat(saved.Emissive.Select(p => p.Part)))
            if (string.IsNullOrWhiteSpace(part.VehicleId) || string.IsNullOrWhiteSpace(part.TemplateId)
                || part.TreePath == null || part.SubPartPath == null || part.TreePath.Length > 256 || part.SubPartPath.Length > 64
                || part.TreePath.Concat(part.SubPartPath).Any(i => i < 0))
                throw new InvalidOperationException("Invalid saved paint target address.");
    }

    private SavedPaint CapturePaint()
    {
        var saved = new SavedPaint
        {
            Enabled = VehiclePaint.Active, Blend = VehiclePaint.BlendMode, GlobalEnabled = VehiclePaint.GlobalEnabled,
            GlobalColor = VehiclePaint.GlobalColor, MaterialColors = KittenColor.CaptureColors(),
            HideVisor = KittenVisorPatches.Hidden, GlobalEmissive = EngineEmissive.GlobalEnabled,
            Temperature = EngineEmissive.GlobalTemperature, Tfi = EngineEmissive.GlobalTfi
        };
        foreach (string template in VehiclePaint.PaintedTemplates)
            if (VehiclePaint.TryGetTemplateColor(template, out var color)) saved.Templates[template] = color;
        foreach (var vehicle in VehicleProvider.GetAllVehicles(includeDebris: true))
        {
            if (vehicle.IsDisposed) continue;
            foreach (var part in PartHelpers.GetAllParts(vehicle))
            {
                if (VehiclePaint.TryGetPartColor(part, out var color))
                    saved.Parts.Add(new() { Part = SavedPartReference.Capture(vehicle, part), Color = color });
                var modules = part.Modules.Get<PartModelDynamicModule>();
                for (int i = 0; i < modules.Length; i++)
                {
                    var settings = EngineEmissive.GetSettings(modules[i].PartModelDynamic);
                    if (settings == null) continue;
                    saved.Emissive.Add(new()
                    {
                        Part = SavedPartReference.Capture(vehicle, part), ModuleIndex = i,
                        Temperature = settings.Value.Temperature, Tfi = settings.Value.Tfi
                    });
                }
            }
        }
        return saved;
    }

    private void ResetPaint()
    {
        VehiclePaint.Cleanup();
        _vehiclePaint.ClearSavedTargets();
        EngineEmissive.ClearAll();
        KittenVisorPatches.Hidden = false;
        _kittenColor.SynchronizeSavedState(false);
        _engineEmissive.SynchronizeSavedState(false);
        KittenColor.ResetOwnedColors();
    }

    private void RestorePaint(SavedPaint saved, SaveRestoreContext context)
    {
        context.Require(Enum.IsDefined(saved.Blend), "Unknown paint blend mode.");
        VehiclePaint.BlendMode = saved.Blend;
        VehiclePaint.GlobalColor = saved.GlobalColor;
        VehiclePaint.GlobalEnabled = saved.GlobalEnabled;
        foreach (var template in saved.Templates) VehiclePaint.SetTemplate(template.Key, template.Value);
        foreach (var item in saved.Parts)
        {
            var part = item.Part.Resolve();
            if (part == null) { context.Warn($"Paint target missing: {item.Part.VehicleId}."); continue; }
            VehiclePaint.SetPart(part, item.Color);
        }
        if (saved.Enabled && !VehiclePaint.Enable()) context.Warn(VehiclePaint.LastError ?? "Paint shader activation failed.");
        EngineEmissive.GlobalEnabled = saved.GlobalEmissive;
        EngineEmissive.GlobalTemperature = saved.Temperature;
        EngineEmissive.GlobalTfi = saved.Tfi;
        foreach (var item in saved.Emissive)
        {
            var part = item.Part.Resolve();
            if (part == null) { context.Warn($"Engine glow target missing: {item.Part.VehicleId}."); continue; }
            var modules = part.Modules.Get<PartModelDynamicModule>();
            if (item.ModuleIndex < 0 || item.ModuleIndex >= modules.Length)
            { context.Warn($"Engine glow module missing: {item.Part.VehicleId}."); continue; }
            EngineEmissive.SetEngine(modules[item.ModuleIndex].PartModelDynamic, item.Temperature, item.Tfi);
        }
        KittenColor.RestoreColors(saved.MaterialColors, context);
        KittenVisorPatches.Hidden = saved.HideVisor;
        _kittenColor.SynchronizeSavedState(saved.HideVisor || saved.MaterialColors.Count > 0);
        _engineEmissive.SynchronizeSavedState(saved.GlobalEmissive || saved.Emissive.Count > 0);
    }

    public sealed class SavedPaint
    {
        public bool Enabled { get; set; }
        public PaintBlendMode Blend { get; set; }
        public bool GlobalEnabled { get; set; }
        public float3 GlobalColor { get; set; }
        public Dictionary<string, float3> Templates { get; set; } = new();
        public List<SavedPartPaint> Parts { get; set; } = new();
        public Dictionary<string, float4> MaterialColors { get; set; } = new();
        public bool HideVisor { get; set; }
        public bool GlobalEmissive { get; set; }
        public float Temperature { get; set; }
        public float Tfi { get; set; }
        public List<SavedEmissive> Emissive { get; set; } = new();
    }

    public sealed class SavedPartPaint
    {
        public SavedPartReference Part { get; set; } = new();
        public float3 Color { get; set; }
    }

    public sealed class SavedEmissive
    {
        public SavedPartReference Part { get; set; } = new();
        public int ModuleIndex { get; set; }
        public float Temperature { get; set; }
        public float Tfi { get; set; }
    }
}
