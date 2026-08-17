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
                    HasVideoStream =
                        CASE
                            WHEN
                                VideoFiles.SizeBytes =
                                    excluded.SizeBytes
                                AND
                                VideoFiles.LastWriteTimeUtc =
                                    excluded.LastWriteTimeUtc
                            THEN VideoFiles.HasVideoStream
                            ELSE NULL
                        END,
                    DurationTicks =
                        CASE
                            WHEN
                                VideoFiles.SizeBytes =
                                    excluded.SizeBytes
                                AND
                                VideoFiles.LastWriteTimeUtc =
                                    excluded.LastWriteTimeUtc
                            THEN VideoFiles.DurationTicks
                            ELSE NULL
                        END,
                    Width =
                        CASE
                            WHEN
                                VideoFiles.SizeBytes =
                                    excluded.SizeBytes
                                AND
                                VideoFiles.LastWriteTimeUtc =
                                    excluded.LastWriteTimeUtc
                            THEN VideoFiles.Width
                            ELSE NULL
                        END,
                    Height =
                        CASE
                            WHEN
                                VideoFiles.SizeBytes =
                                    excluded.SizeBytes
                                AND
                                VideoFiles.LastWriteTimeUtc =
                                    excluded.LastWriteTimeUtc
                            THEN VideoFiles.Height
                            ELSE NULL
                        END,
                    Codec =
                        CASE
                            WHEN
                                VideoFiles.SizeBytes =
                                    excluded.SizeBytes
                                AND
                                VideoFiles.LastWriteTimeUtc =
                                    excluded.LastWriteTimeUtc
                            THEN VideoFiles.Codec
                            ELSE NULL
                        END,
                    AnalysisState =
                        CASE
                            WHEN
                                VideoFiles.SizeBytes =
                                    excluded.SizeBytes
                                AND
                                VideoFiles.LastWriteTimeUtc =
                                    excluded.LastWriteTimeUtc
                            THEN VideoFiles.AnalysisState
                            ELSE 0
                        END,
                    ThumbnailState =
                        CASE
                            WHEN
                                VideoFiles.SizeBytes =
                                    excluded.SizeBytes
                                AND
                                VideoFiles.LastWriteTimeUtc =
                                    excluded.LastWriteTimeUtc
                            THEN VideoFiles.ThumbnailState
                            ELSE 0
                        END,
                    ThumbnailPath =
                        CASE
                            WHEN
                                VideoFiles.SizeBytes =
                                    excluded.SizeBytes
                                AND
                                VideoFiles.LastWriteTimeUtc =
                                    excluded.LastWriteTimeUtc
                            THEN VideoFiles.ThumbnailPath
                            ELSE NULL
                        END,
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

    public async Task<bool> UpdateAnalysisAsync(
        VideoFileAnalysisUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        ValidateAnalysisUpdate(update);

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
        UPDATE VideoFiles
        SET
            HasVideoStream = $hasVideoStream,
            DurationTicks = $durationTicks,
            Width = $width,
            Height = $height,
            Codec = $codec,
            AnalysisState = $analysisState
        WHERE RootSourceId = $rootSourceId
          AND FullPath = $fullPath;
        """;

        var hasVideoStreamParameter =
            command.Parameters.Add(
                "$hasVideoStream",
                SqliteType.Integer);

        hasVideoStreamParameter.Value =
            update.HasVideoStream.HasValue
                ? update.HasVideoStream.Value ? 1 : 0
                : DBNull.Value;

        var durationTicksParameter =
            command.Parameters.Add(
                "$durationTicks",
                SqliteType.Integer);

        durationTicksParameter.Value =
            update.Duration.HasValue
                ? update.Duration.Value.Ticks
                : DBNull.Value;

        var widthParameter =
            command.Parameters.Add(
                "$width",
                SqliteType.Integer);

        widthParameter.Value =
            update.Width.HasValue
                ? update.Width.Value
                : DBNull.Value;

        var heightParameter =
            command.Parameters.Add(
                "$height",
                SqliteType.Integer);

        heightParameter.Value =
            update.Height.HasValue
                ? update.Height.Value
                : DBNull.Value;

        var codecParameter =
            command.Parameters.Add(
                "$codec",
                SqliteType.Text);

        codecParameter.Value =
            update.Codec is null
                ? DBNull.Value
                : update.Codec.Trim();

        command.Parameters.AddWithValue(
            "$analysisState",
            (int)update.State);

        command.Parameters.AddWithValue(
            "$rootSourceId",
            update.RootSourceId.ToString());

        command.Parameters.AddWithValue(
            "$fullPath",
            update.FullPath);

        var affectedRows =
            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

        if (affectedRows == 0)
        {
            _logger.LogWarning(
                "Video analysis result was not stored because " +
                "the indexed file was not found: {VideoPath}.",
                update.FullPath);
        }

        return affectedRows == 1;
    }

    public async Task<bool> UpdateThumbnailAsync(
        VideoFileThumbnailUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (update.RootSourceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Root source identifier cannot be empty.",
                nameof(update));
        }

        if (string.IsNullOrWhiteSpace(update.FullPath))
        {
            throw new ArgumentException(
                "Video file path cannot be empty.",
                nameof(update));
        }

        if (update.SizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(update),
                "Video file size cannot be negative.");
        }

        if (!Enum.IsDefined(update.State))
        {
            throw new ArgumentOutOfRangeException(
                nameof(update),
                "Thumbnail state is not supported.");
        }

        if (update.State ==
                VideoFileThumbnailState.Succeeded &&
            string.IsNullOrWhiteSpace(
                update.ThumbnailPath))
        {
            throw new ArgumentException(
                "Successful thumbnail update requires a path.",
                nameof(update));
        }

        if (update.State !=
                VideoFileThumbnailState.Succeeded &&
            update.ThumbnailPath is not null)
        {
            throw new ArgumentException(
                "Only a successful thumbnail update can have a path.",
                nameof(update));
        }

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
        UPDATE VideoFiles
        SET
            ThumbnailState = $thumbnailState,
            ThumbnailPath = $thumbnailPath
        WHERE
            RootSourceId = $rootSourceId
            AND FullPath = $fullPath
            AND SizeBytes = $sizeBytes
            AND LastWriteTimeUtc = $lastWriteTimeUtc;
        """;

        command.Parameters.AddWithValue(
            "$thumbnailState",
            (int)update.State);

        command.Parameters.AddWithValue(
            "$thumbnailPath",
            update.ThumbnailPath is null
                ? DBNull.Value
                : update.ThumbnailPath);

        command.Parameters.AddWithValue(
            "$rootSourceId",
            update.RootSourceId.ToString("D"));

        command.Parameters.AddWithValue(
            "$fullPath",
            update.FullPath);

        command.Parameters.AddWithValue(
            "$sizeBytes",
            update.SizeBytes);

        command.Parameters.AddWithValue(
            "$lastWriteTimeUtc",
            update.LastWriteTimeUtc.ToString(
                "O",
                CultureInfo.InvariantCulture));

        var affectedRows =
            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

        return affectedRows == 1;
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
                IsAvailable,
                HasVideoStream,
                DurationTicks,
                Width,
                Height,
                Codec,
                AnalysisState,
                ThumbnailState,
                ThumbnailPath
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
    Guid.Parse(reader.GetString(8)),
IsAvailable:
    reader.GetInt64(9) == 1,
HasVideoStream:
    reader.IsDBNull(10)
        ? null
        : reader.GetInt64(10) == 1,
Duration:
    reader.IsDBNull(11)
        ? null
        : TimeSpan.FromTicks(
            reader.GetInt64(11)),
Width:
    reader.IsDBNull(12)
        ? null
        : reader.GetInt32(12),
Height:
    reader.IsDBNull(13)
        ? null
        : reader.GetInt32(13),
Codec:
    reader.IsDBNull(14)
        ? null
        : reader.GetString(14),
AnalysisState:
    (VideoFileAnalysisState)
        reader.GetInt32(15),
ThumbnailState:
    (VideoFileThumbnailState)
        reader.GetInt32(16),
ThumbnailPath:
    reader.IsDBNull(17)
        ? null
        : reader.GetString(17)));

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

    private static void ValidateAnalysisUpdate(
        VideoFileAnalysisUpdate update)
    {
        if (update.RootSourceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Root source identifier cannot be empty.",
                nameof(update));
        }

        if (string.IsNullOrWhiteSpace(update.FullPath))
        {
            throw new ArgumentException(
                "Video file path cannot be empty.",
                nameof(update));
        }

        if (!Enum.IsDefined(update.State))
        {
            throw new ArgumentOutOfRangeException(
                nameof(update),
                "Unknown video analysis state.");
        }

        if (update.Duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(update),
                "Video duration cannot be negative.");
        }

        if (update.Width is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(update),
                "Video width must be positive.");
        }

        if (update.Height is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(update),
                "Video height must be positive.");
        }

        if (update.Codec is not null &&
            string.IsNullOrWhiteSpace(update.Codec))
        {
            throw new ArgumentException(
                "Video codec cannot be empty.",
                nameof(update));
        }

        if (update.State !=
                VideoFileAnalysisState.Succeeded &&
            HasAnyMetadata(update))
        {
            throw new ArgumentException(
                "Metadata can only be stored for a successful analysis.",
                nameof(update));
        }

        if (update.State ==
                VideoFileAnalysisState.Succeeded &&
            update.HasVideoStream is null)
        {
            throw new ArgumentException(
                "Successful analysis must specify whether a " +
                "video stream was found.",
                nameof(update));
        }

        if (update.HasVideoStream == false &&
            (update.Width is not null ||
             update.Height is not null ||
             update.Codec is not null))
        {
            throw new ArgumentException(
                "Stream metadata cannot be stored when no " +
                "video stream was found.",
                nameof(update));
        }
    }

    private static bool HasAnyMetadata(
        VideoFileAnalysisUpdate update)
    {
        return update.HasVideoStream is not null ||
               update.Duration is not null ||
               update.Width is not null ||
               update.Height is not null ||
               update.Codec is not null;
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
