using System;
using System.Collections.Generic;
using RabbitEars.Dsp;
using RabbitEars.Rf;

namespace RabbitEars.Tests;

/// <summary>Filter design targets and the spectrum of a two-channel mux, measured with the project's own FFT.</summary>
public static class SpectrumChecks
{
    private const int FftSize = 1 << 14;

    public static void Run()
    {
        Check.Section("FFT");
        var tone = new float[FftSize * 4];
        for (int i = 0; i < tone.Length; i++) tone[i] = 0.5f * (float)Math.Cos(2 * Math.PI * 1000.0 / FftSize * i);
        double[] toneDb = Fft.PowerSpectrumDb(tone, FftSize);
        Check.Near(toneDb[1000], 20 * Math.Log10(0.5), 0.05, "a 0.5-amplitude tone reads -6.02 dB at its bin");
        Check.AtMost(toneDb[1100], -120, "and nothing 100 bins away (dB)");

        Check.Section("VSB filter design");
        (double[] re, double[] im) = VsbModulator.DesignTaps();
        double ripple = 0;
        for (double f = -0.75e6; f <= 5.5e6; f += 0.05e6)
            ripple = Math.Max(ripple, Math.Abs(FirDesign.ResponseDb(re, im, f, ChannelPlan.ChannelRate)));
        Check.AtMost(ripple, 0.1, "pass-band ripple -0.75 .. +5.5 MHz (dB)");
        Check.AtMost(FirDesign.ResponseDb(re, im, -1.25e6, ChannelPlan.ChannelRate), -20, "response at -1.25 MHz (dB)");
        Check.AtMost(FirDesign.ResponseDb(re, im, -2.2e6, ChannelPlan.ChannelRate), -50, "response at -2.2 MHz (dB)");
        Check.AtMost(FirDesign.ResponseDb(re, im, 6.75e6, ChannelPlan.ChannelRate), -50, "response at +6.75 MHz (dB)");

        foreach (int k in new[] { 4, 6, 8 }) TwoChannelSpectrum(k);
    }

