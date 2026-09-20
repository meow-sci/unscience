using System;

namespace RabbitEars.Rf;

/// <summary>
/// The parametric band plan: channels at Fc = 20 MS/s, wideband at Fw = K·Fc, System I raster
/// (vision carriers at 6 MHz + 8 MHz·slot, each slot spanning carrier −1.25 … +6.75 MHz), and a tuner
/// output of 40 MS/s with the tuned vision carrier at 8 MHz.
/// </summary>
public sealed class ChannelPlan
{
    public const double ChannelRate = 20e6;
    public const double IfRate = 40e6;
    public const double IfCarrierHz = 8e6;
    public const double FirstCarrierHz = 6e6;
    public const double SpacingHz = 8e6;
    public const double LowerEdgeHz = -1.25e6;                            // slot edges relative to the vision carrier
    public const double UpperEdgeHz = 6.75e6;
    public const double SlotCentreOffsetHz = (LowerEdgeHz + UpperEdgeHz) / 2;   // +2.75 MHz

    public ChannelPlan(int k)
    {
        if (k != 2 && k != 4 && k != 6 && k != 8) throw new ArgumentOutOfRangeException(nameof(k), "K must be 2, 4, 6 or 8");
        K = k;
    }

    /// <summary>Interpolation factor: wideband rate / channel rate.</summary>
    public int K { get; }

    public double WidebandRate => K * ChannelRate;

    /// <summary>Tuner decimation: wideband rate / 40 MS/s.</summary>
    public int Decimation => K / 2;

    /// <summary>How many whole slots fit below Nyquist.</summary>
    public int MaxChannels => (int)Math.Floor((WidebandRate / 2 - (FirstCarrierHz + UpperEdgeHz)) / SpacingHz) + 1;

    public double CarrierHz(int slot)
    {
        if (slot < 0 || slot >= MaxChannels) throw new ArgumentOutOfRangeException(nameof(slot), $"slot must be 0..{MaxChannels - 1} for K={K}");
        return FirstCarrierHz + SpacingHz * slot;
    }

    public double SlotCentreHz(int slot) => CarrierHz(slot) + SlotCentreOffsetHz;

    public (double Low, double High) SlotEdgesHz(int slot) => (CarrierHz(slot) + LowerEdgeHz, CarrierHz(slot) + UpperEdgeHz);

    /// <summary>Nearest slot to a frequency, or −1 when it is more than half a spacing from any carrier.</summary>
    public int NearestSlot(double carrierHz)
    {
        int slot = (int)Math.Round((carrierHz - FirstCarrierHz) / SpacingHz);
        return slot >= 0 && slot < MaxChannels ? slot : -1;
    }
}
