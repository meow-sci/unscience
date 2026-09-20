using System;
using System.Linq;
using RabbitEars.Live;
using RabbitEars.Tv;

namespace RabbitEars.Commands;

/// <summary>play: a recorded wideband file, looped and paced to real time, into the same tuner / decoder / web page as `live`.</summary>
public static class PlayCommand
{
    public const string Usage = "play <wideband file> [--port 8090] [--palindrome path] [--no-browser]      (K, format and channels come from <file>.json)";

    public static int Run(string[] argv)
    {
        string? path = argv.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        if (path is null || argv[0] != path) throw new ArgumentException("play: the wideband file comes first");
        var args = new Args(argv.Skip(1).ToArray(), new[] { "port", "palindrome" }, new[] { "no-browser" });
        string binary = PalindromeLocator.Find(args.Get("palindrome"));

        var producer = new FileProducer(path);
        Console.WriteLine($"rabbit-ears: playing {path}: K={producer.Plan.K}, {producer.DurationSeconds:0.##} s looped, {producer.Channels.Count} channel(s)");
        foreach (LiveChannel channel in producer.Channels)
            Console.WriteLine($"CH{channel.Slot + 1} (slot {channel.Slot}): {channel.CarrierHz / 1e6:0.00} MHz  {channel.LevelDb,5:0.0} dB  {channel.Source}");
        var session = new LiveSession("play", producer, binary);
        return LiveHost.Run(session, args.Int("port", 8090), openBrowser: !args.Has("no-browser"));
    }
}
