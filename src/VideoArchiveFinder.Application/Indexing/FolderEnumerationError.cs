namespace VideoArchiveFinder.Application.Indexing;

public sealed record FolderEnumerationError(
    string DirectoryPath,
    Exception Exception)
    : FolderEnumerationEntry;
