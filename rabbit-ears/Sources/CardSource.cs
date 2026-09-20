using System;
using RabbitEars.Pal;

namespace RabbitEars.Sources;

/// <summary>
/// A test card with things that move (clock, frame counter, bouncing ball, ticker) so it is obviously live.
/// Time is the mux clock (frame index / 25), which keeps offline renders deterministic apart from the start time.
/// </summary>
public sealed class CardSource : IFrameSource
{
    private readonly Canvas _background;
    private readonly Canvas _frame = new();
    private readonly TimeSpan _startOfDay;
    private readonly int _channelNumber;
    private readonly string _ticker;

    public CardSource(string style, int channelNumber, string name, string caption, TimeSpan? startOfDay = null)
    {
        Name = name;
        _channelNumber = channelNumber;
        _background = TestCards.Render(style, channelNumber, name, caption);
        _startOfDay = startOfDay ?? DateTime.Now.TimeOfDay;
        _ticker = $"   RABBIT EARS  ***  CHANNEL {channelNumber}  {name}  ***  {caption}  ***  PAL-I 625/50  ***";
    }

    public string Name { get; }

    public VideoFrame GetFrame(long frameIndex)
    {
        _frame.CopyFrom(_background);
        double t = frameIndex / 25.0;

        // Each channel's ball follows its own path and colour cycle.
        double spanX = Canvas.VirtualWidth - 160, spanY = Canvas.VirtualHeight - 200;
        double bx = 80 + Math.Abs((t * (150 + 23 * _channelNumber) + 97 * _channelNumber) % (2 * spanX) - spanX);
        double by = 70 + Math.Abs((t * (110 + 17 * _channelNumber) + 53 * _channelNumber) % (2 * spanY) - spanY);
        _frame.FillEllipse(bx, by, 30, Rgb.White);
        _frame.FillEllipse(bx, by, 27, Rgb.FromHsl(t * 40 + _channelNumber * 60, 1.0, 0.5));

        TimeSpan now = _startOfDay + TimeSpan.FromSeconds(t);
        string clock = $"{now.Hours:00}:{now.Minutes:00}:{now.Seconds:00}.{now.Milliseconds / 10:00}";
        _frame.FillRect(Canvas.VirtualWidth - 330, 40, 282, 44, Rgb.Black);
        _frame.Text(Canvas.VirtualWidth - 322, 48, 4, clock, Rgb.White);
        _frame.FillRect(48, 40, 120, 44, Rgb.Black);
        _frame.Text(56, 48, 4, $"{frameIndex % 10000:0000}", new Rgb(255, 220, 60));

        _frame.FillRect(0, Canvas.VirtualHeight - 78, Canvas.VirtualWidth, 34, new Rgb(10, 10, 60));
        int tickerWidth = _ticker.Length * BitmapFont.Advance * 3;
        double offset = (t * 120) % tickerWidth;
        for (double x = -offset; x < Canvas.VirtualWidth; x += tickerWidth)
            _frame.Text(x, Canvas.VirtualHeight - 72, 3, _ticker, Rgb.White);

        return new VideoFrame(_frame.Pixels, Canvas.Width, Canvas.Height);
    }

    public void Dispose()
    {
    }
}
