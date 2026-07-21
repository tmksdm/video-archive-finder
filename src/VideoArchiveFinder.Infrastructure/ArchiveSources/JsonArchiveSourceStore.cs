using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.ArchiveSources;
using VideoArchiveFinder.Application.Storage;
using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Infrastructure.ArchiveSources;

public sealed class JsonArchiveSourceStore : IArchiveSourceStore, IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private const string FileName = "archive-sources.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly IApplicationDataDirectoryProvider _directoryProvider;
    private readonly ILogger<JsonArchiveSourceStore> _logger;
    private readonly SemaphoreSlim _accessLock = new(1, 1);

    public JsonArchiveSourceStore(
        IApplicationDataDirectoryProvider directoryProvider,
        ILogger<JsonArchiveSourceStore> logger)
    {
        _directoryProvider = directoryProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ArchiveSource>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var filePath = GetFilePath();

            if (!File.Exists(filePath))
            {
                return Array.Empty<ArchiveSource>();
            }

            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var document = await JsonSerializer.DeserializeAsync<StorageDocument>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (document is null)
            {
                _logger.LogWarning(
                    "Archive source storage file {FilePath} is empty.",
                    filePath);

                return Array.Empty<ArchiveSource>();
            }

            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                _logger.LogWarning(
                    "Unsupported archive source storage schema {SchemaVersion} in {FilePath}.",
                    document.SchemaVersion,
                    filePath);

                return Array.Empty<ArchiveSource>();
            }

            return document.Sources is null ? Array.Empty<ArchiveSource>() : document.Sources.AsReadOnly();
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "Archive source storage contains invalid JSON.");

            return Array.Empty<ArchiveSource>();
        }
        catch (IOException exception)
        {
            _logger.LogWarning(
                exception,
                "Archive source storage could not be read.");

            return Array.Empty<ArchiveSource>();
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(
                exception,
                "Access to archive source storage was denied.");

            return Array.Empty<ArchiveSource>();
        }
        finally
        {
            _accessLock.Release();
        }
    }

    public async Task SaveAsync(
        IEnumerable<ArchiveSource> sources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);

        await _accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        string? temporaryFilePath = null;

        try
        {
            var directoryPath =
                _directoryProvider.GetApplicationDataDirectory();

            Directory.CreateDirectory(directoryPath);

            var destinationFilePath = Path.Combine(
                directoryPath,
                FileName);

            temporaryFilePath = Path.Combine(
                directoryPath,
                $"{FileName}.{Guid.NewGuid():N}.tmp");

            var document = new StorageDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                Sources = sources.ToList()
            };

            await using (var stream = new FileStream(
                temporaryFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        document,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);

                await stream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(
                temporaryFilePath,
                destinationFilePath,
                overwrite: true);

            temporaryFilePath = null;

            _logger.LogInformation(
                "Saved {SourceCount} archive sources to {FilePath}.",
                document.Sources.Count,
                destinationFilePath);
        }
        finally
        {
            if (temporaryFilePath is not null)
            {
                TryDeleteTemporaryFile(temporaryFilePath);
            }

            _accessLock.Release();
        }
    }

    public void Dispose()
    {
        _accessLock.Dispose();
    }

    private string GetFilePath()
    {
        return Path.Combine(
            _directoryProvider.GetApplicationDataDirectory(),
            FileName);
    }

    private void TryDeleteTemporaryFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "Temporary archive source file {FilePath} could not be deleted.",
                filePath);
        }
    }

    private sealed class StorageDocument
    {
        public int SchemaVersion { get; init; }

        public List<ArchiveSource> Sources { get; init; } = [];
    }
}

