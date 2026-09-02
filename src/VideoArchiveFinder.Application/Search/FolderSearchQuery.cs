namespace VideoArchiveFinder.Application.Search;

public sealed record FolderSearchQuery(
    string Text,
    FolderSearchMode Mode = FolderSearchMode.Smart,
    int MaxResults = 200,
    IReadOnlyCollection<Guid>? RootSourceIds = null);
