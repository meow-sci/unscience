using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.KiwisMarblesLib;

public sealed class SavedCelestialWeld
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public bool TargetIsVehicle { get; set; }
    public double3 Offset { get; set; }
    public string OriginalParent { get; set; } = "";
    public double OriginalTime { get; set; }
    public double3 OriginalPosition { get; set; }
    public double3 OriginalVelocity { get; set; }
}

public sealed partial class KiwisMarblesSubmod : ISaveParticipantSource
{
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return new SaveParticipant<List<SavedCelestialWeld>>("kiwis-marbles", CaptureSavedWelds,
            ResetSavedWelds, RestoreSavedWelds, 40, ValidateSavedWelds); }
    }

    private List<SavedCelestialWeld> CaptureSavedWelds() => _welds.Select(w =>
    {
        var orbit = w.OriginalOrbit ?? throw new InvalidOperationException("Celestial weld has no original orbit.");
        return new SavedCelestialWeld
        {
            Source = w.Source.Id, Target = ((Astronomical)w.Target).Id, TargetIsVehicle = w.Target is Vehicle,
            Offset = w.Offset, OriginalParent = ((Astronomical)orbit.Parent).Id,
            OriginalTime = orbit.StateVectors.StateTime.Seconds(), OriginalPosition = orbit.StateVectors.PositionCci,
            OriginalVelocity = orbit.StateVectors.VelocityCci
        };
    }).ToList();

    private void ResetSavedWelds()
    {
        // Called with solvers quiescent, while old celestials/vehicles still exist.
        foreach (var weld in _welds.AsEnumerable().Reverse()) CelestialWeldEngine.RestoreOrbit(weld);
        foreach (var weld in _pendingRestores) CelestialWeldEngine.RestoreOrbit(weld);
        _welds.Clear(); _pendingRestores.Clear(); _weldEditState.Clear(); _weldSurfaceState.Clear();
    }

    private static void ValidateSavedWelds(List<SavedCelestialWeld> saved)
    {
        if (saved.Count > 10000 || saved.Select(w => w.Source).Distinct().Count() != saved.Count)
            throw new InvalidOperationException("Invalid celestial weld list.");
        foreach (var w in saved)
        {
            if (string.IsNullOrWhiteSpace(w.Source) || string.IsNullOrWhiteSpace(w.Target)
                || string.IsNullOrWhiteSpace(w.OriginalParent) || w.Source == w.Target || !Finite(w.Offset)
                || !Finite(w.OriginalPosition) || !Finite(w.OriginalVelocity) || !double.IsFinite(w.OriginalTime))
                throw new InvalidOperationException("Invalid celestial weld or original orbit.");
            var seen = new HashSet<string> { w.Source };
            var next = w;
            while (!next.TargetIsVehicle)
            {
                if (!seen.Add(next.Target)) throw new InvalidOperationException("Saved celestial weld cycle.");
                next = saved.FirstOrDefault(item => item.Source == next.Target);
                if (next == null) break;
            }
        }
    }

    private static bool Finite(double3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    private void RestoreSavedWelds(List<SavedCelestialWeld> saved, SaveRestoreContext context)
    {
        var bodies = CelestialProvider.GetAllCelestials();
        foreach (var w in saved)
        {
            try
            {
                var source = bodies.FirstOrDefault(b => b.Id == w.Source);
                IOrbiter? target = w.TargetIsVehicle ? VehicleProvider.FindVehicle(w.Target) : bodies.FirstOrDefault(b => b.Id == w.Target);
                var parent = Universe.CurrentSystem?.All.UnsafeAsList().OfType<IParentBody>()
                    .FirstOrDefault(body => ((Astronomical)body).Id == w.OriginalParent);
                context.Require(source != null && target != null && parent != null, $"Unresolved celestial weld {w.Source} → {w.Target}.");
                context.Require(target!.Parent != null, "Cannot weld relative to an orbiter without a parent.");
                // A body cannot orbit itself or a descendant after reparenting.
                for (IParentBody? ancestor = target.Parent; ancestor is IOrbiter orbiter; ancestor = orbiter.Parent)
                    context.Require(!ReferenceEquals(ancestor, source), "Celestial weld would create a parent cycle.");
                var original = Orbit.CreateFromStateCci(parent!, new UniverseTime(w.OriginalTime),
                    w.OriginalPosition, w.OriginalVelocity, source!.OrbitColor);
                _welds.Add(new() { Source = source, Target = target, Offset = w.Offset, OriginalOrbit = original });
            }
            catch (Exception ex) { context.Warn($"{w.Source}: {ex.Message}"); }
        }
        // Include indirect cycles through vehicle parents, which ordinary target-only sorting misses.
        var proposed = _welds.ToDictionary(w => w.Source, w => w.Target.Parent);
        var cyclic = new HashSet<Celestial>();
        foreach (var source in proposed.Keys)
        {
            var seen = new HashSet<IParentBody>();
            IParentBody? parent = source;
            while (parent != null)
            {
                if (!seen.Add(parent)) { cyclic.Add(source); break; }
                parent = parent is Celestial body && proposed.TryGetValue(body, out var next)
                    ? next : (parent as IOrbiter)?.Parent;
            }
        }
        foreach (var source in cyclic)
        {
            _welds.RemoveAll(w => w.Source == source);
            context.Warn($"{source.Id}: restored celestial parent graph would contain a cycle.");
        }
        SortWelds();
        foreach (var weld in _welds.ToArray())
        {
            // Earlier dependencies can change this target's parent while the sorted chain applies.
            // Recheck the actual graph before calling the recursive native subtree refresh.
            var ancestors = new HashSet<IParentBody>();
            IParentBody? parent = weld.Target.Parent;
            bool safe = true;
            while (parent != null)
            {
                if (ReferenceEquals(parent, weld.Source) || !ancestors.Add(parent)) { safe = false; break; }
                parent = (parent as IOrbiter)?.Parent;
            }
            if (!safe)
            {
                _welds.Remove(weld);
                context.Warn($"{weld.Source.Id}: a restored dependency would make this body its own ancestor.");
            }
            else if (!CelestialWeldEngine.UpdateWeld(weld)) context.Warn($"{weld.Source.Id}: celestial weld is dormant.");
        }
    }
}
