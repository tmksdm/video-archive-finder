using System.IO;
using System.Windows.Media.Imaging;

namespace VideoArchiveFinder.Desktop.Services;

public sealed class WpfThumbnailImageLoader
    : IThumbnailImageLoader
{
    public Task<BitmapSource> LoadAsync(
        string thumbnailPath,
        int decodePixelWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            thumbnailPath);

        if (decodePixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decodePixelWidth),
                "Decode width must be positive.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => LoadCore(
                thumbnailPath,
                decodePixelWidth,
                cancellationToken),
            cancellationToken);
    }

    private static BitmapSource LoadCore(
        string thumbnailPath,
        int decodePixelWidth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var stream =
            new FileStream(
                thumbnailPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);

        var image = new BitmapImage();

        image.BeginInit();

        image.CacheOption =
            BitmapCacheOption.OnLoad;

        image.CreateOptions =
            BitmapCreateOptions.IgnoreImageCache;

        image.DecodePixelWidth =
            decodePixelWidth;

        image.StreamSource =
            stream;

        image.EndInit();

        cancellationToken.ThrowIfCancellationRequested();

        image.Freeze();

        return image;
    }
}
