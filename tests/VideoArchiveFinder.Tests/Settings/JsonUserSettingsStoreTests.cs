using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Settings;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Infrastructure.Settings;

namespace VideoArchiveFinder.Tests.Settings;

public sealed class JsonUserSettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ReturnsDefaults()
    {
        using var directory =
            new TemporaryDirectory();

        using var store =
            CreateStore(directory.DirectoryPath);

        var settings =
            await store.LoadAsync();

        Assert.Equal(
            VideoFilesViewMode.Grid,
            settings.VideoFilesViewMode);

        Assert.Equal(
            UserSettings.DefaultGridCardWidth,
            settings.GridCardWidth);
    }

    [Fact]
    public async Task SaveAndLoadAsync_PreservesSettings()
    {
        using var directory =
            new TemporaryDirectory();

        using var store =
            CreateStore(directory.DirectoryPath);

        var expected = new UserSettings
        {
            VideoFilesViewMode =
                VideoFilesViewMode.List,

            GridCardWidth = 285
        };

        await store.SaveAsync(expected);

        var actual =
            await store.LoadAsync();

        Assert.Equal(
            expected.VideoFilesViewMode,
            actual.VideoFilesViewMode);

        Assert.Equal(
            expected.GridCardWidth,
            actual.GridCardWidth);

        Assert.True(
            File.Exists(
                Path.Combine(
                    directory.DirectoryPath,
                    "user-settings.json")));
    }

    [Fact]
    public async Task LoadAsync_WhenJsonIsInvalid_ReturnsDefaults()
    {
        using var directory =
            new TemporaryDirectory();

        var filePath = Path.Combine(
            directory.DirectoryPath,
            "user-settings.json");

        await File.WriteAllTextAsync(
            filePath,
            "{ invalid json");

        using var store =
            CreateStore(directory.DirectoryPath);

        var settings =
            await store.LoadAsync();

        Assert.Equal(
            VideoFilesViewMode.Grid,
            settings.VideoFilesViewMode);

        Assert.Equal(
            UserSettings.DefaultGridCardWidth,
            settings.GridCardWidth);
    }

    [Fact]
    public async Task LoadAsync_WhenSchemaIsUnsupported_ReturnsDefaults()
    {
        using var directory =
            new TemporaryDirectory();

        var filePath = Path.Combine(
            directory.DirectoryPath,
            "user-settings.json");

        await File.WriteAllTextAsync(
            filePath,
            """
            {
              "SchemaVersion": 999,
              "Settings": {
                "VideoFilesViewMode": "List",
                "GridCardWidth": 300
              }
            }
            """);

        using var store =
            CreateStore(directory.DirectoryPath);

        var settings =
            await store.LoadAsync();

        Assert.Equal(
            VideoFilesViewMode.Grid,
            settings.VideoFilesViewMode);

        Assert.Equal(
            UserSettings.DefaultGridCardWidth,
            settings.GridCardWidth);
    }

    private static JsonUserSettingsStore CreateStore(
        string directoryPath)
    {
        return new JsonUserSettingsStore(
            new TestApplicationDataDirectoryProvider(
                directoryPath),
            NullLogger<JsonUserSettingsStore>.Instance);
    }

    private sealed class TestApplicationDataDirectoryProvider
        : IApplicationDataDirectoryProvider
    {
        private readonly string _directoryPath;

        public TestApplicationDataDirectoryProvider(
            string directoryPath)
        {
            _directoryPath = directoryPath;
        }

        public string GetApplicationDataDirectory()
        {
            return _directoryPath;
        }
    }

    private sealed class TemporaryDirectory
        : IDisposable
    {
        public TemporaryDirectory()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "VideoArchiveFinder.Tests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(
                DirectoryPath);
        }

        public string DirectoryPath
        {
            get;
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(
                    DirectoryPath,
                    recursive: true);
            }
        }
    }
}
