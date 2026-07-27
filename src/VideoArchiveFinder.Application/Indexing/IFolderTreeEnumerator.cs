namespace VideoArchiveFinder.Application.Indexing;

public interface IFolderTreeEnumerator
{
    IAsyncEnumerable<FolderEnumerationEntry> EnumerateAsync(
        string rootPath,
        CancellationToken cancellationToken = default);
}
