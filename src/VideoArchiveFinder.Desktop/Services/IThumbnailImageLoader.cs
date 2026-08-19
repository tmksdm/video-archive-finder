using System.Windows.Media.Imaging;

namespace VideoArchiveFinder.Desktop.Services;

public interface IThumbnailImageLoader
{
    Task<BitmapSource> LoadAsync(
        string thumbnailPath,
        int decodePixelWidth,
        CancellationToken cancellationToken = default);
}
