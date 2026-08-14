using System.Globalization;

namespace VideoArchiveFinder.Infrastructure.Thumbnails;

internal static class StaticThumbnailProfile
{
    public const int CacheFormatVersion = 1;

    public const int OutputWidth = 480;

    public const int JpegQuality = 3;

    public static readonly TimeSpan SeekPosition =
        TimeSpan.Zero;


    public static string GetCacheDescriptor()
    {
        return string.Join(
            ';',
            $"version={CacheFormatVersion}",
            $"format=jpg",
            $"width={OutputWidth}",
            $"jpegQuality={JpegQuality}",
            $"seekMilliseconds=" +
            SeekPosition.TotalMilliseconds.ToString(
                "0",
                CultureInfo.InvariantCulture));
    }
}
