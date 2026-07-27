namespace VideoArchiveFinder.Application.Indexing;

public sealed record FolderIndexingProgress(
    FolderIndexingStage Stage,
    string? CurrentPath,
    int DiscoveredFolderCount,
    int IndexedFolderCount,
    int ErrorCount);
