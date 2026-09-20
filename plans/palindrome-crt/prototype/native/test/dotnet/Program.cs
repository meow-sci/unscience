// P/Invoke smoke test: the way the KSA mod would consume the native library.
using System;
using System.Reflection;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
internal struct PalSmokeParams
{
    public uint StructSize, Width, Height, Fields, Colour, DepositLanes, BlockSamples, ThreadedPipeline;
    public double SampleRateHz;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PalSmokeResult
{
    public uint StructSize;
    public int HoldLocked;
    public ulong SamplesFed, AcceptedEdges, RejectedEdges, DetectedFields, FieldCallbacks;
    public double LineOmega, FieldOmega, AgcGain, SubcarrierHz, BurstAmplitude, KillerGain, ElapsedMs;
    public uint FrameWidth, FrameHeight, FrameChannels, Reserved;
    public ulong FrameBytes, FrameSum, FrameFnv1a64;
}

internal static unsafe class Native
{
    private const string Lib = "palindrome";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint pal_abi_version();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pal_build_info();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr pal_last_error();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int pal_cpu_supported();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int pal_smoke_exception();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pal_smoke_decode(in PalSmokeParams p, ref PalSmokeResult r, byte* frameOut, nuint frameOutCap);
}

internal static class Program
{
    private static unsafe int Main(string[] args)
    {
        var libPath = args.Length > 0 ? args[0] : throw new ArgumentException("usage: PalSmoke <native lib path>");
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(),
            (name, _, _) => name == "palindrome" ? NativeLibrary.Load(libPath) : IntPtr.Zero);

        Console.WriteLine($"abi={Native.pal_abi_version()} cpu_supported={Native.pal_cpu_supported()}");
        Console.WriteLine($"build={Marshal.PtrToStringUTF8(Native.pal_build_info())}");
        var rc = Native.pal_smoke_exception();
        Console.WriteLine($"exception_rc={rc} msg=\"{Marshal.PtrToStringUTF8(Native.pal_last_error())}\"");

        var p = new PalSmokeParams
        {
            StructSize = (uint)sizeof(PalSmokeParams), Width = 720, Height = 576, Fields = 10, Colour = 1,
            DepositLanes = 4, BlockSamples = 1u << 16, ThreadedPipeline = 1, SampleRateHz = 16e6,
        };
        var r = new PalSmokeResult { StructSize = (uint)sizeof(PalSmokeResult) };
        var frame = new byte[720 * 576 * 3];
        fixed (byte* f = frame)
            rc = Native.pal_smoke_decode(in p, ref r, f, (nuint)frame.Length);
        if (rc != 0)
        {
            Console.WriteLine($"decode_rc={rc} err=\"{Marshal.PtrToStringUTF8(Native.pal_last_error())}\"");
            return 1;
        }
        ulong sum = 0;
        foreach (var b in frame) sum += b;
        Console.WriteLine($"decode_rc=0 sizeof(params)={sizeof(PalSmokeParams)} sizeof(result)={sizeof(PalSmokeResult)} " +
                          $"locked={r.HoldLocked} fields={r.DetectedFields} edges={r.AcceptedEdges}/{r.RejectedEdges} " +
                          $"frame={r.FrameWidth}x{r.FrameHeight}x{r.FrameChannels} sum={r.FrameSum} managed_sum={sum} " +
                          $"fnv1a64=0x{r.FrameFnv1a64:x16} elapsed_ms={r.ElapsedMs:F1}");
        return sum == r.FrameSum ? 0 : 2;
    }
}
