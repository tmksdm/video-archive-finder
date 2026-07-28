namespace VideoArchiveFinder.Application.Indexing;

public sealed record FolderIndexingState(
    Guid RootSourceId,
    int DiscoveredFolderCount,
    int IndexedFolderCount,
    int ErrorCount,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);
