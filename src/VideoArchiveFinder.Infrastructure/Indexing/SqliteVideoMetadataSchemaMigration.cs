using Microsoft.Data.Sqlite;

namespace VideoArchiveFinder.Infrastructure.Indexing;

internal static class SqliteVideoMetadataSchemaMigration
{
    public static async Task MigrateFromVersion3ToVersion4Async(
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
                ADD COLUMN HasVideoStream INTEGER NULL
                    CHECK
                    (
                        HasVideoStream IS NULL OR
                        HasVideoStream IN (0, 1)
                    );

                ALTER TABLE VideoFiles
                ADD COLUMN DurationTicks INTEGER NULL
                    CHECK
                    (
                        DurationTicks IS NULL OR
                        DurationTicks >= 0
                    );

                ALTER TABLE VideoFiles
                ADD COLUMN Width INTEGER NULL
                    CHECK
                    (
                        Width IS NULL OR
                        Width > 0
                    );

                ALTER TABLE VideoFiles
                ADD COLUMN Height INTEGER NULL
                    CHECK
                    (
                        Height IS NULL OR
                        Height > 0
                    );

                ALTER TABLE VideoFiles
                ADD COLUMN Codec TEXT NULL;

                ALTER TABLE VideoFiles
                ADD COLUMN AnalysisState INTEGER NOT NULL
                    DEFAULT 0
                    CHECK (AnalysisState IN (0, 1, 2));

                PRAGMA user_version = 4;
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
