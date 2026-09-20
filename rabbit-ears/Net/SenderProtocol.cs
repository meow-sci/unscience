using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace RabbitEars.Net;

/// <summary>The server's answer to a handshake (the status byte on the wire).</summary>
public enum SenderStatus : byte
{
    Ok = 0,
    BadHandshake = 1,
    UnsupportedFormat = 2,
    SlotInUse = 3,
    NoFreeSlot = 4,
    SlotOutOfRange = 5,
}

/// <summary>What a sender announces about itself.</summary>
public sealed record SenderHello(CvbsFormat Format, int SampleRate, int RequestedSlot, string Name);

/// <summary>The server's reply: a status and the slot the sender now owns.</summary>
public readonly record struct SenderReply(SenderStatus Status, int AssignedSlot);

/// <summary>Header of one block of samples.</summary>
public readonly record struct SenderBlockHeader(int SampleCount, long FirstSampleIndex);

/// <summary>
/// Wire format of the sender contract (little-endian):
/// hello  = "RBE1" | u8 version | u8 format | u16 reserved | u32 sample_rate | i32 requested_slot | u8 name_len | name
/// reply  = "RBE1" | u8 status | i32 assigned_slot
/// block  = u32 sample_count | u64 first_sample_index | samples
/// </summary>
public static class SenderProtocol
{
    public const byte Version = 1;
    public const int AnySlot = -1;
    public const int BlockHeaderBytes = 12;
    public const int MaxBlockSamples = 4_000_000;                         // 200 ms: anything larger is a broken stream
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("RBE1");

    public static byte[] EncodeHello(SenderHello hello)
    {
        byte[] name = Encoding.UTF8.GetBytes(hello.Name);
        if (name.Length > 255) Array.Resize(ref name, 255);
        var bytes = new byte[17 + name.Length];
        Magic.CopyTo(bytes, 0);
        bytes[4] = Version;
        bytes[5] = (byte)hello.Format;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)hello.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), hello.RequestedSlot);
        bytes[16] = (byte)name.Length;
        name.CopyTo(bytes, 17);
        return bytes;
    }

    /// <summary>Reads a hello; throws <see cref="InvalidDataException"/> when it is not one.</summary>
    public static SenderHello ReadHello(Stream stream)
    {
        var head = new byte[17];
        ReadExactly(stream, head);
        if (!head.AsSpan(0, 4).SequenceEqual(Magic)) throw new InvalidDataException("not a rabbit-ears sender (bad magic)");
        if (head[4] != Version) throw new InvalidDataException($"unsupported sender protocol version {head[4]}");
        if (head[5] > (byte)CvbsFormat.S16) throw new InvalidDataException($"unknown sample format {head[5]}");
        var name = new byte[head[16]];
        ReadExactly(stream, name);
        return new SenderHello((CvbsFormat)head[5], (int)BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(8)),
            BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(12)), Encoding.UTF8.GetString(name));
    }

    public static byte[] EncodeReply(SenderReply reply)
    {
        var bytes = new byte[9];
        Magic.CopyTo(bytes, 0);
        bytes[4] = (byte)reply.Status;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(5), reply.AssignedSlot);
        return bytes;
    }

    public static SenderReply ReadReply(Stream stream)
    {
        var bytes = new byte[9];
        ReadExactly(stream, bytes);
        if (!bytes.AsSpan(0, 4).SequenceEqual(Magic)) throw new InvalidDataException("not a rabbit-ears server (bad magic)");
        return new SenderReply((SenderStatus)bytes[4], BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(5)));
    }

    public static void WriteBlockHeader(Span<byte> target, SenderBlockHeader header)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(target, (uint)header.SampleCount);
        BinaryPrimitives.WriteUInt64LittleEndian(target.Slice(4), (ulong)header.FirstSampleIndex);
    }

    public static SenderBlockHeader ParseBlockHeader(ReadOnlySpan<byte> bytes)
    {
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (count > MaxBlockSamples) throw new InvalidDataException($"block of {count} samples is too large");
        return new SenderBlockHeader((int)count, (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(4)));
    }

    /// <summary>Fills the buffer or throws <see cref="EndOfStreamException"/>.</summary>
    public static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        int got = 0;
        while (got < buffer.Length)
        {
            int n = stream.Read(buffer.Slice(got));
            if (n <= 0) throw new EndOfStreamException("sender stream closed");
            got += n;
        }
    }

    public static string Describe(SenderStatus status) => status switch
    {
        SenderStatus.Ok => "ok",
        SenderStatus.BadHandshake => "the server did not accept the handshake",
        SenderStatus.UnsupportedFormat => "the server does not support this sample format or rate (20 MS/s, u8 | s16)",
        SenderStatus.SlotInUse => "that slot is already in use",
        SenderStatus.NoFreeSlot => "no free slot in the band",
        SenderStatus.SlotOutOfRange => "that slot does not exist in the server's band plan",
        _ => $"error status {(byte)status}",
    };
}
