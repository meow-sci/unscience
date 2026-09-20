using System;
using System.Threading;
using RabbitEars.Pal;
using RabbitEars.Sources;

namespace RabbitEars.Net;

/// <summary>
/// One claimed slot of the sender server: a bounded FIFO between the network thread (<see cref="Write"/>) and the
/// mux (<see cref="Read"/>). While the FIFO is empty the slot transmits a locally generated "no programme" signal
/// (black picture, sync and burst). Positions are sample indices of the sender's stream: the local generator is
/// seeked to the position the programme stopped at, and the programme resumes only when its next sample falls on
/// the same point of the 8-field PAL sequence, so sync, V switch and subcarrier never jump at either transition.
/// </summary>
public sealed class SenderSlot : ICvbsSource
{
    /// <summary>The PAL sequence repeats every 2500 lines = 4 frames: positions are compared modulo this.</summary>
    public const long SequenceSamples = 4L * PalTiming.SamplesPerFrame;
    public const int DefaultCapacity = PalTiming.SampleRate / 2;          // 500 ms
    public const int DefaultPrefill = PalTiming.SampleRate / 20;          // 50 ms queued before the programme (re)starts

    private readonly object _gate = new();
    private readonly short[] _ring;
    private readonly int _prefill;
    private readonly PalEncoder _noProgramme = new(new BlackSource());
    private int _readAt, _count;
    private long _tailIndex;                                              // stream index of the next sample to be written
    private long _position;                                               // stream index of the next sample to transmit
    private bool _onProgramme, _localSeeked, _closed;
    private long _underruns, _samplesReceived;

    public SenderSlot(int slot, int capacitySamples = DefaultCapacity, int prefillSamples = DefaultPrefill)
    {
        Slot = slot;
        _ring = new short[capacitySamples];
        _prefill = Math.Min(prefillSamples, capacitySamples / 2);
    }

    public int Slot { get; }
    public string Name { get; set; } = "";
    public CvbsFormat Format { get; set; }

    /// <summary>True while a sender's connection owns this slot.</summary>
    public bool Connected { get; set; }

    public bool OnProgramme => Volatile.Read(ref _onProgramme);
    public long Underruns => Interlocked.Read(ref _underruns);
    public long SamplesReceived => Interlocked.Read(ref _samplesReceived);

    public double FifoMilliseconds
    {
        get { lock (_gate) return _count * 1000.0 / PalTiming.SampleRate; }
    }

    /// <summary>
    /// Queues one received block (s16 scale). Blocks while the FIFO is full — that is the back-pressure a sender
    /// feels through TCP. A gap or jump in the index discards what was queued: the stream restarts from there.
    /// Returns false when the slot was closed while waiting.
    /// </summary>
    public bool Write(ReadOnlySpan<short> samples, long firstSampleIndex)
    {
        lock (_gate)
        {
            if (firstSampleIndex != _tailIndex)
            {
                _count = 0;
                _tailIndex = firstSampleIndex;
            }
            int done = 0;
            while (done < samples.Length)
            {
                while (_count == _ring.Length && !_closed) Monitor.Wait(_gate, 100);
                if (_closed) return false;
                int writeAt = (int)(((long)_readAt + _count) % _ring.Length);
                int run = Math.Min(samples.Length - done, Math.Min(_ring.Length - _count, _ring.Length - writeAt));
                samples.Slice(done, run).CopyTo(_ring.AsSpan(writeAt, run));
                _count += run;
                _tailIndex += run;
                done += run;
            }
            Interlocked.Add(ref _samplesReceived, samples.Length);
            return true;
        }
    }

    /// <summary>Unblocks a waiting writer for good (server shutdown).</summary>
    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            Monitor.PulseAll(_gate);
        }
    }

    public void Read(Span<float> cvbs)
    {
        int done = 0;
        while (done < cvbs.Length)
        {
            int run = _onProgramme ? ReadProgramme(cvbs.Slice(done)) : ReadNoProgramme(cvbs.Slice(done));
            done += run;
        }
    }

    private int ReadProgramme(Span<float> target)
    {
        lock (_gate)
        {
            int run = Math.Min(target.Length, Math.Min(_count, _ring.Length - _readAt));
            if (run == 0)
            {
                Volatile.Write(ref _onProgramme, false);
                _localSeeked = false;
                Interlocked.Increment(ref _underruns);
                return 0;
            }
            CvbsStreamCodec.S16ToVolts(_ring.AsSpan(_readAt, run), target);
            _readAt = (_readAt + run) % _ring.Length;
            _count -= run;
            _position += run;
            Monitor.PulseAll(_gate);
            return run;
        }
    }

    private int ReadNoProgramme(Span<float> target)
    {
        int run = target.Length;
        lock (_gate)
        {
            if (_count >= _prefill)
            {
                long headIndex = _tailIndex - _count;
                long untilAligned = ((headIndex - _position) % SequenceSamples + SequenceSamples) % SequenceSamples;
                if (untilAligned == 0)
                {
                    _position = headIndex;
                    Volatile.Write(ref _onProgramme, true);
                    return 0;
                }
                run = (int)Math.Min(run, untilAligned);
            }
        }
        if (!_localSeeked)
        {
            _noProgramme.Seek(_position % SequenceSamples);
            _localSeeked = true;
        }
        _noProgramme.Read(target.Slice(0, run));
        _position += run;
        return run;
    }

    /// <summary>A black picture for the "no programme" signal.</summary>
    private sealed class BlackSource : IFrameSource
    {
        private readonly VideoFrame _frame = new(new byte[8 * 8 * 3], 8, 8);

        public string Name => "no programme";

        public VideoFrame GetFrame(long frameIndex) => _frame;

        public void Dispose()
        {
        }
    }
}
