using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Infrastructure.Indexing;
using VideoArchiveFinder.Infrastructure.Search;
using Xunit.Abstractions;

namespace VideoArchiveFinder.Tests.Search;

public sealed class LocalIndexAcceptanceTests(ITestOutputHelper output)
{
    private const string DataDirectoryVariable = "VAF_ACCEPTANCE_DATA_DIRECTORY";

    [ExistingIndexFact]
    public async Task ExistingIndex_AllMatchesReachableWithoutChangingDatabase()
    {
        var dataDirectory = Environment.GetEnvironmentVariable(DataDirectoryVariable)!;
        Assert.True(Path.IsPathFullyQualified(dataDirectory));
        var pathProvider = new IndexDatabasePathProvider(
            new ExistingDataDirectoryProvider(dataDirectory));
        var databasePath = pathProvider.GetDatabasePath();
        Assert.True(File.Exists(databasePath));
        var before = await HashAsync(databasePath);

        Guid[] videoSources;
        await using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT RootSourceId FROM VideoFiles;";
            await using var reader = await command.ExecuteReaderAsync();
            var sources = new List<Guid>();
            while (await reader.ReadAsync())
            {
                sources.Add(Guid.Parse(reader.GetString(0)));
            }

            videoSources = sources.ToArray();
        }

        using var service = new SqliteFolderSearchPageService(
            pathProvider, new TextNormalizationService(),
            new RussianSearchStemService(),
            NullLogger<SqliteFolderSearchPageService>.Instance);
        var page = await service.SearchFirstPageAsync(new FolderSearchQuery(
            "фестиваль", FolderSearchMode.Smart, 200, null, videoSources));
        var session = page.NextSession;

        try
        {
            var ids = new HashSet<long>();
            var pages = 0;
            var total = page.TotalMatchCount;
            var reportedFileFound = false;

            while (true)
            {
                pages++;
                Assert.Equal(total, page.TotalMatchCount);
                Assert.All(page.Matches, match => Assert.True(ids.Add(match.Id),
                    $"Duplicate match id {match.Id}"));
                reportedFileFound |= page.Matches.Any(match => match.IsVideoFile &&
                    match.Name.Equals(
                        "10_15_ФестивальПамятиЕфремова_Лабецкая_03-23.mxf",
                        StringComparison.OrdinalIgnoreCase));

                if (!page.HasMoreResults)
                {
                    break;
                }

                Assert.NotEmpty(page.Matches);
                page = await service.SearchNextPageAsync(page.NextSession);
            }

            await service.ValidateSessionAsync(session);
            Assert.Equal(total, ids.Count);
            Assert.Equal(total, page.LoadedMatchCount);
            Assert.Equal(before, await HashAsync(databasePath));
            output.WriteLine(
                $"Query=фестиваль; matches={total}; pages={pages}; " +
                $"reportedFileFound={reportedFileFound}; SHA256={Convert.ToHexString(before)}");
        }
        finally
        {
            service.ReleaseSession(session);
        }
    }

    private static async Task<byte[]> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await SHA256.HashDataAsync(stream);
    }

    private sealed class ExistingDataDirectoryProvider(string path)
        : IApplicationDataDirectoryProvider
    {
        public string GetApplicationDataDirectory() => path;
    }

    public sealed class ExistingIndexFactAttribute : FactAttribute
    {
        public ExistingIndexFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(DataDirectoryVariable)))
            {
                Skip = $"Opt-in read-only acceptance: set {DataDirectoryVariable}.";
            }
        }
    }
}
