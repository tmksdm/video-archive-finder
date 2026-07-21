using Microsoft.Extensions.DependencyInjection;
using VideoArchiveFinder.Application.ArchiveSources;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Infrastructure.ArchiveSources;
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

        services.AddSingleton<
            IArchiveSourceStore,
            JsonArchiveSourceStore>();

        return services;
    }
}
