using System;

namespace RabbitEars.Sources;

/// <summary>One RGB24 picture (row-major, 3 bytes per pixel, gamma-encoded). Any size; shown as 4:3.</summary>
public readonly struct VideoFrame
{
    public VideoFrame(byte[] rgb, int width, int height)
    {
        if (rgb.Length < width * height * 3) throw new ArgumentException("frame buffer too small");
        Rgb = rgb;
        Width = width;
        Height = height;
    }

    public byte[] Rgb { get; }
    public int Width { get; }
    public int Height { get; }
}

/// <summary>
/// A 25 frames/s picture source. <see cref="GetFrame"/> is called once per transmitted frame, from the
/// thread that runs that channel's encoder; the returned buffer must stay untouched until the next call.
/// </summary>
public interface IFrameSource : IDisposable
{
    string Name { get; }

    /// <param name="frameIndex">Frames since the encoder started (the mux clock: frame n is at n/25 s).</param>
    VideoFrame GetFrame(long frameIndex);
}
