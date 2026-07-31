namespace VideoArchiveFinder.Application.VideoFiles;

public sealed record VideoFileIndexUpsertItem(
    string FullPath,
    string Name,
    string NormalizedName,
    string Extension,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc,
    string FolderFullPath,
    Guid RootSourceId,
    bool IsAvailable,
    DateTimeOffset LastSeenUtc);