    private static void TwoChannelSpectrum(int k)
    {
        Check.Section($"two-channel mux spectrum, K={k}");
        var plan = new ChannelPlan(k);
        int slotA = 0, slotB = 2;                                          // slot 1 stays empty: leakage shows there
        var mux = new WidebandMux(plan, 2);
        mux.AddChannel(slotA, new NoiseCvbsSource(1), "a");
        mux.AddChannel(slotB, new NoiseCvbsSource(2), "b");
        int low = FftSize * 80 / k;
        var wide = new float[low * k];
        mux.Process(wide, low);
        double[] db = Fft.PowerSpectrumDb(wide.AsSpan(FftSize), FftSize);  // skip the filters' start-up
        double binHz = plan.WidebandRate / FftSize;

        // Interpolation leaves faint images of each carrier at f_k ± 20 MHz·i (folded at Nyquist). They are discrete
        // lines, judged against the carrier; everything else is noise-like and judged against the sideband level.
        var images = new HashSet<int>();
        double worstImage = double.MinValue;
        double carrierDb = 20 * Math.Log10(0.48 / 2);                      // mean envelope 0.76 − 0.8·0.35, reference amplitude 1/2
        foreach (int slot in new[] { slotA, slotB })
            for (int i = 1; i < k; i++)
            {
                double f = (plan.CarrierHz(slot) + i * ChannelPlan.ChannelRate) % plan.WidebandRate;
                if (f > plan.WidebandRate / 2) f = plan.WidebandRate - f;
                if (InsideSlot(plan, slotA, f) || InsideSlot(plan, slotB, f)) continue;   // hidden under real sidebands
                int bin = (int)Math.Round(f / binHz);
                for (int b = Math.Max(0, bin - 4); b <= Math.Min(db.Length - 1, bin + 4); b++)
                {
                    images.Add(b);
                    worstImage = Math.Max(worstImage, db[b]);
                }
            }
        Check.AtMost(worstImage - carrierDb, -70, "strongest interpolation image of a carrier (dBc)");

        foreach (int slot in new[] { slotA, slotB })
        {
            double carrier = plan.CarrierHz(slot);
            int peak = PeakBin(db, (carrier - 0.5e6) / binHz, (carrier + 0.5e6) / binHz);
            Check.Near(peak * binHz, carrier, binHz, $"slot {slot} carrier frequency (Hz)");
            Check.Near(LinePower(db, peak), carrierDb, 0.3, $"slot {slot} carrier level (dB)");

            double inBand = BandMean(db, carrier + 1.0e6, carrier + 5.0e6, binHz);
            double mirror = BandMean(db, carrier + 1.25e6, carrier + 1.35e6, binHz);
            double vestige = BandMean(db, carrier - 1.35e6, carrier - 1.25e6, binHz);
            Check.AtMost(vestige - mirror, -20, $"slot {slot} lower vestige at -1.25 MHz vs the same offset above (dB)");
            Check.Near(BandMean(db, carrier - 0.7e6, carrier - 0.3e6, binHz) - BandMean(db, carrier + 0.3e6, carrier + 0.7e6, binHz), 0, 0.5,
                $"slot {slot} is double-sideband inside ±0.75 MHz (dB difference)");
            Check.AtMost(BandMax(db, carrier + 6.75e6, carrier + 7.9e6, binHz, images) - inBand, -40, $"slot {slot} leakage above +6.75 MHz (dB)");
            Check.AtMost(BandMax(db, carrier - 3.0e6, carrier - 2.2e6, binHz, images) - inBand, -40, $"slot {slot} leakage below -2.2 MHz (dB)");
        }

        double reference = BandMean(db, plan.CarrierHz(slotA) + 1.0e6, plan.CarrierHz(slotA) + 5.0e6, binHz);
        (double emptyLow, double emptyHigh) = plan.SlotEdgesHz(1);
        Check.AtMost(BandMax(db, emptyLow, emptyHigh - 1.0e6, binHz, images) - reference, -40, "leakage into the empty slot between them (dB)");
        double top = plan.CarrierHz(slotB) + ChannelPlan.UpperEdgeHz;
        if (plan.WidebandRate / 2 - top > 1e6)
            Check.AtMost(BandMax(db, top, plan.WidebandRate / 2 - 0.2e6, binHz, images) - reference, -40, "leakage above the highest slot, up to Nyquist (dB)");
        Check.AtMost(BandMax(db, 0.2e6, plan.CarrierHz(slotA) - 2.2e6, binHz, images) - reference, -40, "leakage below the lowest slot (dB)");
    }

    private static bool InsideSlot(ChannelPlan plan, int slot, double hz)
    {
        (double low, double high) = plan.SlotEdgesHz(slot);
        return hz >= low - 1e6 && hz <= high;
    }

    private static int PeakBin(double[] db, double from, double to)
    {
        int best = (int)from;
        for (int b = (int)from; b <= (int)to; b++)
            if (db[b] > db[best]) best = b;
        return best;
    }

    /// <summary>Level of a spectral line wherever it falls between bins: main-lobe power over the window's noise bandwidth.</summary>
    private static double LinePower(double[] db, int peak)
    {
        const double blackmanHarrisEnbw = 2.0044;
        double sum = 0;
        for (int b = peak - 4; b <= peak + 4; b++) sum += Math.Pow(10, db[b] / 10);
        return 10 * Math.Log10(sum / blackmanHarrisEnbw);
    }

    private static double BandMean(double[] db, double fromHz, double toHz, double binHz)
    {
        double sum = 0;
        int a = (int)Math.Ceiling(fromHz / binHz), b = (int)Math.Floor(toHz / binHz);
        for (int i = a; i <= b; i++) sum += Math.Pow(10, db[i] / 10);
        return 10 * Math.Log10(sum / (b - a + 1));
    }

    private static double BandMax(double[] db, double fromHz, double toHz, double binHz, HashSet<int> skip)
    {
        double max = double.MinValue;
        for (int i = (int)Math.Ceiling(fromHz / binHz); i <= (int)Math.Floor(toHz / binHz); i++)
            if (!skip.Contains(i)) max = Math.Max(max, db[i]);
        return max;
    }
}
