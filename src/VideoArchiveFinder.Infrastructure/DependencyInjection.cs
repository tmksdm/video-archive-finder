using Microsoft.Extensions.DependencyInjection;
using VideoArchiveFinder.Application.ArchiveSources;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Infrastructure.ArchiveSources;
using VideoArchiveFinder.Infrastructure.Indexing;
using VideoArchiveFinder.Infrastructure.Storage;
using VideoArchiveFinder.Infrastructure.Search;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Application.Settings;
using VideoArchiveFinder.Infrastructure.Settings;
using VideoArchiveFinder.Application.ExternalTools;
using VideoArchiveFinder.Infrastructure.ExternalTools;





namespace VideoArchiveFinder.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddVideoArchiveFinderInfrastructure(
        this IServiceCollection services)
    {

        services.AddSingleton<
            IFfmpegToolsLocator,
            BundledFfmpegToolsLocator>();

        services.AddSingleton<
            IExternalProcessRunner,
            SystemExternalProcessRunner>();

        services.AddSingleton<
            IFfprobeRunner,
            FfprobeRunner>();

        services.AddSingleton<
            IFfprobeJsonParser,
            FfprobeJsonParser>();

        services.AddSingleton<
            IApplicationDataDirectoryProvider,
            LocalApplicationDataDirectoryProvider>();

        services.AddSingleton<
            IUserSettingsStore,
            JsonUserSettingsStore>();

        services.AddSingleton<IndexDatabasePathProvider>();

        services.AddSingleton<
            IIndexDatabaseInitializer,
            SqliteIndexDatabaseInitializer>();

        services.AddSingleton<
            IFolderIndexRepository,
            SqliteFolderIndexRepository>();

        services.AddSingleton<
            IVideoFileIndexRepository,
            SqliteVideoFileIndexRepository>();

        services.AddSingleton<
            IVideoFileCandidatePolicy,
            VideoFileCandidatePolicy>();

        services.AddSingleton<
            IVideoFileDiscoveryService,
            VideoFileDiscoveryService>();

        services.AddSingleton<
            IVideoFileSystem,
            SystemVideoFileSystem>();

        services.AddSingleton<
            IFolderIndexingStateRepository,
            SqliteFolderIndexingStateRepository>();

        services.AddSingleton<
            IFolderIndexCleanupService,
            SqliteFolderIndexCleanupService>();

        services.AddSingleton<
            IFolderFileSystem,
            SystemFolderFileSystem>();

        services.AddSingleton<
            IFolderTreeEnumerator,
            SystemFolderTreeEnumerator>();

        services.AddSingleton<
            ITextNormalizationService,
            TextNormalizationService>();

        services.AddSingleton<
            ISearchStemService,
            RussianSearchStemService>();

        services.AddSingleton<
            IFolderSearchService,
            SqliteFolderSearchService>();

        services.AddSingleton<
            IFolderSearchContextProvider,
            SqliteFolderSearchContextProvider>();

        services.AddSingleton<
            IFolderNameHighlightService,
            FolderNameHighlightService>();

        services.AddSingleton<FolderSearchTreeBuilder>();

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
