using System;
using System.Collections.Generic;
using System.Linq;
using RabbitEars.Live;
using RabbitEars.Rf;
using RabbitEars.Tv;

namespace RabbitEars.Web;

/// <summary>Builds the JSON documents of /api/state and /api/knobs from a running session.</summary>
public static class StateReport
{
    public static Dictionary<string, object?> State(LiveSession session) => new()
    {
        ["mode"] = session.Mode,
        ["plan"] = Plan(session.Plan),
        ["channels"] = session.Producer.Channels.Select(Channel).ToArray(),
        ["tuner"] = Tuner(session),
        ["stats"] = Stats(session),
        ["decoder"] = Decoder(session.Television),
        ["set_values"] = SetValues(session.Television),
    };

    public static Dictionary<string, object?> Knobs(LiveSession session) => new()
    {
        ["set"] = SetKnobs.All.Select(k => k.ToJson()).ToArray(),
        ["set_values"] = SetValues(session.Television),
        ["set_open_groups"] = SetKnobs.OpenGroups,
        ["looks"] = SetKnobs.Looks.Keys.ToArray(),
        ["signal"] = SignalKnobs.All.Select(k => k.ToJson()).ToArray(),
    };

    public static Dictionary<string, object> SetValues(Television television) =>
        television.Knobs.ToDictionary(kv => kv.Key, kv => SetKnobs.ByName[kv.Key].JsonValue(kv.Value));

    public static Dictionary<string, object?> Plan(ChannelPlan plan) => new()
    {
        ["k"] = plan.K,
        ["wideband_rate_hz"] = plan.WidebandRate,
        ["nyquist_hz"] = plan.WidebandRate / 2,
        ["if_rate_hz"] = ChannelPlan.IfRate,
        ["if_carrier_hz"] = ChannelPlan.IfCarrierHz,
        ["max_channels"] = plan.MaxChannels,
        ["slots"] = Enumerable.Range(0, plan.MaxChannels).Select(slot => new Dictionary<string, object>
        {
            ["slot"] = slot, ["carrier_hz"] = plan.CarrierHz(slot),
            ["low_hz"] = plan.SlotEdgesHz(slot).Low, ["high_hz"] = plan.SlotEdgesHz(slot).High,
        }).ToArray(),
    };

    public static Dictionary<string, object?> Channel(LiveChannel channel) => new()
    {
        ["slot"] = channel.Slot,
        ["name"] = channel.Sender?.Name ?? channel.Name,
        ["carrier_hz"] = channel.CarrierHz,
        ["level_db"] = channel.LevelDb,
        ["source"] = channel.Source,
        ["level_adjustable"] = channel.LevelAdjustable,
        ["signal"] = channel.Encoder is null ? null : SignalKnobs.Values(channel.Encoder.Params),
        ["sender_connected"] = channel.Sender?.Connected,
        ["sender"] = channel.Sender is null ? null : new Dictionary<string, object>
        {
            ["connected"] = channel.Sender.Connected,
            ["on_programme"] = channel.Sender.OnProgramme,
            ["format"] = channel.Sender.Format.ToString().ToLowerInvariant(),
            ["fifo_ms"] = Math.Round(channel.Sender.FifoMilliseconds, 1),
            ["underruns"] = channel.Sender.Underruns,
            ["received_seconds"] = Math.Round(channel.Sender.SamplesReceived / ChannelPlan.ChannelRate, 2),
        },
    };

    public static Dictionary<string, object?> Tuner(LiveSession session)
    {
        double frequency = session.Tuner.FrequencyHz;
        int slot = session.Plan.NearestSlot(frequency);
        return new Dictionary<string, object?>
        {
            ["frequency_hz"] = frequency,
            ["slot"] = slot,
            ["offset_hz"] = slot >= 0 ? frequency - session.Plan.CarrierHz(slot) : null,
            ["passband_low_hz"] = frequency + ChannelPlan.LowerEdgeHz,
            ["passband_high_hz"] = frequency + ChannelPlan.UpperEdgeHz,
            ["atten_db"] = session.Tuner.AttenuationDb,
            ["noise_db"] = session.NoiseDb,
            ["afc"] = "unknown",
        };
    }

    private static Dictionary<string, object?> Stats(LiveSession session)
    {
        PipelineStats stats = session.Pipeline.Stats;
        (double decoderCpu, double processCpu) = session.CpuPercent();
        PalindromeProcess? active = session.Television.Active;
        return new Dictionary<string, object?>
        {
            ["mux_rtf"] = Math.Round(stats.Producer.RealTimeFactor, 2),
            ["mux_ms_per_block"] = Math.Round(stats.Producer.MillisecondsPerBlock, 2),
            ["tuner_rtf"] = Math.Round(stats.Tuner.RealTimeFactor, 2),
            ["tuner_ms_per_block"] = Math.Round(stats.Tuner.MillisecondsPerBlock, 2),
            ["block_ms"] = LivePipeline.BlockSeconds * 1000.0,
            ["clock_rate"] = Math.Round(stats.ClockRate, 3),
            ["late_ms"] = Math.Round(stats.LateMilliseconds, 1),
            ["resyncs"] = stats.Resyncs,
            ["lost_seconds"] = Math.Round(stats.LostSeconds, 2),
            ["queue_blocks"] = session.Pipeline.QueuedBlocks,
            ["decoder_queue_blocks"] = active?.QueuedBlocks ?? 0,
            ["decoder_dropped_blocks"] = active?.DroppedBlocks ?? 0,
            ["frames_per_s"] = session.Hub.FramesPerSecond,
            ["fields_per_s"] = session.Hub.FramesPerSecond * (active?.Stride ?? 1),
            ["decoder_cpu_percent"] = Math.Round(decoderCpu, 1),
            ["process_cpu_percent"] = Math.Round(processCpu, 1),
            ["uptime_s"] = Math.Round(stats.UptimeSeconds, 1),
            ["viewers"] = session.Viewers,
            ["pipeline_error"] = session.Pipeline.Failure?.Message,
        };
    }

    private static Dictionary<string, object?> Decoder(Television television)
    {
        PalindromeProcess? active = television.Active;
        return new Dictionary<string, object?>
        {
            ["message"] = television.Message,
            ["warming"] = television.Warming,
            ["command"] = active is null ? "" : "palindrome " + active.CommandLine,
            ["pid"] = active?.Pid,
            ["generation"] = active?.Generation,
            ["width"] = active?.Width,
            ["height"] = active?.Height,
            ["channels"] = active?.Channels,
            ["stride"] = active?.Stride,
        };
    }
}
