using System;
using System.Collections.Generic;
using System.Linq;

namespace MeowSci.ItsSoShinyLib;

public sealed class ShinyScrollAnimation
{
    private float _scrollOffset;
    private int _lastScrollCol = -1;
    private int _totalScroll;
    private int _imageWidth;
    private int _imageHeight;
    private HashSet<(int x, int y)> _pixelSet = new();
    private ShinyGridState? _state;

    internal (int x, int y)[] SavedPixels => _pixelSet.ToArray();
    internal float SavedOffset => _scrollOffset;
    internal void RestoreOffset(float value) { _scrollOffset = value; _lastScrollCol = -1; Update(0); }

    public float ScrollSpeed { get; set; } = 3f;
    public bool IsActive { get; private set; }

    public void Start(ShinyGridState state, (int x, int y)[] pixels, float speed)
    {
        _state = state;
        ScrollSpeed = speed;

        if (pixels.Length == 0)
        {
            Console.WriteLine("its-so-shiny: scroll animation has no pixel data");
            return;
        }

        _imageWidth = pixels.Max(p => p.x) + 1;
        _imageHeight = pixels.Max(p => p.y) + 1;
        _pixelSet = new HashSet<(int x, int y)>(pixels);
        _totalScroll = _imageWidth + (state.ShinyGrid.Grid.Cols / 2);
        _scrollOffset = 0f;
        _lastScrollCol = -1;
        IsActive = true;
    }

    public void Stop()
    {
        IsActive = false;
        _lastScrollCol = -1;
    }

    public void Update(double dt)
    {
        if (!IsActive || _state == null) return;

        _scrollOffset += ScrollSpeed * (float)dt;
        if (_scrollOffset >= _totalScroll)
            _scrollOffset -= _totalScroll;

        int scrollCol = (int)_scrollOffset;
        if (scrollCol == _lastScrollCol) return;
        _lastScrollCol = scrollCol;

        foreach (var (key, cell) in _state.ShinyGrid.Grid.Cells)
        {
            int srcX = scrollCol + key.col;
            int srcY = key.row;
            bool on = srcX >= 0 && srcX < _imageWidth
                   && srcY >= 0 && srcY < _imageHeight
                   && _pixelSet.Contains((srcX, srcY));
            cell.SetEnabled(on, _state.Intensity);
        }
    }
}