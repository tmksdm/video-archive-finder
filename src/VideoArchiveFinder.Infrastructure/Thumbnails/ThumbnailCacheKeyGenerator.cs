using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VideoArchiveFinder.Application.Thumbnails;

namespace VideoArchiveFinder.Infrastructure.Thumbnails;

public sealed class ThumbnailCacheKeyGenerator
    : IThumbnailCacheKeyGenerator
{
    public string GenerateKey(
        StaticThumbnailRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.VideoPath);

        ArgumentOutOfRangeException.ThrowIfNegative(
            request.SizeBytes);

        var normalizedPath = NormalizePath(
            request.VideoPath);

        var canonicalValue = string.Join(
            '\n',
            normalizedPath,
            request.SizeBytes.ToString(
                CultureInfo.InvariantCulture),
            request.LastWriteTimeUtc
                .UtcDateTime
                .Ticks
                .ToString(CultureInfo.InvariantCulture),
            StaticThumbnailProfile.GetCacheDescriptor());

        var valueBytes = Encoding.UTF8.GetBytes(
            canonicalValue);

        var hashBytes = SHA256.HashData(valueBytes);

        return Convert
            .ToHexString(hashBytes)
            .ToLowerInvariant();
    }

    private static string NormalizePath(
        string videoPath)
    {
        var fullPath = Path.GetFullPath(videoPath);

        var normalizedSeparators = fullPath.Replace(
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar);

        return normalizedSeparators.ToUpperInvariant();
    }
}
