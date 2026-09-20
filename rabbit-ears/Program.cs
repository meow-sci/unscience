using System;
using System.Linq;
using RabbitEars.Commands;

namespace RabbitEars;

public static class Program
{
    public static int Main(string[] argv)
    {
        if (argv.Length == 0 || argv[0] is "help" or "--help" or "-h")
        {
            PrintUsage();
            return argv.Length == 0 ? 2 : 0;
        }
        try
        {
            string[] rest = argv.Skip(1).ToArray();
            switch (argv[0])
            {
                case "plan": return PlanCommand.Run(rest);
                case "mux": return MuxCommand.Run(rest);
                case "tune": return TuneCommand.Run(rest);
                case "bench": return BenchCommand.Run(rest);
                case "live": return LiveCommand.Run(rest);
                case "play": return PlayCommand.Run(rest);
                case "send": return SendCommand.Run(rest);
                default:
                    Console.WriteLine($"rabbit-ears: unknown command '{argv[0]}'");
                    PrintUsage();
                    return 2;
            }
        }
        catch (Exception e) when (e is ArgumentException or System.IO.IOException or InvalidOperationException or System.Net.Sockets.SocketException or System.Net.HttpListenerException)
        {
            Console.WriteLine($"rabbit-ears: {e.Message}");
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("rabbit-ears: a multi-channel PAL broadcast band in pure maths, and a tuner for it\n");
        Console.WriteLine("  " + PlanCommand.Usage);
        Console.WriteLine("  " + MuxCommand.Usage);
        Console.WriteLine("  " + TuneCommand.Usage);
        Console.WriteLine("  " + BenchCommand.Usage);
        Console.WriteLine("  " + LiveCommand.Usage);
        Console.WriteLine("  " + PlayCommand.Usage);
        Console.WriteLine("  " + SendCommand.Usage);
    }
}
