using System.Windows;

namespace VideoArchiveFinder.Desktop.Services;

public sealed class WindowsClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        Clipboard.SetDataObject(
            text,
            copy: true);
    }
}
