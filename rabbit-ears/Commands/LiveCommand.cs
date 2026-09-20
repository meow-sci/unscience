using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using RabbitEars.Live;
using RabbitEars.Net;
using RabbitEars.Pal;
using RabbitEars.Rf;
using RabbitEars.Sources;
using RabbitEars.Tv;

namespace RabbitEars.Commands;

/// <summary>live: in-process channels + network senders → real-time mux → tuner → PALindrome → web page.</summary>
public static class LiveCommand
{
    public const string Usage =
        "live [--channel <source> ...] [--k 6] [--levels dB,dB,..] [--listen [addr:]9000|off] [--port 8090] [--palindrome path]\n" +
        "      [--threads n] [--no-browser]      (no --channel: one test card per slot, up to six)";

    private static readonly string[] DefaultCardNames = { "BARS ONE", "GRID TWO", "HUE THREE", "CHECK FOUR", "STEPS FIVE", "RAYS SIX" };

    public static int Run(string[] argv)
    {
        var args = new Args(argv, new[] { "channel", "k", "levels", "listen", "port", "palindrome", "threads" }, new[] { "no-browser" });
        var plan = new ChannelPlan(args.Int("k", 6));
        string binary = PalindromeLocator.Find(args.Get("palindrome"));
        IReadOnlyList<string> specs = args.All("channel");
        if (specs.Count == 0) specs = DefaultChannels(plan);
        if (specs.Count > plan.MaxChannels) throw new ArgumentException($"K={plan.K} holds at most {plan.MaxChannels} channels");
        double[] levels = args.DoubleList("levels");

        var mux = new MuxProducer(plan, args.Int("threads", 0));
        SenderServer? senders = null;
        try
        {
            TimeSpan startOfDay = DateTime.Now.TimeOfDay;
            for (int slot = 0; slot < specs.Count; slot++)
            {
                double carrier = plan.CarrierHz(slot), level = slot < levels.Length ? levels[slot] : 0.0;
                IFrameSource source = SourceFactory.Create(specs[slot], slot + 1, $"{carrier / 1e6:0.00} MHZ", paced: true, startOfDay);
                mux.AddLocal(slot, specs[slot], source, level);
                Console.WriteLine($"CH{slot + 1} (slot {slot}): {carrier / 1e6:0.00} MHz  {level,5:0.0} dB  {specs[slot]}");
            }
            senders = StartSenderServer(args.Get("listen") ?? "9000", plan, mux);
        }
        catch
        {
            senders?.Dispose();
            mux.Dispose();
            throw;
        }
        Console.WriteLine($"rabbit-ears: K={plan.K}, wideband {plan.WidebandRate / 1e6:0} MS/s, decoder {binary}");
        var session = new LiveSession("live", mux, binary, senders);
        return LiveHost.Run(session, args.Int("port", 8090), openBrowser: !args.Has("no-browser"));
    }

    private static string[] DefaultChannels(ChannelPlan plan) =>
        Enumerable.Range(0, Math.Min(TestCards.Styles.Length, plan.MaxChannels))
            .Select(i => $"card:{TestCards.Styles[i]}:{DefaultCardNames[i % DefaultCardNames.Length]}").ToArray();

    private static SenderServer? StartSenderServer(string listen, ChannelPlan plan, MuxProducer mux)
    {
        if (listen is "off" or "none") return null;
        int colon = listen.LastIndexOf(':');
        IPAddress address = colon < 0 ? IPAddress.Loopback : IPAddress.Parse(listen.Substring(0, colon));
        if (!int.TryParse(listen.Substring(colon + 1), out int port)) throw new ArgumentException($"--listen: '{listen}' is not [addr:]port");
        var server = new SenderServer(address, port, plan, mux.IsSlotTaken, slot => mux.AddSender(slot));
        Console.WriteLine($"rabbit-ears: senders connect to {address}:{server.Port}   (RabbitEars send --source <spec> --to {address}:{server.Port})");
        return server;
    }
}
