using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Application.Thumbnails;

namespace VideoArchiveFinder.Infrastructure.Thumbnails;

public sealed class ThumbnailCachePathProvider
{
    private readonly IApplicationDataDirectoryProvider
        _applicationDataDirectoryProvider;

    private readonly IThumbnailCacheKeyGenerator
        _cacheKeyGenerator;

    public ThumbnailCachePathProvider(
        IApplicationDataDirectoryProvider
            applicationDataDirectoryProvider,
        IThumbnailCacheKeyGenerator cacheKeyGenerator)
    {
        _applicationDataDirectoryProvider =
            applicationDataDirectoryProvider;

        _cacheKeyGenerator = cacheKeyGenerator;
    }

    public string GetCacheDirectory()
    {
        return Path.Combine(
            _applicationDataDirectoryProvider
                .GetApplicationDataDirectory(),
            "Cache",
            "Thumbnails");
    }

    public string GetVersionDirectory()
    {
        return Path.Combine(
            GetCacheDirectory(),
            $"v{StaticThumbnailProfile.CacheFormatVersion}");
    }

    public string GetThumbnailPath(
        StaticThumbnailRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = _cacheKeyGenerator.GenerateKey(
            request);

        return Path.Combine(
            GetVersionDirectory(),
            key[..2],
            $"{key}.jpg");
    }
}
