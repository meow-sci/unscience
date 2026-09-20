using System;
using System.Runtime.Intrinsics;
using RabbitEars.Dsp;
using RabbitEars.Sources;

namespace RabbitEars.Pal;

/// <summary>A continuous stream of composite video in volts at 20 MS/s (tip −0.3, blanking 0, white +0.7).</summary>
public interface ICvbsSource
{
    /// <summary>Fills the whole span with the next samples. Called from one thread at a time.</summary>
    void Read(Span<float> cvbs);
}

/// <summary>
/// Streaming PAL-I encoder: RGB frames in, CVBS out, generated one line at a time so the output is identical
/// for any block size and the subcarrier, V-switch and sync run unbroken from sample 0 forever.
/// Not thread-safe; <see cref="Params"/> may be swapped from another thread (read once per line).
/// </summary>
public sealed unsafe class PalEncoder : ICvbsSource
{
    private const int Spl = PalTiming.SamplesPerLine;
    private static readonly float[] CosTable = BuildCarrier(cosine: true);
    private static readonly float[] SinTable = BuildCarrier(cosine: false);

    private readonly IFrameSource _source;
    private readonly PalLinePlan _plan = PalLinePlan.Instance;
    private readonly FrameSampler _sampler = new();
    private readonly PalImpairments _impairments;
    private readonly float[] _line = new float[Spl];
    private readonly float[] _y = new float[PalTiming.ActiveSamples];
    private readonly float[] _u = new float[PalTiming.ActiveSamples];
    private readonly float[] _v = new float[PalTiming.ActiveSamples];
    private VideoFrame _frame;
    private long _globalLine;
    private int _lineCursor = Spl;                                        // samples of _line already handed out
    private volatile PalSignalParams _params = PalSignalParams.Default;

    public PalEncoder(IFrameSource source, ulong noiseSeed = 1)
    {
        _source = source;
        _impairments = new PalImpairments(noiseSeed);
    }

    public PalSignalParams Params
    {
        get => _params;
        set => _params = value ?? PalSignalParams.Default;
    }

    /// <summary>Samples produced so far.</summary>
    public long SamplePosition => (_globalLine - 1) * Spl + _lineCursor;

    public long FrameIndex => Math.Max(0, _globalLine - 1) / PalTiming.Lines;

    private static float[] BuildCarrier(bool cosine)
    {
        var t = new float[Spl];
        double step = 2.0 * Math.PI * PalTiming.SubcarrierHz / PalTiming.SampleRate;
        for (int i = 0; i < Spl; i++) t[i] = (float)(cosine ? Math.Cos(step * i) : Math.Sin(step * i));
        return t;
    }

    /// <summary>
    /// Repositions the stream: the next sample read is sample <paramref name="position"/> of the unbroken stream
    /// that started at 0, so sync, V switch and subcarrier phase are those of that position.
    /// </summary>
    public void Seek(long position)
    {
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
        _globalLine = position / Spl;
        int offset = (int)(position % Spl);
        _lineCursor = Spl;
        if (offset == 0) return;
        BuildLine();
        _lineCursor = offset;
    }

    public void Read(Span<float> cvbs)
    {
        int done = 0;
        while (done < cvbs.Length)
        {
            if (_lineCursor == Spl)
            {
                BuildLine();
                _lineCursor = 0;
            }
            int run = Math.Min(Spl - _lineCursor, cvbs.Length - done);
            _line.AsSpan(_lineCursor, run).CopyTo(cvbs.Slice(done));
            _lineCursor += run;
            done += run;
        }
    }

