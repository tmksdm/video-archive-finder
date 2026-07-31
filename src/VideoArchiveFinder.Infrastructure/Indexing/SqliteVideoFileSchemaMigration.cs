using Microsoft.Data.Sqlite;

namespace VideoArchiveFinder.Infrastructure.Indexing;

internal static class SqliteVideoFileSchemaMigration
{
    public static async Task MigrateFromVersion2ToVersion3Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var transaction =
            connection.BeginTransaction();

        try
        {
            await using var command =
                connection.CreateCommand();

            command.Transaction = transaction;

            command.CommandText =
                """
                CREATE TABLE VideoFiles
                (
                    Id INTEGER NOT NULL
                        CONSTRAINT PK_VideoFiles
                        PRIMARY KEY AUTOINCREMENT,

                    FullPath TEXT NOT NULL COLLATE NOCASE,
                    Name TEXT NOT NULL,
                    NormalizedName TEXT NOT NULL,
                    Extension TEXT NOT NULL COLLATE NOCASE,

                    SizeBytes INTEGER NOT NULL
                        CHECK (SizeBytes >= 0),

                    LastWriteTimeUtc TEXT NOT NULL,
                    FolderFullPath TEXT NOT NULL COLLATE NOCASE,
                    RootSourceId TEXT NOT NULL,

                    IsAvailable INTEGER NOT NULL
                        CHECK (IsAvailable IN (0, 1)),

                    LastSeenUtc TEXT NOT NULL
                );

                CREATE UNIQUE INDEX
                    UX_VideoFiles_RootSourceId_FullPath
                ON VideoFiles
                (
                    RootSourceId,
                    FullPath
                );

                CREATE INDEX
                    IX_VideoFiles_RootSourceId_FolderFullPath
                ON VideoFiles
                (
                    RootSourceId,
                    FolderFullPath
                );

                CREATE INDEX
                    IX_VideoFiles_NormalizedName
                ON VideoFiles
                (
                    NormalizedName
                );

                PRAGMA user_version = 3;
                """;

            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            await transaction
                .CommitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await transaction
                .RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);

            throw;
        }
    }
}
