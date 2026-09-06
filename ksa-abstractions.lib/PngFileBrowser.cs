using System;
namespace MeowSci.KsaAbstractions;

/// <summary>PNG browser facade retaining the existing consumer API.</summary>
public sealed class PngFileBrowser
{
    private readonly LibraryFileBrowser _browser;
    public PngFileBrowser(string id, string windowTitle = "Import PNG") =>
        _browser = new(PngLibrary.Catalog, id, windowTitle);
    public bool Visible => _browser.Visible;
    public void Open() => _browser.Open();
    public void Render(Action<string> onImported) => _browser.Render(onImported);
}
