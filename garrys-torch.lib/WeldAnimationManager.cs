using System;
using System.Collections.Generic;

namespace MeowSci.GarrysTorchLib;

/// <summary>
/// Manages active and queued weld animations. At most one animation runs per weld at a time;
/// additional animations are queued and started in order when the active one completes.
/// </summary>
public partial class WeldAnimationManager
{
    private readonly Dictionary<WeldEntry, WeldAnimation> _active = new();
    private readonly Dictionary<WeldEntry, Queue<WeldAnimation>> _queues = new();

    /// <summary>Returns the currently active animation for the weld, or null.</summary>
    public WeldAnimation? GetActiveAnimation(WeldEntry weld)
    {
        return _active.TryGetValue(weld, out var anim) ? anim : null;
    }

    /// <summary>
    /// Enqueues an animation for the weld. If no animation is currently active, it starts immediately.
    /// </summary>
    public void Enqueue(WeldEntry weld, WeldAnimation animation)
    {
        if (!_active.ContainsKey(weld))
        {
            _active[weld] = animation;
            return;
        }

        if (!_queues.TryGetValue(weld, out var queue))
        {
            queue = new Queue<WeldAnimation>();
            _queues[weld] = queue;
        }
        queue.Enqueue(animation);
    }

    /// <summary>
    /// Updates all active animations. When an animation completes, the next queued animation
    /// is started with corrected start state captured from the weld's current values.
    /// </summary>
    public void Update(double dt)
    {
        // Snapshot keys to allow mutation during iteration
        var keys = new List<WeldEntry>(_active.Keys);
        foreach (var weld in keys)
        {
            var anim = _active[weld];
            bool running = anim.Update(weld, dt);

            if (!running)
            {
                _active.Remove(weld);
                PromoteNext(weld);
            }
        }
    }

    /// <summary>Cancels active and queued animations for the specified weld.</summary>
    public void CancelAll(WeldEntry weld)
    {
        _active.Remove(weld);
        _queues.Remove(weld);
    }

    /// <summary>Removes all animations for all welds.</summary>
    public void Clear()
    {
        _active.Clear();
        _queues.Clear();
    }

    /// <summary>
    /// Promotes the next queued animation for a weld, re-capturing start state from the
    /// weld's current values so that stale start positions are never used.
    /// </summary>
    private void PromoteNext(WeldEntry weld)
    {
        if (!_queues.TryGetValue(weld, out var queue) || queue.Count == 0)
        {
            _queues.Remove(weld);
            return;
        }

        var next = queue.Dequeue();
        if (queue.Count == 0)
            _queues.Remove(weld);

        // Recapture start state from current weld values
        var corrected = new WeldAnimation(
            weld.Position, weld.Rotation, weld.Scale,
            next.TargetPosition, next.TargetRotation, next.TargetScale,
            next.DurationSeconds, next.Easing,
            next.EasingPowerStart, next.EasingPowerEnd);

        _active[weld] = corrected;
    }
}
