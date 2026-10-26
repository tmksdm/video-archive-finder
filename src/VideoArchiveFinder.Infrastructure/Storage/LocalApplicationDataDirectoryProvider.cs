using VideoArchiveFinder.Application.Storage;

namespace VideoArchiveFinder.Infrastructure.Storage;

public sealed class LocalApplicationDataDirectoryProvider
    : IApplicationDataDirectoryProvider
{
    public string GetApplicationDataDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable(
            "VAF_DATA_DIRECTORY");

        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (!Path.IsPathFullyQualified(overridePath))
            {
                throw new InvalidOperationException(
                    "VAF_DATA_DIRECTORY must be an absolute path.");
            }

            return Path.GetFullPath(overridePath);
        }

        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "VideoArchiveFinder");
    }
}
