using VideoArchiveFinder.Application.Storage;

namespace VideoArchiveFinder.Infrastructure.Storage;

public sealed class LocalApplicationDataDirectoryProvider
    : IApplicationDataDirectoryProvider
{
    public string GetApplicationDataDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "VideoArchiveFinder");
    }
}
