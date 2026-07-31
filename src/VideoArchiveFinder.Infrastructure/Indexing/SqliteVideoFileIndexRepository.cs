using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.VideoFiles;

namespace VideoArchiveFinder.Infrastructure.Indexing;

public sealed class SqliteVideoFileIndexRepository
    : IVideoFileIndexRepository
{
    private readonly IndexDatabasePathProvider
        _databasePathProvider;

    private readonly ILogger<SqliteVideoFileIndexRepository>
        _logger;

    public SqliteVideoFileIndexRepository(
        IndexDatabasePathProvider databasePathProvider,
        ILogger<SqliteVideoFileIndexRepository> logger)
    {
        _databasePathProvider = databasePathProvider;
        _logger = logger;
    }

    public async Task UpsertBatchAsync(
        IReadOnlyCollection<VideoFileIndexUpsertItem> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count == 0)
        {
            return;
        }

        foreach (var file in files)
        {
            ValidateFile(file);
        }

        await using var connection = CreateConnection();

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);

        await using var transaction =
            connection.BeginTransaction();

        try
        {
            await using var command =
                connection.CreateCommand();

            command.Transaction = transaction;

            command.CommandText =
                """
                INSERT INTO VideoFiles
                (
                    FullPath,
                    Name,
                    NormalizedName,
                    Extension,
                    SizeBytes,
                    LastWriteTimeUtc,
                    FolderFullPath,
                    RootSourceId,
                    IsAvailable,
                    LastSeenUtc
                )
                VALUES
                (
                    $fullPath,
                    $name,
                    $normalizedName,
                    $extension,
                    $sizeBytes,
                    $lastWriteTimeUtc,
                    $folderFullPath,
                    $rootSourceId,
                    $isAvailable,
                    $lastSeenUtc
                )
                ON CONFLICT
                (
                    RootSourceId,
                    FullPath
                )
                DO UPDATE SET
                    Name = excluded.Name,
                    NormalizedName = excluded.NormalizedName,
                    Extension = excluded.Extension,
                    SizeBytes = excluded.SizeBytes,
                    LastWriteTimeUtc =
                        excluded.LastWriteTimeUtc,
                    FolderFullPath =
                        excluded.FolderFullPath,
                    IsAvailable = excluded.IsAvailable,
                    LastSeenUtc = excluded.LastSeenUtc;
                """;

            var fullPathParameter =
                command.Parameters.Add(
                    "$fullPath",
                    SqliteType.Text);

            var nameParameter =
                command.Parameters.Add(
                    "$name",
                    SqliteType.Text);

            var normalizedNameParameter =
                command.Parameters.Add(
                    "$normalizedName",
                    SqliteType.Text);

            var extensionParameter =
                command.Parameters.Add(
                    "$extension",
                    SqliteType.Text);

            var sizeBytesParameter =
                command.Parameters.Add(
                    "$sizeBytes",
                    SqliteType.Integer);

            var lastWriteTimeUtcParameter =
                command.Parameters.Add(
                    "$lastWriteTimeUtc",
                    SqliteType.Text);

            var folderFullPathParameter =
                command.Parameters.Add(
                    "$folderFullPath",
                    SqliteType.Text);

            var rootSourceIdParameter =
                command.Parameters.Add(
                    "$rootSourceId",
                    SqliteType.Text);

            var isAvailableParameter =
                command.Parameters.Add(
                    "$isAvailable",
                    SqliteType.Integer);

            var lastSeenUtcParameter =
                command.Parameters.Add(
                    "$lastSeenUtc",
                    SqliteType.Text);

            foreach (var file in files)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                fullPathParameter.Value =
                    file.FullPath;

                nameParameter.Value =
                    file.Name;

                normalizedNameParameter.Value =
                    file.NormalizedName;

                extensionParameter.Value =
                    file.Extension;

                sizeBytesParameter.Value =
                    file.SizeBytes;

                lastWriteTimeUtcParameter.Value =
                    FormatTimestamp(
                        file.LastWriteTimeUtc);

                folderFullPathParameter.Value =
                    file.FolderFullPath;

                rootSourceIdParameter.Value =
                    file.RootSourceId.ToString();

                isAvailableParameter.Value =
                    file.IsAvailable ? 1 : 0;

                lastSeenUtcParameter.Value =
                    FormatTimestamp(
                        file.LastSeenUtc);

                await command
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction
                .CommitAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Upserted {VideoFileCount} video file candidates.",
                files.Count);
        }
        catch
        {
            await transaction
                .RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);

            throw;
        }
    }

    public async Task<int> CompleteFolderScanAsync(
        Guid rootSourceId,
        string folderFullPath,
        DateTimeOffset scanStartedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateFolderIdentity(
            rootSourceId,
            folderFullPath);

        await using var connection = CreateConnection();

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            DELETE FROM VideoFiles
            WHERE RootSourceId = $rootSourceId
              AND FolderFullPath = $folderFullPath
              AND LastSeenUtc < $scanStartedAtUtc;
            """;

        command.Parameters.AddWithValue(
            "$rootSourceId",
            rootSourceId.ToString());

        command.Parameters.AddWithValue(
            "$folderFullPath",
            folderFullPath);

        command.Parameters.AddWithValue(
            "$scanStartedAtUtc",
            FormatTimestamp(scanStartedAtUtc));

        var removedCount =
            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

        if (removedCount > 0)
        {
            _logger.LogInformation(
                "Removed {VideoFileCount} stale video file " +
                "candidates from folder {FolderPath}.",
                removedCount,
                folderFullPath);
        }

        return removedCount;
    }

    public async Task<IReadOnlyList<IndexedVideoFile>>
        GetByFolderPathAsync(
            Guid rootSourceId,
            string folderFullPath,
            CancellationToken cancellationToken = default)
    {
        ValidateFolderIdentity(
            rootSourceId,
            folderFullPath);

        await using var connection = CreateConnection();

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                Id,
                FullPath,
                Name,
                NormalizedName,
                Extension,
                SizeBytes,
                LastWriteTimeUtc,
                FolderFullPath,
                RootSourceId,
                IsAvailable
            FROM VideoFiles
            WHERE RootSourceId = $rootSourceId
              AND FolderFullPath = $folderFullPath
            ORDER BY
                Name COLLATE NOCASE,
                FullPath COLLATE NOCASE;
            """;

        command.Parameters.AddWithValue(
            "$rootSourceId",
            rootSourceId.ToString());

        command.Parameters.AddWithValue(
            "$folderFullPath",
            folderFullPath);

        await using var reader =
            await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

        var files = new List<IndexedVideoFile>();

        while (await reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            files.Add(
                new IndexedVideoFile(
                    Id: reader.GetInt64(0),
                    FullPath: reader.GetString(1),
                    Name: reader.GetString(2),
                    NormalizedName: reader.GetString(3),
                    Extension: reader.GetString(4),
                    SizeBytes: reader.GetInt64(5),
                    LastWriteTimeUtc:
                        ParseTimestamp(
                            reader.GetString(6)),
                    FolderFullPath:
                        reader.GetString(7),
                    RootSourceId:
                        Guid.Parse(
                            reader.GetString(8)),
                    IsAvailable:
                        reader.GetInt64(9) == 1));
        }

        return files;
    }

    private SqliteConnection CreateConnection()
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    _databasePathProvider.GetDatabasePath(),

                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true
            }.ToString();

        return new SqliteConnection(connectionString);
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """;

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string FormatTimestamp(
        DateTimeOffset timestamp)
    {
        return timestamp
            .ToUniversalTime()
            .ToString(
                "O",
                CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseTimestamp(
        string value)
    {
        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }

    private static void ValidateFolderIdentity(
        Guid rootSourceId,
        string folderFullPath)
    {
        if (rootSourceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Root source identifier cannot be empty.",
                nameof(rootSourceId));
        }

        if (string.IsNullOrWhiteSpace(folderFullPath))
        {
            throw new ArgumentException(
                "Folder path cannot be empty.",
                nameof(folderFullPath));
        }
    }

    private static void ValidateFile(
        VideoFileIndexUpsertItem file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (string.IsNullOrWhiteSpace(file.FullPath))
        {
            throw new ArgumentException(
                "Video file path cannot be empty.",
                nameof(file));
        }

        if (string.IsNullOrWhiteSpace(file.Name))
        {
            throw new ArgumentException(
                "Video file name cannot be empty.",
                nameof(file));
        }

        if (string.IsNullOrWhiteSpace(file.NormalizedName))
        {
            throw new ArgumentException(
                "Normalized video file name cannot be empty.",
                nameof(file));
        }

        if (string.IsNullOrWhiteSpace(file.Extension) ||
            !file.Extension.StartsWith(
                ".",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Video file extension must start with a dot.",
                nameof(file));
        }

        if (file.SizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(file),
                "Video file size cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(
            file.FolderFullPath))
        {
            throw new ArgumentException(
                "Folder path cannot be empty.",
                nameof(file));
        }

        if (file.RootSourceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Root source identifier cannot be empty.",
                nameof(file));
        }
    }
}
