using System;
using System.Collections.Generic;
using KSA;
using Brutal.Numerics;

namespace MeowSci.ZippoLib;

/// <summary>
/// Manages per-part animation queues. At most one animation runs per part at a time;
/// additional animations queue up to <see cref="MaxQueueDepth"/>.
/// </summary>
public partial class LightAnimationManager
{
    public const int MaxQueueDepth = 25;

    // Key = runtime-unique Part.InstanceId token. Part.Id is only a template/subpart name and can collide.
    private readonly Dictionary<string, LightAnimation> _active = new();
    private readonly Dictionary<string, Queue<LightAnimation>> _queues = new();

    /// <summary>Returns the active animation for the part, or null.</summary>
    public LightAnimation? GetActiveAnimation(string partKey) =>
        _active.TryGetValue(partKey, out var anim) ? anim : null;

    /// <summary>Returns the number of queued (not yet active) animations for the part.</summary>
    public int GetQueueCount(string partKey) =>
        _queues.TryGetValue(partKey, out var q) ? q.Count : 0;

    /// <summary>Returns true if an animation is currently active for the part.</summary>
    public bool IsAnimating(string partKey) => _active.ContainsKey(partKey);

    /// <summary>
    /// Enqueues an animation for the part. If no animation is active, starts immediately.
    /// Returns false if the queue is full (MaxQueueDepth reached).
    /// </summary>
    public bool Enqueue(string partKey, LightAnimation animation)
    {
        if (!_active.ContainsKey(partKey))
        {
            _active[partKey] = animation;
            return true;
        }

        if (!_queues.TryGetValue(partKey, out var queue))
        {
            queue = new Queue<LightAnimation>();
            _queues[partKey] = queue;
        }

        if (queue.Count >= MaxQueueDepth)
            return false;

        queue.Enqueue(animation);
        return true;
    }

    /// <summary>
    /// Ticks all active animations. For each completed animation, applies end values to the
    /// part and promotes the next queued animation (re-capturing start state from current part values).
    /// </summary>
    /// <param name="dt">Delta time in seconds.</param>
    /// <param name="partResolver">Resolves a Part from its ID. Return null if the part is unavailable.</param>
    public void Update(double dt, Func<string, Part?> partResolver)
    {
        var keys = new List<string>(_active.Keys);
        foreach (var partKey in keys)
        {
            var part = partResolver(partKey);
            if (part == null)
            {
                // Part no longer available — cancel all animations for it
                _active.Remove(partKey);
                _queues.Remove(partKey);
                continue;
            }

            var anim = _active[partKey];
            var (color, intensity) = anim.Update(dt);

            LightController.ApplyColor(part, color);
            LightController.ApplyIntensity(part, intensity);

            if (anim.IsComplete)
            {
                _active.Remove(partKey);
                PromoteNext(partKey, part);
            }
        }
    }

    /// <summary>Cancels all animations (active and queued) for a specific part.</summary>
    public void CancelAll(string partKey)
    {
        _active.Remove(partKey);
        _queues.Remove(partKey);
    }

    /// <summary>Cancels all animations across all parts.</summary>
    public void Clear()
    {
        _active.Clear();
        _queues.Clear();
    }

    private void PromoteNext(string partKey, Part part)
    {
        if (!_queues.TryGetValue(partKey, out var queue) || queue.Count == 0)
        {
            _queues.Remove(partKey);
            return;
        }

        var next = queue.Dequeue();
        if (queue.Count == 0)
            _queues.Remove(partKey);

        // Re-capture current part values as start state
        var currentColor = LightController.ReadColor(part.Template);
        var currentIntensity = LightController.ReadIntensity(part.Template);

        var corrected = new LightAnimation(
            currentColor, next.EndColor,
            currentIntensity, next.EndIntensity,
            next.DurationSeconds, next.Easing,
            next.EasingPowerStart, next.EasingPowerEnd);

        _active[partKey] = corrected;
    }
}
