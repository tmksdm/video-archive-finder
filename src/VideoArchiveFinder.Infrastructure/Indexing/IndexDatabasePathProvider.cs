using VideoArchiveFinder.Application.Storage;

namespace VideoArchiveFinder.Infrastructure.Indexing;

public sealed class IndexDatabasePathProvider
{
    private const string IndexDirectoryName = "Index";
    private const string DatabaseFileName = "video-archive-finder.db";

    private readonly IApplicationDataDirectoryProvider
        _applicationDataDirectoryProvider;

    public IndexDatabasePathProvider(
        IApplicationDataDirectoryProvider applicationDataDirectoryProvider)
    {
        _applicationDataDirectoryProvider =
            applicationDataDirectoryProvider;
    }

    public string GetDatabasePath()
    {
        return Path.Combine(
            _applicationDataDirectoryProvider
                .GetApplicationDataDirectory(),
            IndexDirectoryName,
            DatabaseFileName);
    }
}
