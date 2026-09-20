using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RabbitEars.Rf;

namespace RabbitEars.Live;

/// <summary>Plays a wideband capture in a loop. K, sample format and the channel list come from its sidecar.</summary>
public sealed class FileProducer : IWidebandProducer, IDisposable
{
    private readonly WidebandReader _reader;
    private readonly LiveChannel[] _channels;

    public FileProducer(string path)
    {
        _reader = new WidebandReader(path);
        WidebandFileInfo info = _reader.Info;
        Plan = new ChannelPlan(info.K);
        if (info.SampleCount <= 0) throw new InvalidDataException($"{path}: the sidecar says the capture is empty");
        ReferenceAmplitude = info.ReferenceCarrierAmplitude > 0 ? info.ReferenceCarrierAmplitude : 1.0 / Plan.MaxChannels;
        DurationSeconds = info.SampleCount / Plan.WidebandRate;
        _channels = info.Channels.OrderBy(c => c.Slot)
            .Select(c => new LiveChannel(c.Slot, c.Name, c.Source, c.CarrierHz) { LevelDb = c.LevelDb, LevelAdjustable = false })
            .ToArray();
    }

    public ChannelPlan Plan { get; }
    public double ReferenceAmplitude { get; }
    public double DurationSeconds { get; }
    public long Loops { get; private set; }
    public IReadOnlyList<LiveChannel> Channels => _channels;

    public void Produce(float[] block, int lowRateSamples)
    {
        int want = lowRateSamples * Plan.K, done = 0, emptyReads = 0;
        while (done < want)
        {
            int n = _reader.Read(block.AsSpan(done, want - done));
            done += n;
            if (n > 0)
            {
                emptyReads = 0;
                continue;
            }
            if (++emptyReads > 1) throw new InvalidDataException("the wideband file holds no samples");
            _reader.Rewind();                                             // the loop seam is a real discontinuity: the set re-locks
            Loops++;
        }
    }

    public void Dispose() => _reader.Dispose();
}
