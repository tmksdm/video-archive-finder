namespace VideoArchiveFinder.Application.Indexing;

public sealed record FolderIndexingResult(
    Guid RootSourceId,
    int DiscoveredFolderCount,
    int IndexedFolderCount,
    int ErrorCount,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);
