using VideoArchiveFinder.Infrastructure.Storage;

namespace VideoArchiveFinder.Tests;

[CollectionDefinition("Application data environment", DisableParallelization = true)]
public sealed class ApplicationDataEnvironmentCollection;

[Collection("Application data environment")]
public sealed class LocalApplicationDataDirectoryProviderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void WithoutOverride_UsesExistingLocalApplicationData(string? value)
    {
        WithEnvironment(value, () =>
            Assert.Equal(
                Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                    "VideoArchiveFinder"),
                new LocalApplicationDataDirectoryProvider()
                    .GetApplicationDataDirectory()));
    }

    [Fact]
    public void AbsoluteOverride_IsolatedFromDefaultDirectory()
    {
        var isolatedPath = Path.Combine(Path.GetTempPath(),
            "VAF-isolation-" + Guid.NewGuid().ToString("N"));

        WithEnvironment(isolatedPath, () =>
            Assert.Equal(Path.GetFullPath(isolatedPath),
                new LocalApplicationDataDirectoryProvider()
                    .GetApplicationDataDirectory()));
    }

    [Theory]
    [InlineData("relative-data")]
    [InlineData("..\\data")]
    public void RelativeOverride_FailsInsteadOfUsingWorkingData(string value)
    {
        WithEnvironment(value, () =>
            Assert.Throws<InvalidOperationException>(() =>
                new LocalApplicationDataDirectoryProvider()
                    .GetApplicationDataDirectory()));
    }

    private static void WithEnvironment(string? value, Action action)
    {
        var original = Environment.GetEnvironmentVariable("VAF_DATA_DIRECTORY");

        try
        {
            Environment.SetEnvironmentVariable("VAF_DATA_DIRECTORY", value);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("VAF_DATA_DIRECTORY", original);
        }
    }
}
