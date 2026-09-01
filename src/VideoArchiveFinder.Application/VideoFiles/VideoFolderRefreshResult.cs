namespace VideoArchiveFinder.Application.VideoFiles;

public sealed record VideoFolderRefreshResult(
    IReadOnlyList<IndexedVideoFile> Files,
    int ErrorCount,
    bool IsComplete);
