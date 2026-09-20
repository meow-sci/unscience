using System;
using System.IO;
using System.Linq;
using RabbitEars.Net;

namespace RabbitEars.Tests;

/// <summary>The sender contract on the wire: sample scaling exactly as designed, handshake and block framing.</summary>
public static class NetChecks
{
    public static void Run()
    {
        Codec();
        Handshake();
    }

    private static void Codec()
    {
        Check.Section("CVBS stream codec");
        float[] volts = { -0.3f, 0f, 0.7f, 0.35f, -0.42f, 0.98f, 2f, -2f };
        var u8 = new byte[volts.Length];
        CvbsStreamCodec.Encode(volts, CvbsFormat.U8, u8);
        Check.That(u8.SequenceEqual(new byte[] { 22, 76, 204, 140, 0, 255, 255, 0 }), $"u8 = round((v + 0.42)·182), clipped: {string.Join(' ', u8)}");

        var s16 = new byte[volts.Length * 2];
        CvbsStreamCodec.Encode(volts, CvbsFormat.S16, s16);
        short[] words = Enumerable.Range(0, volts.Length).Select(i => BitConverter.ToInt16(s16, i * 2)).ToArray();
        Check.That(words.SequenceEqual(new short[] { -7200, 0, 16800, 8400, -10080, 23520, 32767, -32768 }), $"s16 = round(v·24000), little-endian, clipped: {string.Join(' ', words)}");

        const int n = 4096;
        var ramp = new float[n];
        for (int i = 0; i < n; i++) ramp[i] = -0.4f + 1.3f * i / (n - 1);
        foreach (CvbsFormat format in new[] { CvbsFormat.U8, CvbsFormat.S16 })
        {
            var wire = new byte[n * CvbsStreamCodec.BytesPerSample(format)];
            var back = new float[n];
            var viaFifo = new short[n];
            var fifoVolts = new float[n];
            CvbsStreamCodec.Encode(ramp, format, wire);
            CvbsStreamCodec.Decode(wire, format, back);
            CvbsStreamCodec.DecodeToS16(wire, format, viaFifo);
            CvbsStreamCodec.S16ToVolts(viaFifo, fifoVolts);
            double lsb = format == CvbsFormat.U8 ? 1.0 / 182 : 1.0 / 24000, worst = 0, worstFifo = 0;
            for (int i = 0; i < n; i++)
            {
                worst = Math.Max(worst, Math.Abs(back[i] - ramp[i]));
                worstFifo = Math.Max(worstFifo, Math.Abs(fifoVolts[i] - back[i]));
            }
            Check.AtMost(worst, 0.5001 * lsb, $"{format} round trip error within half an LSB (volts)");
            Check.AtMost(worstFifo, 0.5001 / 24000, $"{format} wire → FIFO scale → volts agrees with the direct decode");
        }
    }

    private static void Handshake()
    {
        Check.Section("sender handshake and block framing");
        var hello = new SenderHello(CvbsFormat.S16, 20_000_000, 3, "Kamera übertragung");
        byte[] bytes = SenderProtocol.EncodeHello(hello);
        Check.That(bytes[0] == 'R' && bytes[1] == 'B' && bytes[2] == 'E' && bytes[3] == '1' && bytes[4] == 1 && bytes[5] == 1, "hello starts \"RBE1\", version 1, format byte");
        Check.That(BitConverter.ToUInt32(bytes, 8) == 20_000_000 && BitConverter.ToInt32(bytes, 12) == 3, "sample rate at offset 8, requested slot at offset 12");
        Check.That(bytes[16] == bytes.Length - 17, "name length byte counts UTF-8 bytes");
        Check.That(SenderProtocol.ReadHello(new MemoryStream(bytes)) == hello, "hello round trip (UTF-8 name)");
        SenderHello any = SenderProtocol.ReadHello(new MemoryStream(SenderProtocol.EncodeHello(new SenderHello(CvbsFormat.U8, 20_000_000, SenderProtocol.AnySlot, ""))));
        Check.That(any.RequestedSlot == -1 && any.Name == "" && any.Format == CvbsFormat.U8, "any-slot request with an empty name");

        Check.That(Throws<InvalidDataException>(() => SenderProtocol.ReadHello(new MemoryStream(Corrupt(bytes, 0, (byte)'X')))), "bad magic is rejected");
        Check.That(Throws<InvalidDataException>(() => SenderProtocol.ReadHello(new MemoryStream(Corrupt(bytes, 4, 2)))), "unknown version is rejected");
        Check.That(Throws<InvalidDataException>(() => SenderProtocol.ReadHello(new MemoryStream(Corrupt(bytes, 5, 7)))), "unknown format is rejected");
        Check.That(Throws<EndOfStreamException>(() => SenderProtocol.ReadHello(new MemoryStream(bytes, 0, bytes.Length - 3))), "truncated hello is rejected");

        var reply = new SenderReply(SenderStatus.SlotInUse, -1);
        Check.That(SenderProtocol.ReadReply(new MemoryStream(SenderProtocol.EncodeReply(reply))) == reply, "reply round trip");
        Check.That(SenderProtocol.EncodeReply(new SenderReply(SenderStatus.Ok, 4)).Length == 9, "reply is 9 bytes");

        var header = new byte[SenderProtocol.BlockHeaderBytes];
        var block = new SenderBlockHeader(160_000, 123_456_789_012L);
        SenderProtocol.WriteBlockHeader(header, block);
        Check.That(SenderProtocol.ParseBlockHeader(header) == block, "block header round trip (u32 count, u64 index)");
        SenderProtocol.WriteBlockHeader(header, new SenderBlockHeader(SenderProtocol.MaxBlockSamples + 1, 0));
        Check.That(Throws<InvalidDataException>(() => SenderProtocol.ParseBlockHeader(header)), "absurd block size is rejected");
    }

    private static byte[] Corrupt(byte[] source, int at, byte value)
    {
        byte[] copy = (byte[])source.Clone();
        copy[at] = value;
        return copy;
    }

    public static bool Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return true;
        }
        return false;
    }
}
