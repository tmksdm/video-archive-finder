namespace VideoArchiveFinder.Application.VideoFiles;

public sealed record VideoFileDiscoveryResult(
    IReadOnlyList<DiscoveredVideoFile> Files,
    int ErrorCount,
    bool CanRemoveStaleEntries);
