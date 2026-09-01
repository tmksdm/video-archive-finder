using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Indexing;

namespace VideoArchiveFinder.Infrastructure.Indexing;

public sealed class SqliteFolderIndexRepository
    : IFolderIndexRepository
{
    private readonly IndexDatabasePathProvider _databasePathProvider;

    private readonly ILogger<SqliteFolderIndexRepository> _logger;

    public SqliteFolderIndexRepository(
        IndexDatabasePathProvider databasePathProvider,
        ILogger<SqliteFolderIndexRepository> logger)
    {
        _databasePathProvider = databasePathProvider;
        _logger = logger;
    }

    public async Task UpsertBatchAsync(
        IReadOnlyCollection<FolderIndexUpsertItem> folders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folders);

        if (folders.Count == 0)
        {
            return;
        }

        foreach (var folder in folders)
        {
            ValidateFolder(folder);
        }

        await using var connection = CreateConnection();

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = connection.BeginTransaction();

        try
        {
            await UpsertFoldersAsync(
                    connection,
                    transaction,
                    folders,
                    cancellationToken)
                .ConfigureAwait(false);

            await UpdateParentRelationshipsAsync(
                    connection,
                    transaction,
                    folders,
                    cancellationToken)
                .ConfigureAwait(false);

            await transaction
                .CommitAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Upserted {FolderCount} folders into the index.",
                folders.Count);
        }
        catch (OperationCanceledException)
        {
            await transaction
                .RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Folder index batch upsert was cancelled.");

            throw;
        }
        catch (Exception exception)
        {
            await transaction
                .RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);

            _logger.LogError(
                exception,
                "Failed to upsert {FolderCount} folders into the index.",
                folders.Count);

            throw;
        }
    }

    public async Task<int> CompleteScanAsync(
        Guid rootSourceId,
        DateTimeOffset scanStartedAtUtc,
        IReadOnlyCollection<string> protectedPaths,
        CancellationToken cancellationToken = default)
    {
        if (rootSourceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Root source identifier cannot be empty.",
                nameof(rootSourceId));
        }

        ArgumentNullException.ThrowIfNull(protectedPaths);

        var normalizedProtectedPaths =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var protectedPath in protectedPaths)
        {
            if (string.IsNullOrWhiteSpace(protectedPath))
            {
                throw new ArgumentException(
                    "Protected paths cannot contain an empty path.",
                    nameof(protectedPaths));
            }

            normalizedProtectedPaths.Add(
                protectedPath.Trim());
        }

        await using var connection = CreateConnection();

        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await ConfigureConnectionAsync(
                connection,
                cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = connection.BeginTransaction();

        try
        {
            var staleFolders =
                new List<(long Id, string FullPath)>();

            await using (var selectCommand =
                connection.CreateCommand())
            {
                selectCommand.Transaction = transaction;

                selectCommand.CommandText =
                    """
    SELECT Id, FullPath
    FROM Folders
    WHERE RootSourceId = $rootSourceId
      AND julianday(LastSeenUtc) <
          julianday($scanStartedAtUtc)
    ORDER BY Id;
    """;


                selectCommand.Parameters.AddWithValue(
                    "$rootSourceId",
                    rootSourceId.ToString("D"));

                selectCommand.Parameters.AddWithValue(
                    "$scanStartedAtUtc",
                    scanStartedAtUtc
                        .ToUniversalTime()
                        .ToString(
                            "O",
                            CultureInfo.InvariantCulture));

                await using var reader =
                    await selectCommand
                        .ExecuteReaderAsync(cancellationToken)
                        .ConfigureAwait(false);

                while (await reader
                    .ReadAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    staleFolders.Add(
                        (
                            reader.GetInt64(0),
                            reader.GetString(1)
                        ));
                }
            }

            var folderIdsToDelete =
                staleFolders
                    .Where(
                        folder =>
                            !IsRelatedToProtectedPath(
                                folder.FullPath,
                                normalizedProtectedPaths))
                    .Select(folder => folder.Id)
                    .ToArray();

            const int deleteBatchSize = 250;

            for (var offset = 0;
                 offset < folderIdsToDelete.Length;
                 offset += deleteBatchSize)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                var currentBatch =
                    folderIdsToDelete
                        .Skip(offset)
                        .Take(deleteBatchSize)
                        .ToArray();

                await using var deleteCommand =
                    connection.CreateCommand();

                deleteCommand.Transaction = transaction;

                var parameterNames =
                    new string[currentBatch.Length];

                for (var index = 0;
                     index < currentBatch.Length;
                     index++)
                {
                    var parameterName =
                        $"$folderId{index}";

                    parameterNames[index] =
                        parameterName;

                    deleteCommand.Parameters.AddWithValue(
                        parameterName,
                        currentBatch[index]);
                }

                deleteCommand.CommandText =
                    $"""
                    DELETE FROM Folders
                    WHERE Id IN
                        ({string.Join(", ", parameterNames)});
                    """;

                await deleteCommand
                    .ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction
                .CommitAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Completed folder index scan for source " +
                "{RootSourceId}. Removed {RemovedFolderCount} " +
                "stale folders; protected paths: " +
                "{ProtectedPathCount}.",
                rootSourceId,
                folderIdsToDelete.Length,
                normalizedProtectedPaths.Count);

            return folderIdsToDelete.Length;
        }
        catch (OperationCanceledException)
        {
            await transaction
                .RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Folder index scan completion was cancelled " +
                "for source {RootSourceId}.",
                rootSourceId);

            throw;
        }
        catch (Exception exception)
        {
            await transaction
                .RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);

            _logger.LogError(
                exception,
                "Failed to complete folder index scan " +
                "for source {RootSourceId}.",
                rootSourceId);

            throw;
        }

        static bool IsRelatedToProtectedPath(
            string folderPath,
            IReadOnlyCollection<string> paths)
        {
            foreach (var protectedPath in paths)
            {
                if (IsSameOrDescendant(
                        folderPath,
                        protectedPath) ||
                    IsSameOrDescendant(
                        protectedPath,
                        folderPath))
                {
                    return true;
                }
            }

            return false;
        }

        static bool IsSameOrDescendant(
            string path,
            string possibleAncestor)
        {
            if (string.Equals(
                path,
                possibleAncestor,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!path.StartsWith(
                    possibleAncestor,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (possibleAncestor.EndsWith('\\') ||
                possibleAncestor.EndsWith('/'))
            {
                return true;
            }

            return path.Length > possibleAncestor.Length &&
                (path[possibleAncestor.Length] == '\\' ||
                 path[possibleAncestor.Length] == '/');
        }
    }

    public async Task<IReadOnlyList<IndexedFolder>>
        GetByRootSourceIdAsync(
            Guid rootSourceId,
            CancellationToken cancellationToken = default)
    {
        if (rootSourceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Root source identifier cannot be empty.",
                nameof(rootSourceId));
        }

        try
        {
            await using var connection = CreateConnection();

            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await ConfigureConnectionAsync(
                    connection,
                    cancellationToken)
                .ConfigureAwait(false);

            await using var command = connection.CreateCommand();

            command.CommandText =
                """
                SELECT
                    Id,
                    FullPath,
                    Name,
                    NormalizedName,
                    SearchTokens,
                    SearchStems,
                    ParentFolderId,
                    RootSourceId,
                    IsAvailable,
                    LastSeenUtc,
                    DirectSubfolderCount,
                    DirectVideoFileCount
                FROM Folders
                WHERE RootSourceId = $rootSourceId
                ORDER BY Id;
                """;

            command.Parameters.AddWithValue(
                "$rootSourceId",
                rootSourceId.ToString("D"));

            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            var folders = new List<IndexedFolder>();

            while (await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                folders.Add(ReadFolder(reader));
            }

            return folders;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Reading folders for source {RootSourceId} was cancelled.",
                rootSourceId);

            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to read folders for source {RootSourceId}.",
                rootSourceId);

            throw;
        }
    }

    public async Task<IReadOnlyList<IndexedFolder>>
        GetChildrenAsync(
            long parentFolderId,
            CancellationToken cancellationToken = default)
    {
        if (parentFolderId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parentFolderId));
        }

        try
        {
            await using var connection = CreateConnection();

            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            await ConfigureConnectionAsync(
                    connection,
                    cancellationToken)
                .ConfigureAwait(false);

            await using var command = connection.CreateCommand();

            command.CommandText =
                """
                SELECT
                    Id,
                    FullPath,
                    Name,
                    NormalizedName,
                    SearchTokens,
                    SearchStems,
                    ParentFolderId,
                    RootSourceId,
                    IsAvailable,
                    LastSeenUtc,
                    DirectSubfolderCount,
                    DirectVideoFileCount
                FROM Folders
                WHERE ParentFolderId = $parentFolderId
                ORDER BY Name COLLATE NOCASE, Id;
                """;

            command.Parameters.AddWithValue(
                "$parentFolderId",
                parentFolderId);

            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            var folders = new List<IndexedFolder>();

            while (await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                folders.Add(ReadFolder(reader));
            }

            return folders;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Reading child folders for parent {ParentFolderId} " +
                "was cancelled.",
                parentFolderId);

            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to read child folders for parent " +
                "{ParentFolderId}.",
                parentFolderId);

            throw;
        }
    }

    private static async Task UpsertFoldersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyCollection<FolderIndexUpsertItem> folders,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;

        command.CommandText =
            """
            INSERT INTO Folders
            (
                FullPath,
                Name,
                NormalizedName,
                SearchTokens,
                SearchStems,
                RootSourceId,
                IsAvailable,
                LastSeenUtc,
                DirectSubfolderCount,
                DirectVideoFileCount
            )
            VALUES
            (
                $fullPath,
                $name,
                $normalizedName,
                $searchTokens,
                $searchStems,
                $rootSourceId,
                $isAvailable,
                $lastSeenUtc,
                $directSubfolderCount,
                $directVideoFileCount
            )
            ON CONFLICT(RootSourceId, FullPath)
            DO UPDATE SET
                Name = excluded.Name,
                NormalizedName = excluded.NormalizedName,
                SearchTokens = excluded.SearchTokens,
                SearchStems = excluded.SearchStems,
                IsAvailable = excluded.IsAvailable,
                LastSeenUtc = excluded.LastSeenUtc,
                DirectSubfolderCount =
                    excluded.DirectSubfolderCount,
                DirectVideoFileCount =
                    excluded.DirectVideoFileCount;
            """;

        var fullPathParameter =
            command.Parameters.Add("$fullPath", SqliteType.Text);

        var nameParameter =
            command.Parameters.Add("$name", SqliteType.Text);

        var normalizedNameParameter =
            command.Parameters.Add("$normalizedName", SqliteType.Text);

        var searchTokensParameter =
            command.Parameters.Add("$searchTokens", SqliteType.Text);

        var searchStemsParameter =
            command.Parameters.Add("$searchStems", SqliteType.Text);

        var rootSourceIdParameter =
            command.Parameters.Add("$rootSourceId", SqliteType.Text);

        var isAvailableParameter =
            command.Parameters.Add("$isAvailable", SqliteType.Integer);

        var lastSeenUtcParameter =
            command.Parameters.Add("$lastSeenUtc", SqliteType.Text);

        var directSubfolderCountParameter =
            command.Parameters.Add(
                "$directSubfolderCount",
                SqliteType.Integer);

        var directVideoFileCountParameter =
            command.Parameters.Add(
                "$directVideoFileCount",
                SqliteType.Integer);

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            fullPathParameter.Value = folder.FullPath;
            nameParameter.Value = folder.Name;
            normalizedNameParameter.Value = folder.NormalizedName;
            searchTokensParameter.Value = folder.SearchTokens;
            searchStemsParameter.Value = folder.SearchStems;

            rootSourceIdParameter.Value =
                folder.RootSourceId.ToString("D");

            isAvailableParameter.Value =
                folder.IsAvailable ? 1 : 0;

            lastSeenUtcParameter.Value =
                folder.LastSeenUtc.ToString(
                    "O",
                    CultureInfo.InvariantCulture);

            directSubfolderCountParameter.Value =
                folder.DirectSubfolderCount;

            directVideoFileCountParameter.Value =
                folder.DirectVideoFileCount;

            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task UpdateParentRelationshipsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyCollection<FolderIndexUpsertItem> folders,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;

        command.CommandText =
            """
            UPDATE Folders
            SET ParentFolderId =
                CASE
                    WHEN $parentFullPath IS NULL
                    THEN NULL
                    ELSE
                    (
                        SELECT parent.Id
                        FROM Folders AS parent
                        WHERE parent.RootSourceId = $rootSourceId
                          AND parent.FullPath = $parentFullPath
                        LIMIT 1
                    )
                END
            WHERE RootSourceId = $rootSourceId
              AND FullPath = $fullPath;
            """;

        var parentFullPathParameter =
            command.Parameters.Add(
                "$parentFullPath",
                SqliteType.Text);

        var rootSourceIdParameter =
            command.Parameters.Add(
                "$rootSourceId",
                SqliteType.Text);

        var fullPathParameter =
            command.Parameters.Add(
                "$fullPath",
                SqliteType.Text);

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            parentFullPathParameter.Value =
                folder.ParentFullPath is null
                    ? DBNull.Value
                    : folder.ParentFullPath;

            rootSourceIdParameter.Value =
                folder.RootSourceId.ToString("D");

            fullPathParameter.Value = folder.FullPath;

            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private SqliteConnection CreateConnection()
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePathProvider.GetDatabasePath(),
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
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """;

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static IndexedFolder ReadFolder(
        SqliteDataReader reader)
    {
        var rootSourceIdText = reader.GetString(7);

        if (!Guid.TryParse(rootSourceIdText, out var rootSourceId))
        {
            throw new InvalidDataException(
                $"Invalid root source identifier: " +
                $"{rootSourceIdText}.");
        }

        var lastSeenUtc = DateTimeOffset.Parse(
            reader.GetString(9),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

        return new IndexedFolder(
            Id: reader.GetInt64(0),
            FullPath: reader.GetString(1),
            Name: reader.GetString(2),
            NormalizedName: reader.GetString(3),
            SearchTokens: reader.GetString(4),
            SearchStems: reader.GetString(5),
            ParentFolderId: reader.IsDBNull(6)
                ? null
                : reader.GetInt64(6),
            RootSourceId: rootSourceId,
            IsAvailable: reader.GetInt64(8) != 0,
            LastSeenUtc: lastSeenUtc,
            DirectSubfolderCount: reader.GetInt32(10),
            DirectVideoFileCount: reader.GetInt32(11));
    }

    private static void ValidateFolder(
        FolderIndexUpsertItem folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (string.IsNullOrWhiteSpace(folder.FullPath))
        {
            throw new ArgumentException(
                "Folder path cannot be empty.",
                nameof(folder));
        }

        if (string.IsNullOrWhiteSpace(folder.Name))
        {
            throw new ArgumentException(
                "Folder name cannot be empty.",
                nameof(folder));
        }

        if (string.IsNullOrWhiteSpace(folder.NormalizedName))
        {
            throw new ArgumentException(
                "Normalized folder name cannot be empty.",
                nameof(folder));
        }

        if (folder.SearchTokens is null)
        {
            throw new ArgumentException(
                "Search tokens cannot be null.",
                nameof(folder));
        }

        if (folder.SearchStems is null)
        {
            throw new ArgumentException(
                "Search stems cannot be null.",
                nameof(folder));
        }

        if (folder.RootSourceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Root source identifier cannot be empty.",
                nameof(folder));
        }

        if (folder.DirectSubfolderCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(folder),
                "Direct subfolder count cannot be negative.");
        }

        if (folder.DirectVideoFileCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(folder),
                "Direct video file count cannot be negative.");
        }

        if (string.Equals(
            folder.FullPath,
            folder.ParentFullPath,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Folder cannot be its own parent.",
                nameof(folder));
        }
    }
}
