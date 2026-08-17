using Microsoft.Data.Sqlite;

namespace VideoArchiveFinder.Infrastructure.Indexing;

internal static class SqliteVideoThumbnailSchemaMigration
{
    public static async Task MigrateFromVersion4ToVersion5Async(
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
                ALTER TABLE VideoFiles
                ADD COLUMN ThumbnailState INTEGER NOT NULL
                    DEFAULT 0
                    CHECK
                    (
                        ThumbnailState IN (0, 1, 2, 3)
                    );

                ALTER TABLE VideoFiles
                ADD COLUMN ThumbnailPath TEXT NULL;

                PRAGMA user_version = 5;
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
