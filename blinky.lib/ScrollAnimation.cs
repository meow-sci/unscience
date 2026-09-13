using System;
using System.Collections.Generic;
using System.Linq;
using KSA;

namespace MeowSci.BlinkyLib;

/// <summary>
/// Manages a scrolling pixel animation: maintains scroll state and applies pixel on/off to engine controllers.
/// Scrolls user-supplied pixel data horizontally across the grid at a configurable speed.
/// </summary>
public class ScrollAnimation
{
    private float _scrollOffset;
    private int _lastScrollCol = -1;
    private int _totalScroll;
    private int _imageWidth;
    private int _imageHeight;
    private HashSet<(int x, int y)> _pixelSet = new();
    private PixelGrid? _grid;

    internal (int x, int y)[] SavedPixels => _pixelSet.ToArray();
    internal float SavedOffset => _scrollOffset;
    internal void RestoreOffset(float value) { _scrollOffset = value; _lastScrollCol = -1; Update(0); }

    public float ScrollSpeed { get; set; } = 3f;
    public int GridRows { get; private set; }
    public int GridCols { get; private set; }
    public int ImageWidth => _imageWidth;
    public int ImageHeight => _imageHeight;
    public float ScrollOffset => _scrollOffset;
    public bool IsActive { get; private set; }

    /// <summary>
    /// Starts a scrolling animation with the given pixel data.
    /// </summary>
    /// <param name="grid">The pixel grid to animate on.</param>
    /// <param name="pixels">Sparse pixel coordinates that are "on" in the source image.</param>
    /// <param name="speed">Scroll speed in pixels/second.</param>
    public void Start(PixelGrid grid, (int x, int y)[] pixels, float speed)
    {
        _grid = grid;
        GridRows = grid.Rows;
        GridCols = grid.Cols;
        ScrollSpeed = speed;

        if (pixels.Length == 0)
        {
            _imageWidth = 0;
            _imageHeight = 0;
            Console.WriteLine("blinky: scroll animation has no pixel data");
            return;
        }

        _imageWidth = pixels.Max(p => p.x) + 1;
        _imageHeight = pixels.Max(p => p.y) + 1;
        _pixelSet = new HashSet<(int x, int y)>(pixels);

        // Total scroll distance: image width + half-grid-width gap before repeat
        _totalScroll = _imageWidth + (GridCols / 2);
        _scrollOffset = 0f;
        _lastScrollCol = -1; // force first frame to apply
        IsActive = true;

        Console.WriteLine($"blinky: scroll start — grid {GridCols}x{GridRows}, image {_imageWidth}x{_imageHeight}, speed {speed}, totalScroll {_totalScroll}");
    }

    /// <summary>Starts a scrolling animation using the built-in default pixel data.</summary>
    public void StartBuiltIn(PixelGrid grid, float speed)
    {
        Start(grid, BuiltInScrollPixels.Pixels, speed);
    }

    /// <summary>Stops the animation. Does NOT turn off pixels — call TurnOffAll on BlinkyGridManager for that.</summary>
    public void Stop()
    {
        IsActive = false;
        _lastScrollCol = -1;
    }

    /// <summary>Advances the animation by dt seconds and updates engine active states when the integer column changes.</summary>
    public void Update(double dt)
    {
        if (!IsActive || _grid == null) return;

        _scrollOffset += ScrollSpeed * (float)dt;

        // Wrap around when we've scrolled the full cycle
        if (_scrollOffset >= _totalScroll)
            _scrollOffset -= _totalScroll;

        int scrollCol = (int)_scrollOffset;

        // Skip update if we haven't moved to a new integer column
        if (scrollCol == _lastScrollCol) return;
        _lastScrollCol = scrollCol;

        foreach (var (key, engines) in _grid.Engines)
        {
            int srcX = scrollCol + key.col;
            int srcY = key.row;

            bool on = srcX >= 0 && srcX < _imageWidth
                   && srcY >= 0 && srcY < _imageHeight
                   && _pixelSet.Contains((srcX, srcY));

            for (int i = 0; i < engines.Length; i++)
                engines[i].SetIsActive(null, on);
        }
    }
}
