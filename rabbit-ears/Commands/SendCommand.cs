using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using RabbitEars.Net;
using RabbitEars.Pal;
using RabbitEars.Sources;

namespace RabbitEars.Commands;

/// <summary>send: encode one source to PAL here and stream the CVBS to a `live` server, paced to real time.</summary>
public static class SendCommand
{
    public const string Usage =
        "send --source <source> --to host:port [--slot n] [--format u8|s16] [--name text] [--lead-ms 150]\n" +
        "      (slot is 0-based, default any free one; the sender stays --lead-ms ahead of real time, at most 250)";

    private const int BlockSamples = 125 * PalTiming.SamplesPerLine;      // 8 ms

    public static int Run(string[] argv)
    {
        var args = new Args(argv, new[] { "source", "to", "slot", "format", "name", "lead-ms" }, Array.Empty<string>());
        string spec = args.Require("source"), to = args.Require("to");
        int colon = to.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(to.Substring(colon + 1), out int port)) throw new ArgumentException($"--to: '{to}' is not host:port");
        string host = to.Substring(0, colon);
        CvbsFormat format = CvbsStreamCodec.ParseFormat(args.Get("format") ?? "u8");
        int slot = args.Int("slot", SenderProtocol.AnySlot);
        double lead = Math.Clamp(args.Double("lead-ms", 150), 20, 250) / 1000.0;

        using var stop = new CancellationTokenSource();
        using ShutdownSignals signals = ShutdownSignals.Hook(stop.Cancel);
        string name = args.Get("name") ?? SourceFactory.DisplayName(spec);
        try
        {
            using var client = new SenderClient(host, port, new SenderHello(format, PalTiming.SampleRate, slot, name));
            using IFrameSource source = SourceFactory.Create(spec, client.AssignedSlot + 1, "NETWORK SENDER", paced: true);
            Console.WriteLine($"rabbit-ears: sending '{name}' ({spec}) to {to} as {format.ToString().ToLowerInvariant()}: slot {client.AssignedSlot} (CH{client.AssignedSlot + 1})");
            Stream(client, new PalEncoder(source), lead, stop.Token);
        }
        catch (Exception e) when (e is IOException or SocketException)
        {
            Console.WriteLine($"rabbit-ears: send stopped: {e.Message}");
            return 1;
        }
        return 0;
    }

    private static void Stream(SenderClient client, PalEncoder encoder, double lead, CancellationToken stop)
    {
        var block = new float[BlockSamples];
        var clock = Stopwatch.StartNew();
        double busy = 0, nextReport = 5;
        while (!stop.IsCancellationRequested)
        {
            double before = clock.Elapsed.TotalSeconds;
            encoder.Read(block);
            busy += clock.Elapsed.TotalSeconds - before;
            client.Send(block);

            double sent = client.SamplesSent / (double)PalTiming.SampleRate, elapsed = clock.Elapsed.TotalSeconds;
            double ahead = sent - elapsed;
            if (ahead > lead) Thread.Sleep(TimeSpan.FromSeconds(ahead - lead));
            if (elapsed < nextReport) continue;
            nextReport += 5;
            Console.WriteLine($"rabbit-ears: sent {sent:0.0} s, {ahead * 1000:0} ms ahead of real time, encoder {sent / Math.Max(busy, 1e-9):0.0}x real time");
        }
    }
}
