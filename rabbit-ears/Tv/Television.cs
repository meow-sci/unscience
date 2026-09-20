using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace RabbitEars.Tv;

/// <summary>Where the tuner's IF goes: a television, or a counter in tests.</summary>
public interface IIfSink
{
    /// <summary>One block of the 40 MS/s IF as little-endian s16. May hold the caller back (back-pressure).</summary>
    void Feed(ReadOnlySpan<byte> s16);
}

/// <summary>
/// The television set: owns the PALindrome decoder processes. The decoder's knobs are fixed when it starts, so
/// a knob change starts a new process. Seamless: the new one is fed the same IF beside the old one and goes on
/// screen after <see cref="WarmFields"/> fields, once it has locked. A decoder that dies before its first
/// picture almost always rejected its settings: that is reported and the last good settings come back.
/// </summary>
public sealed class Television : IIfSink, IDisposable
{
    public const int WarmFields = 30;
    private const int MaxConsecutiveFailures = 4;
    private readonly string _binary;
    private readonly FrameHub _hub;
    private readonly object _gate = new();
    private Dictionary<string, string> _knobs = SetKnobs.Defaults();
    private Dictionary<string, string> _goodKnobs = SetKnobs.Defaults();
    private PalindromeProcess? _active, _pending, _provenGood;
    private int _generation, _failures;
    private long _lastRelaunch;
    private string _message = "";
    private bool _disposed;

    public Television(string binary, FrameHub hub)
    {
        _binary = binary;
        _hub = hub;
    }

    public string Message
    {
        get { lock (_gate) return _message; }
    }

    public bool Warming
    {
        get { lock (_gate) return _pending is not null; }
    }

    public Dictionary<string, string> Knobs
    {
        get { lock (_gate) return new Dictionary<string, string>(_knobs); }
    }

    /// <summary>The decoder on screen, for stats (may be null between decoders).</summary>
    public PalindromeProcess? Active
    {
        get { lock (_gate) return _active; }
    }

    public IReadOnlyList<int> DecoderPids
    {
        get { lock (_gate) return new[] { _active, _pending }.Where(p => p is not null && p.Alive).Select(p => p!.Pid).ToArray(); }
    }

    /// <summary>Starts a decoder with the current knobs: beside the old one (seamless) or instead of it.</summary>
    public void Launch(bool seamless) => Launch(seamless, keepMessage: false);

    private void Launch(bool seamless, bool keepMessage)
    {
        lock (_gate)
        {
            if (_disposed) return;
            PalindromeProcess instance = Spawn(_knobs);
            _pending?.Retire();
            if (seamless && _active is not null && _active.Alive)
            {
                _pending = instance;
                _message = "warming up the new set beside the old one ...";
                return;
            }
            if (!keepMessage) _message = "";
            _active?.Retire();
            _pending = null;
            _active = instance;
        }
    }

    /// <summary>Applies validated knob values (see <see cref="Knob.Coerce"/>) and starts a decoder that uses them.</summary>
    public void ChangeKnobs(IReadOnlyDictionary<string, string> changes, bool seamless)
    {
        lock (_gate)
        {
            foreach ((string name, string value) in changes) _knobs[name] = value;
            Launch(seamless);
        }
    }

    /// <summary>Defaults plus a named look; an unknown or empty name is a plain reset.</summary>
    public void ApplyLook(string name, bool seamless)
    {
        Dictionary<string, string> knobs = SetKnobs.Defaults();
        if (SetKnobs.Looks.TryGetValue(name, out Dictionary<string, string>? look))
            foreach ((string knob, string value) in look) knobs[knob] = SetKnobs.ByName[knob].Coerce(value);
        ChangeKnobs(knobs, seamless);
    }

    public void Feed(ReadOnlySpan<byte> s16)
    {
        PalindromeProcess? active, pending;
        lock (_gate)
        {
            active = _active;
            pending = _pending;
        }
        pending?.Enqueue(s16, wait: false);
        if (active is not null && active.Alive)
        {
            active.Enqueue(s16, wait: true);
            return;
        }
        RelaunchIfDue();
    }

    private void RelaunchIfDue()
    {
        lock (_gate)
        {
            long now = Stopwatch.GetTimestamp();
            if (_disposed || _failures > MaxConsecutiveFailures || now - _lastRelaunch < Stopwatch.Frequency / 2) return;
            _lastRelaunch = now;
            // A decoder that died before its first picture is Failed()'s business (it reverts the knobs first).
            if (_active is null || (!_active.Alive && _active.Frames > 0)) Launch(seamless: false, keepMessage: true);
        }
    }

    private PalindromeProcess Spawn(Dictionary<string, string> knobs)
    {
        _generation++;
        return new PalindromeProcess(_binary, DecoderCommandLine.Build(knobs), knobs, _generation, OnFrame, OnExit);
    }

    private void OnFrame(PalindromeProcess instance, byte[] packet)
    {
        int fields = instance.Frames * instance.Stride;
        lock (_gate)
        {
            _failures = 0;
            if (ReferenceEquals(instance, _pending) && fields >= WarmFields)
            {
                _active?.Retire();                                        // the new set has locked: swap
                _active = instance;
                _pending = null;
            }
            if (!ReferenceEquals(instance, _active)) return;
            if (fields >= WarmFields && !ReferenceEquals(_provenGood, instance))
            {
                _provenGood = instance;
                _goodKnobs = new Dictionary<string, string>(instance.Knobs);
                if (!_message.StartsWith("decoder refused", StringComparison.Ordinal)) _message = "";
            }
        }
        _hub.Publish(packet, instance.Width, instance.Height, instance.Channels);
    }

    private void OnExit(PalindromeProcess instance)
    {
        lock (_gate)
        {
            if (_disposed || instance.Frames > 0 || !(ReferenceEquals(instance, _active) || ReferenceEquals(instance, _pending))) return;
        }
        Thread.Sleep(200);                                                // let the stderr reader collect the reason
        Failed(instance);
    }

    /// <summary>A decoder died before producing a picture: almost always a knob combination it rejects.</summary>
    private void Failed(PalindromeProcess instance)
    {
        lock (_gate)
        {
            if (_disposed) return;
            string reason = instance.StderrTail.Length > 0 ? instance.StderrTail : "exited without a message";
            _failures++;
            bool sameAsGood = instance.Knobs.Count == _goodKnobs.Count && instance.Knobs.All(kv => _goodKnobs.TryGetValue(kv.Key, out string? v) && v == kv.Value);
            if (sameAsGood || _failures > MaxConsecutiveFailures)
            {
                _message = $"decoder will not start: {reason}";
                Console.WriteLine($"rabbit-ears: {_message}");
                return;
            }
            _message = $"decoder refused these settings: {reason}  (reverted)";
            Console.WriteLine($"rabbit-ears: {_message}");
            _knobs = new Dictionary<string, string>(_goodKnobs);
            if (ReferenceEquals(instance, _pending))
            {
                _pending = null;
            }
            else if (ReferenceEquals(instance, _active))
            {
                _active = null;
                Launch(seamless: false, keepMessage: true);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _active?.Kill();
            _pending?.Kill();
        }
    }
}
