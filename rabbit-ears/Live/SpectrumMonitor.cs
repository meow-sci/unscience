using System;
using System.Diagnostics;
using RabbitEars.Dsp;

namespace RabbitEars.Live;

/// <summary>
/// Keeps a recent snapshot of the wideband signal and turns it into a power spectrum on demand
/// (Welch-averaged, 0 … Fw/2). Offer is called from the tuner thread; GetDb from web requests.
/// </summary>
public sealed class SpectrumMonitor
{
    public const int FftSize = 2048;                                      // → 1025 bins
    private const int SnapshotSamples = FftSize * 24;
    private static readonly long SnapshotInterval = Stopwatch.Frequency / 5;
    private readonly object _gate = new();
    private readonly float[] _snapshot = new float[SnapshotSamples];
    private double[] _db = Array.Empty<double>();
    private long _lastSnapshot;
    private bool _fresh;

    public void Offer(ReadOnlySpan<float> wideband)
    {
        long now = Stopwatch.GetTimestamp();
        if (now - _lastSnapshot < SnapshotInterval || wideband.Length < SnapshotSamples) return;
        _lastSnapshot = now;
        lock (_gate)
        {
            wideband.Slice(0, SnapshotSamples).CopyTo(_snapshot);
            _fresh = true;
        }
    }

    /// <summary>dB per bin, bin k at k · Fw / <see cref="FftSize"/>; a full-scale sine reads 0 dB. Empty before the first block.</summary>
    public double[] GetDb()
    {
        lock (_gate)
        {
            if (_fresh)
            {
                _db = Fft.PowerSpectrumDb(_snapshot, FftSize);
                _fresh = false;
            }
            return _db;
        }
    }
}
