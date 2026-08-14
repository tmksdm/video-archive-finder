namespace VideoArchiveFinder.Application.Thumbnails;

public interface IThumbnailCacheKeyGenerator
{
    string GenerateKey(StaticThumbnailRequest request);
}
