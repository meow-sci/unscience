using System;
using RabbitEars.Dsp;
using RabbitEars.Rf;

namespace RabbitEars.Commands;

/// <summary>plan: prints the band plan and the designed filters' key responses.</summary>
public static class PlanCommand
{
    public const string Usage = "plan [--k 2|4|6|8]";

    public static int Run(string[] argv)
    {
        var args = new Args(argv, new[] { "k" }, Array.Empty<string>());
        var plan = new ChannelPlan(args.Int("k", 6));
        Console.WriteLine($"K = {plan.K}: wideband {plan.WidebandRate / 1e6:0} MS/s (Nyquist {plan.WidebandRate / 2e6:0} MHz), " +
                          $"{plan.MaxChannels} channel(s), tuner decimation {plan.Decimation} -> {ChannelPlan.IfRate / 1e6:0} MS/s IF, carrier at {ChannelPlan.IfCarrierHz / 1e6:0} MHz");
        Console.WriteLine("slot  vision carrier   slot edges (MHz)");
        for (int slot = 0; slot < plan.MaxChannels; slot++)
        {
            (double lo, double hi) = plan.SlotEdgesHz(slot);
            Console.WriteLine($"{slot,4}  {plan.CarrierHz(slot) / 1e6,10:0.00} MHz   {lo / 1e6,6:0.00} .. {hi / 1e6,6:0.00}");
        }

        (double[] re, double[] im) = VsbModulator.DesignTaps();
        Console.WriteLine($"\nVSB filter ({re.Length} complex taps at 20 MS/s), dB relative to the carrier:");
        foreach (double mhz in new[] { -3.0, -2.2, -1.75, -1.25, -1.0, -0.75, -0.5, 0.0, 1.0, 4.43, 5.5, 6.0, 6.75, 8.0 })
            Console.WriteLine($"  {mhz,6:0.00} MHz  {FirDesign.ResponseDb(re, im, mhz * 1e6, ChannelPlan.ChannelRate),8:0.0}");

        double[] interp = FirDesign.LowPass(plan.K * SlotPlacer.TapsPerPhase, SlotPlacer.PassHz, ChannelPlan.ChannelRate - SlotPlacer.PassHz, plan.WidebandRate);
        Console.WriteLine($"\nInterpolator prototype ({interp.Length} taps at {plan.WidebandRate / 1e6:0} MS/s, {SlotPlacer.TapsPerPhase} per phase), offsets from the slot centre:");
        foreach (double mhz in new[] { 0.0, 4.0, 4.3, 10.0, 15.7, 16.0, 20.0, 24.0 })
            Console.WriteLine($"  {mhz,6:0.00} MHz  {FirDesign.ResponseDb(interp, null, mhz * 1e6, plan.WidebandRate),8:0.0}");

        var tuner = new Tuner(plan);
        double[] tune = FirDesign.LowPass(tuner.Taps, Tuner.PassHz, Tuner.StopHz, plan.WidebandRate);
        Console.WriteLine($"\nTuner filter ({tuner.Taps} taps at {plan.WidebandRate / 1e6:0} MS/s), offsets from the slot centre:");
        foreach (double mhz in new[] { 0.0, 4.0, 4.5, 8.0, 12.0, 14.5, 20.0, 29.25 })
            Console.WriteLine($"  {mhz,6:0.00} MHz  {FirDesign.ResponseDb(tune, null, mhz * 1e6, plan.WidebandRate),8:0.0}");
        return 0;
    }
}
