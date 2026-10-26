using Microsoft.Data.Sqlite;

namespace VideoArchiveFinder.Tests;

internal static class SqliteTestDatabasePools
{
    public static void ClearForTemporaryDirectory(
        string temporaryDirectory)
    {
        var databasePath =
            Path.Combine(
                temporaryDirectory,
                "Index",
                "video-archive-finder.db");

        ClearPool(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath
            });

        ClearPool(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            });

        ClearPool(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true
            });

        ClearPool(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
                Pooling = true
            });
    }

    private static void ClearPool(
        SqliteConnectionStringBuilder connectionStringBuilder)
    {
        using var connection =
            new SqliteConnection(
                connectionStringBuilder.ToString());

        SqliteConnection.ClearPool(connection);
    }
}
