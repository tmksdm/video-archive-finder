using Microsoft.Extensions.DependencyInjection;
using VideoArchiveFinder.Application.ArchiveSources;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Infrastructure.ArchiveSources;
using VideoArchiveFinder.Infrastructure.Indexing;
using VideoArchiveFinder.Infrastructure.Storage;

namespace VideoArchiveFinder.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddVideoArchiveFinderInfrastructure(
        this IServiceCollection services)
    {
        services.AddSingleton<
            IApplicationDataDirectoryProvider,
            LocalApplicationDataDirectoryProvider>();

        services.AddSingleton<IndexDatabasePathProvider>();

        services.AddSingleton<
            IIndexDatabaseInitializer,
            SqliteIndexDatabaseInitializer>();

        services.AddSingleton<
            IFolderIndexRepository,
            SqliteFolderIndexRepository>();

        services.AddSingleton<
            IFolderIndexingStateRepository,
            SqliteFolderIndexingStateRepository>();

        services.AddSingleton<
            IFolderFileSystem,
            SystemFolderFileSystem>();

        services.AddSingleton<
            IFolderTreeEnumerator,
            SystemFolderTreeEnumerator>();

        services.AddSingleton<
            IFolderIndexingService,
            FolderIndexingService>();

        services.AddSingleton<
            IArchivePathProbe,
            SystemArchivePathProbe>();

        services.AddSingleton<
            IArchiveSourceAvailabilityChecker,
            ArchiveSourceAvailabilityChecker>();

        services.AddSingleton<
            IArchiveSourceStore,
            JsonArchiveSourceStore>();

        services.AddSingleton<
            IArchiveSourceService,
            ArchiveSourceService>();

        return services;
    }
}
