namespace VideoArchiveFinder.Application.Thumbnails;

public enum StaticThumbnailGenerationStatus
{
    Generated,
    CacheHit,
    ToolUnavailable,
    InputUnavailable,
    TimedOut,
    Failed
}
