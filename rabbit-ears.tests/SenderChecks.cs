using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using RabbitEars.Net;
using RabbitEars.Pal;
using RabbitEars.Rf;

namespace RabbitEars.Tests;

/// <summary>
/// Sender server with a real TCP loopback sender: slot admission, FIFO delivery, and the "no programme" fallback.
/// A grey picture is streamed, starved and resumed; every transmitted sample must equal either the unbroken grey
/// stream or the unbroken black stream at that position, so sync, levels and burst phase never jump.
/// </summary>
public static class SenderChecks
{
    private const int Block = 125 * PalTiming.SamplesPerLine;
    // One sending burst: 7 frames + 100 lines + 720 samples ≈ 0.29 s, so it ends inside the active picture, where
    // the grey programme and the black fallback differ and the switch can be located exactly.
    private const int Burst = 7 * PalTiming.SamplesPerFrame + 100 * PalTiming.SamplesPerLine + 720;
    private const int FirstRead = 50 * Block, SecondRead = 38 * Block;   // 8 M and 6.08 M samples

    public static void Run()
    {
        Check.Section("sender server: admission, FIFO, no-programme fallback (TCP loopback)");
        var created = new List<SenderSlot>();
        using var server = new SenderServer(IPAddress.Loopback, 0, new ChannelPlan(6), slot => slot == 0, created.Add);

        Check.That(Refused(server, 0), "a slot held by an in-process channel is refused");
        Check.That(Refused(server, 9), "a slot outside the band plan is refused");

        using var client = new SenderClient("127.0.0.1", server.Port, new SenderHello(CvbsFormat.U8, PalTiming.SampleRate, SenderProtocol.AnySlot, "loopback"));
        Check.That(client.AssignedSlot == 1 && created.Count == 1 && created[0].Slot == 1, $"'any' gets the first free slot (1): got {client.AssignedSlot}");
        Check.That(Refused(server, 1), "a slot with a connected sender is refused");
        SenderSlot slot = created[0];
        Check.That(slot.Connected && slot.Name == "loopback" && slot.Format == CvbsFormat.U8, "slot reports its sender");

        var sender = new PalEncoder(new SolidSource(140, 140, 140));
        var grey = new PalEncoder(new SolidSource(140, 140, 140));
        var black = new PalEncoder(new SolidSource(0, 0, 0));
        var tally = new Tally();

        Send(client, sender, Burst);
        Check.That(WaitFor(() => slot.SamplesReceived == Burst), "first burst arrived in the FIFO");
        Transmit(slot, grey, black, FirstRead, tally);                     // the programme, then the FIFO runs dry
        Check.That(tally.Programme == Burst && tally.NoProgramme == FirstRead - Burst && tally.Foreign == 0,
            $"programme until the FIFO is empty, then black + sync: {tally}");
        Check.That(slot.Underruns == 1 && !slot.OnProgramme, "one underrun counted");

        Send(client, sender, Burst);                                       // the stream resumes at index Burst while the slot is at 8 M
        Check.That(WaitFor(() => slot.SamplesReceived == 2 * Burst), "second burst arrived");
        tally = new Tally();
        Transmit(slot, grey, black, SecondRead, tally);
        long holdOff = (((long)Burst - FirstRead) % SenderSlot.SequenceSamples + SenderSlot.SequenceSamples) % SenderSlot.SequenceSamples;
        Check.That(tally.NoProgramme == holdOff && tally.Programme == SecondRead - holdOff && tally.Foreign == 0,
            $"programme resumes on the same point of the 8-field sequence (after {holdOff} samples): {tally}");
        Check.That(tally.Switches == 1, "exactly one switch back, none during the programme");
        Check.AtMost(tally.WorstError, 0.5001 / 182 + 1.0 / 24000, "levels: every sample within u8 quantisation of the unbroken reference streams (V)");

        client.Dispose();
        Check.That(WaitFor(() => !slot.Connected), "disconnect is noticed");
        using var second = new SenderClient("127.0.0.1", server.Port, new SenderHello(CvbsFormat.S16, PalTiming.SampleRate, 1, "second"));
        Check.That(second.AssignedSlot == 1 && created.Count == 1 && slot.Name == "second", "the freed slot can be claimed again (same FIFO, no new mux channel)");
    }

    private sealed class Tally
    {
        public long Programme, NoProgramme, Foreign, Switches, Undecided;
        public double WorstError;
        public bool? LastWasProgramme;

        public override string ToString() => $"programme {Programme}, no-programme {NoProgramme}, neither {Foreign}, switches {Switches}";
    }

    /// <summary>Reads what the mux would transmit and classifies each line-sized run against both reference streams.</summary>
    private static void Transmit(SenderSlot slot, PalEncoder grey, PalEncoder black, int samples, Tally tally)
    {
        var got = new float[Block];
        var asGrey = new float[Block];
        var asBlack = new float[Block];
        const int run = 80;                                               // short runs: a switch is located to within 80 samples
        for (int done = 0; done < samples; done += Block)
        {
            slot.Read(got);
            grey.Read(asGrey);
            black.Read(asBlack);
            for (int start = 0; start < Block; start += run)
            {
                double errorGrey = 0, errorBlack = 0, difference = 0;
                for (int i = start; i < start + run; i++)
                {
                    errorGrey = Math.Max(errorGrey, Math.Abs(got[i] - asGrey[i]));
                    errorBlack = Math.Max(errorBlack, Math.Abs(got[i] - asBlack[i]));
                    difference = Math.Max(difference, Math.Abs(asGrey[i] - asBlack[i]));
                }
                const double tolerance = 0.5001 / 182 + 1.0 / 24000;
                bool isGrey = errorGrey <= tolerance, isBlack = errorBlack <= tolerance;
                if (!isGrey && !isBlack)
                {
                    tally.Foreign += run;
                    continue;
                }
                tally.WorstError = Math.Max(tally.WorstError, Math.Min(errorGrey, errorBlack));
                if (difference < 0.05)                                     // sync / blanking: both streams agree here
                {
                    if (tally.LastWasProgramme is null) tally.Undecided += run;
                    else if (tally.LastWasProgramme == true) tally.Programme += run;
                    else tally.NoProgramme += run;
                    continue;
                }
                if (tally.LastWasProgramme is { } last && last != isGrey) tally.Switches++;
                if (tally.LastWasProgramme is null)                        // the leading blanking belongs to whatever comes first
                {
                    if (isGrey) tally.Programme += tally.Undecided;
                    else tally.NoProgramme += tally.Undecided;
                }
                tally.LastWasProgramme = isGrey;
                if (isGrey) tally.Programme += run;
                else tally.NoProgramme += run;
            }
        }
    }

    private static void Send(SenderClient client, PalEncoder encoder, int samples)
    {
        var block = new float[Block];
        for (int done = 0; done < samples; done += Block)
        {
            Span<float> part = block.AsSpan(0, Math.Min(Block, samples - done));
            encoder.Read(part);
            client.Send(part);
        }
    }

    private static bool Refused(SenderServer server, int slot)
    {
        try
        {
            using var client = new SenderClient("127.0.0.1", server.Port, new SenderHello(CvbsFormat.U8, PalTiming.SampleRate, slot, "refused"));
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    public static bool WaitFor(Func<bool> condition, int milliseconds = 5000)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            if (condition()) return true;
            Thread.Sleep(5);
        }
        return condition();
    }
}
