using System.Collections.Generic;
using RabbitEars.Net;
using RabbitEars.Pal;
using RabbitEars.Rf;
using RabbitEars.Sources;

namespace RabbitEars.Live;

/// <summary>The head of the live pipeline: whatever makes the wideband signal, one block at a time.</summary>
public interface IWidebandProducer
{
    ChannelPlan Plan { get; }

    /// <summary>Sync-tip amplitude of a 0 dB channel in the wideband signal (the tuner's level reference).</summary>
    double ReferenceAmplitude { get; }

    /// <summary>Fills the block with the next <paramref name="lowRateSamples"/> × K wideband samples. One thread only.</summary>
    void Produce(float[] block, int lowRateSamples);

    IReadOnlyList<LiveChannel> Channels { get; }
}

/// <summary>What the UI knows about one channel of the band.</summary>
public sealed class LiveChannel
{
    public LiveChannel(int slot, string name, string source, double carrierHz)
    {
        Slot = slot;
        Name = name;
        Source = source;
        CarrierHz = carrierHz;
    }

    public int Slot { get; }
    public string Name { get; set; }
    public string Source { get; }
    public double CarrierHz { get; }
    public double LevelDb { get; set; }

    /// <summary>Set for channels encoded in this process: their transmitted-signal impairments are adjustable.</summary>
    public PalEncoder? Encoder { get; init; }

    public IFrameSource? FrameSource { get; init; }

    /// <summary>Set for channels fed by a network sender.</summary>
    public SenderSlot? Sender { get; init; }

    /// <summary>The transmitter, once the mux thread has added it (null for file playback).</summary>
    public MuxChannel? Transmitter { get; set; }

    /// <summary>False for channels baked into a file.</summary>
    public bool LevelAdjustable { get; init; } = true;
}
