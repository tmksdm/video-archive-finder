namespace VideoArchiveFinder.Application.Indexing;

public sealed record FolderIndexUpsertItem(
    string FullPath,
    string Name,
    string NormalizedName,
    string SearchTokens,
    string SearchStems,
    string? ParentFullPath,
    Guid RootSourceId,
    bool IsAvailable,
    DateTimeOffset LastSeenUtc,
    int DirectSubfolderCount,
    int DirectVideoFileCount);
