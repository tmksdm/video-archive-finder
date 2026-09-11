using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Domain.ArchiveSources;
using VideoArchiveFinder.Infrastructure.ArchiveSources;

namespace VideoArchiveFinder.Tests.ArchiveSources;

public sealed class JsonArchiveSourceStoreTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "VideoArchiveFinder.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ReturnsEmptyList()
    {
        using var store = CreateStore();

        var sources = await store.LoadAsync();

        Assert.Empty(sources);
    }

    [Fact]
    public async Task SaveAndLoadAsync_PreservesSources()
    {
        using var store = CreateStore();

        var expectedSources = new[]
        {
            ArchiveSource.Create(
                @"C:\Video Archive",
                "окальный архив"),
            ArchiveSource.Create(
                @"\\media-server\archive",
                "Сетевой архив",
                ArchiveSourceIndexingMode
                    .FolderAndVideoFileNames)
        };

        await store.SaveAsync(expectedSources);
        var actualSources = await store.LoadAsync();

        Assert.Equal(2, actualSources.Count);

        Assert.Equal(
            expectedSources[0],
            actualSources[0]);

        Assert.Equal(
            expectedSources[1],
            actualSources[1]);

        Assert.Equal(
            ArchiveSourceIndexingMode.FolderAndVideoFileNames,
            actualSources[1].IndexingMode);
    }

    [Fact]
    public async Task LoadAsync_WhenJsonIsDamaged_ReturnsEmptyList()
    {
        Directory.CreateDirectory(_temporaryDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(
                _temporaryDirectory,
                "archive-sources.json"),
            "{ damaged json");

        using var store = CreateStore();

        var sources = await store.LoadAsync();

        Assert.Empty(sources);
    }

    [Fact]
    public async Task LoadAsync_LegacySource_DefaultsToFolderNames()
    {
        Directory.CreateDirectory(_temporaryDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(
                _temporaryDirectory,
                "archive-sources.json"),
            """
            {
              "SchemaVersion": 1,
              "Sources": [
                {
                  "Id": "11111111-1111-1111-1111-111111111111",
                  "DisplayName": "Архив",
                  "FullPath": "C:\\Archive",
                  "SourceType": "LocalFolder",
                  "AddedAtUtc": "2026-01-01T00:00:00+00:00"
                }
              ]
            }
            """);

        using var store = CreateStore();

        var source = Assert.Single(await store.LoadAsync());

        Assert.Equal(
            ArchiveSourceIndexingMode.FolderNames,
            source.IndexingMode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(
                _temporaryDirectory,
                recursive: true);
        }
    }

    private JsonArchiveSourceStore CreateStore()
    {
        return new JsonArchiveSourceStore(
            new TestDirectoryProvider(_temporaryDirectory),
            NullLogger<JsonArchiveSourceStore>.Instance);
    }

    private sealed class TestDirectoryProvider(string directory)
        : IApplicationDataDirectoryProvider
    {
        public string GetApplicationDataDirectory()
        {
            return directory;
        }
    }
}
