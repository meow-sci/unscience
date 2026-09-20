using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace RabbitEars.Tv;

/// <summary>
/// One `palindrome render --live` child: s16 IF samples go in on stdin (through a bounded queue and a writer
/// thread, so a slow decoder never stalls another one), raw frames come back on stdout (handed on with
/// <see cref="FramePacket.HeaderBytes"/> of spare room in front of the pixels), stderr is kept as a tail.
/// </summary>
public sealed class PalindromeProcess
{
    private const int QueueBlocks = 12;                                   // ≈ 100 ms of IF
    private const int StderrTailLines = 6;
    private readonly Process _process;
    private readonly BlockingCollection<(byte[] Buffer, int Length)> _queue = new(QueueBlocks);
    private readonly ConcurrentBag<byte[]> _pool = new();
    private readonly Action<PalindromeProcess, byte[]> _onFrame;
    private readonly Action<PalindromeProcess> _onExit;
    private readonly object _tailGate = new();
    private readonly List<string> _stderrTail = new();
    private int _frames, _retired;
    private long _droppedBlocks;

    public PalindromeProcess(string binary, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> knobs, int generation,
        Action<PalindromeProcess, byte[]> onFrame, Action<PalindromeProcess> onExit)
    {
        Knobs = new Dictionary<string, string>(knobs);
        Generation = generation;
        (Width, Height) = DecoderCommandLine.Raster(knobs);
        Channels = DecoderCommandLine.Channels(knobs);
        Stride = DecoderCommandLine.Stride(knobs);
        CommandLine = string.Join(' ', arguments);
        _onFrame = onFrame;
        _onExit = onExit;

        var info = new ProcessStartInfo(binary)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        _process = Process.Start(info) ?? throw new InvalidOperationException("could not start palindrome");
        Pid = _process.Id;
        Started = Stopwatch.GetTimestamp();
        StartThread(WriteLoop, "palindrome-stdin");
        StartThread(ReadFrames, "palindrome-frames");
        StartThread(ReadStderr, "palindrome-stderr");
    }

    public IReadOnlyDictionary<string, string> Knobs { get; }
    public int Generation { get; }
    public int Width { get; }
    public int Height { get; }
    public int Channels { get; }
    public int Stride { get; }
    public string CommandLine { get; }
    public int Pid { get; }
    public long Started { get; }
    public int Frames => Volatile.Read(ref _frames);
    public long DroppedBlocks => Interlocked.Read(ref _droppedBlocks);
    public int QueuedBlocks => _queue.Count;

    public bool Alive
    {
        get
        {
            try
            {
                return !_process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public string StderrTail
    {
        get { lock (_tailGate) return string.Join(" ", _stderrTail.TakeLast(2)); }
    }

    /// <summary>
    /// Queues IF samples for the decoder. With <paramref name="wait"/> the caller is held back while the queue is
    /// full (back-pressure, up to a second); without it a full queue drops the block. Returns false when not queued.
    /// </summary>
    public bool Enqueue(ReadOnlySpan<byte> s16, bool wait)
    {
        if (_queue.IsAddingCompleted) return false;
        if (!_pool.TryTake(out byte[]? buffer) || buffer.Length < s16.Length) buffer = new byte[s16.Length];
        s16.CopyTo(buffer);
        try
        {
            if (_queue.TryAdd((buffer, s16.Length), wait ? 1000 : 0)) return true;
        }
        catch (InvalidOperationException)
        {
            return false;                                                 // retired meanwhile
        }
        Interlocked.Increment(ref _droppedBlocks);
        _pool.Add(buffer);
        return false;
    }

    private void WriteLoop()
    {
        Stream stdin = _process.StandardInput.BaseStream;
        try
        {
            foreach ((byte[] buffer, int length) in _queue.GetConsumingEnumerable())
            {
                stdin.Write(buffer, 0, length);
                _pool.Add(buffer);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            _queue.CompleteAdding();                                      // the decoder went away
        }
    }

    private void ReadFrames()
    {
        Stream stdout = _process.StandardOutput.BaseStream;
        int size = Width * Height * Channels;
        try
        {
            while (true)
            {
                var frame = new byte[FramePacket.HeaderBytes + size];       // room for the viewer header: no second copy
                int got = 0;
                while (got < size)
                {
                    int n = stdout.Read(frame, FramePacket.HeaderBytes + got, size - got);
                    if (n <= 0)
                    {
                        _onExit(this);
                        return;
                    }
                    got += n;
                }
                Interlocked.Increment(ref _frames);
                _onFrame(this, frame);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            _onExit(this);
        }
    }

    private void ReadStderr()
    {
        try
        {
            while (_process.StandardError.ReadLine() is { } line)
            {
                if (line.Trim().Length == 0) continue;
                lock (_tailGate)
                {
                    _stderrTail.Add(line.Trim());
                    if (_stderrTail.Count > StderrTailLines) _stderrTail.RemoveAt(0);
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }
    }

    /// <summary>Polite shutdown in the background: end of input, a second to finish, then kill.</summary>
    public void Retire()
    {
        if (Interlocked.Exchange(ref _retired, 1) != 0) return;
        StartThread(() =>
        {
            _queue.CompleteAdding();
            try
            {
                _process.StandardInput.BaseStream.Close();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }
            try
            {
                if (!_process.WaitForExit(1000)) _process.Kill();
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }, "palindrome-retire");
    }

    /// <summary>Immediate stop (application shutdown).</summary>
    public void Kill()
    {
        Interlocked.Exchange(ref _retired, 1);
        _queue.CompleteAdding();
        try
        {
            if (!_process.HasExited) _process.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void StartThread(ThreadStart body, string name) => new Thread(body) { IsBackground = true, Name = name }.Start();
}
