using System;

namespace RabbitEars.Pal;

/// <summary>PAL System I (625/50) timing on the line-locked 20 MS/s grid: exactly 1280 samples per 64 µs line.</summary>
public static class PalTiming
{
    public const int SampleRate = 20_000_000;
    public const int SamplesPerLine = 1280;
    public const int Lines = 625;
    public const int SamplesPerFrame = SamplesPerLine * Lines;          // 800,000 = one 25 Hz frame
    public const double SubcarrierHz = 4_433_618.75;

    public const int ActiveStart = 208;                                  // 10.4 µs after the sync leading edge
    public const int ActiveSamples = 1040;                               // 52 µs
    public const int ActiveRows = 576;

    public const int SyncSamples = 94;                                   // 4.7 µs
    public const int EqualisingSamples = 47;                             // 2.35 µs
    public const int BroadSamples = SamplesPerLine / 2 - SyncSamples;    // half line less 4.7 µs
    public const int BurstStart = 112;                                   // 5.6 µs
    public const int BurstSamples = 45;                                  // 10 cycles

    public const float SyncVolts = -0.3f;
    public const float WhiteVolts = 0.7f;
    public const float BurstVolts = 0.15f;                               // burst amplitude (half of 0.3 V p-p)

    // Subcarrier cycles per line = 283.7516 = 283 + 1879/2500: the phase repeats every 2500 lines (4 frames).
    public const int PhaseNumerator = 1879;
    public const int PhaseDenominator = 2500;

    /// <summary>Subcarrier phase (cycles, 0..1) at the first sample of a line counted from the start of the stream.</summary>
    public static double LineStartPhaseCycles(long globalLine)
    {
        long r = (globalLine % PhaseDenominator) * PhaseNumerator % PhaseDenominator;
        return (double)r / PhaseDenominator;
    }

    /// <summary>First 0-based frame line and source-row parity for each field's active picture.</summary>
    public const int Field1FirstLine = 22;                               // line 23
    public const int Field2FirstLine = 335;                              // line 336
    public const int FieldActiveLines = 288;

    /// <summary>Source row (0..575) shown on a 0-based frame line, or −1 outside the active picture.</summary>
    public static int SourceRow(int frameLine)
    {
        if (frameLine >= Field1FirstLine && frameLine < Field1FirstLine + FieldActiveLines)
            return (frameLine - Field1FirstLine) * 2;
        if (frameLine >= Field2FirstLine && frameLine < Field2FirstLine + FieldActiveLines)
            return (frameLine - Field2FirstLine) * 2 + 1;
        return -1;
    }

    public static double Hz(double cyclesPerSample) => cyclesPerSample * SampleRate;
    public static int Microseconds(double us) => (int)Math.Round(us * 1e-6 * SampleRate);
}
