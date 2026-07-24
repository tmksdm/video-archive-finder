namespace VideoArchiveFinder.Application.Indexing;

public interface IIndexDatabaseInitializer
{
    Task InitializeAsync(
        CancellationToken cancellationToken = default);
}