    private void BuildLine()
    {
        PalSignalParams p = _params;
        int frameLine = (int)(_globalLine % PalTiming.Lines);
        if (frameLine == 0 || _frame.Rgb is null) _frame = _source.GetFrame(_globalLine / PalTiming.Lines);

        _plan.SyncTemplates[_plan.TemplateOfLine[frameLine]].CopyTo(_line, 0);

        double phase = 2.0 * Math.PI * PalTiming.LineStartPhaseCycles(_globalLine);
        float s0 = (float)Math.Sin(phase), c0 = (float)Math.Cos(phase);
        float vSwitch = (_globalLine & 1) == 0 ? 1f : -1f;

        if (_plan.HasBurst[frameLine] && p.BurstGain > 0) AddBurst(s0, c0, vSwitch, p.BurstGain);

        int row = PalTiming.SourceRow(frameLine);
        if (row >= 0)
        {
            _sampler.SampleRow(_frame, row, p.ChromaGain, _y, _u, _v);
            int first = frameLine == PalTiming.Field1FirstLine ? PalTiming.ActiveSamples / 2 : 0;       // line 23
            int last = frameLine == PalTiming.Field2FirstLine + PalTiming.FieldActiveLines - 1            // line 623
                ? PalTiming.ActiveSamples / 2 : PalTiming.ActiveSamples;
            AddPicture(first, last, s0, c0, vSwitch, p);
        }

        _impairments.Apply(_line, _globalLine, p);
        _globalLine++;
    }

    /// <summary>Burst = (0.15/√2)·gate·(−sin ωt ± cos ωt): ±135° swinging with the V switch.</summary>
    private void AddBurst(float s0, float c0, float vSwitch, float gain)
    {
        float k = PalTiming.BurstVolts / MathF.Sqrt(2f) * gain;
        float onCos = k * (-s0 + vSwitch * c0), onSin = k * (-c0 - vSwitch * s0);
        float[] gate = _plan.BurstEnvelope;
        for (int s = _plan.BurstFirst; s < _plan.BurstLast; s++)
            _line[s] += gate[s] * (CosTable[s] * onCos + SinTable[s] * onSin);
    }

    /// <summary>Adds 0.7·(Y + U·sin(ωt + φ) ± V·cos(ωt + φ)) + pedestal over the active samples.</summary>
    private void AddPicture(int first, int last, float s0, float c0, float vSwitch, PalSignalParams p)
    {
        if (p.ChromaPhaseDegrees != 0)
        {
            double a = p.ChromaPhaseDegrees * Math.PI / 180.0;
            float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
            (s0, c0) = (s0 * ca + c0 * sa, c0 * ca - s0 * sa);
        }
        // U·sin(θ0 + ωs) ± V·cos(θ0 + ωs) = C[s]·(U·s0 ± V·c0) + S[s]·(U·c0 ∓ V·s0)
        float white = PalTiming.WhiteVolts;
        Vector128<float> vs0 = Vector128.Create(s0), vc0 = Vector128.Create(c0), vsw = Vector128.Create(vSwitch);
        Vector128<float> vWhite = Vector128.Create(white), vPed = Vector128.Create(p.Pedestal);
        fixed (float* line = _line, y = _y, u = _u, v = _v, cosT = CosTable, sinT = SinTable)
        {
            float* o = line + PalTiming.ActiveStart, ct = cosT + PalTiming.ActiveStart, st = sinT + PalTiming.ActiveStart;
            int i = first;
            for (; i + 4 <= last; i += 4)
            {
                Vector128<float> uu = Vector128.Load(u + i), vv = Vector128.Load(v + i) * vsw;
                Vector128<float> onCos = uu * vs0 + vv * vc0, onSin = uu * vc0 - vv * vs0;
                Vector128<float> chroma = Vector128.Load(ct + i) * onCos + Vector128.Load(st + i) * onSin;
                (Vector128.Load(o + i) + vPed + vWhite * (Vector128.Load(y + i) + chroma)).Store(o + i);
            }
            for (; i < last; i++)
            {
                float uu = u[i], vv = v[i] * vSwitch;
                float chroma = ct[i] * (uu * s0 + vv * c0) + st[i] * (uu * c0 - vv * s0);
                o[i] = o[i] + p.Pedestal + white * (y[i] + chroma);
            }
        }
    }
}
