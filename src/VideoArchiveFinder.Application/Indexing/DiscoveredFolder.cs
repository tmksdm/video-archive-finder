namespace VideoArchiveFinder.Application.Indexing;

public sealed record DiscoveredFolder(
    string FullPath,
    string Name,
    string? ParentFullPath,
    int DirectSubfolderCount,
    bool IsAvailable,
    bool IsReparsePoint)
    : FolderEnumerationEntry;
