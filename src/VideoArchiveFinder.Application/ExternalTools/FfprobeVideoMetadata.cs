namespace VideoArchiveFinder.Application.ExternalTools;

public sealed record FfprobeVideoMetadata(
    bool HasVideoStream,
    TimeSpan? Duration,
    int? Width,
    int? Height,
    string? CodecName);
