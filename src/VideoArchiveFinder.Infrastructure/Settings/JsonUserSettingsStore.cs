using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Settings;
using VideoArchiveFinder.Application.Storage;

namespace VideoArchiveFinder.Infrastructure.Settings;

public sealed class JsonUserSettingsStore
    : IUserSettingsStore, IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private const string FileName = "user-settings.json";

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

    private readonly IApplicationDataDirectoryProvider
        _directoryProvider;

    private readonly ILogger<JsonUserSettingsStore>
        _logger;

    private readonly SemaphoreSlim _accessLock = new(1, 1);

    public JsonUserSettingsStore(
        IApplicationDataDirectoryProvider directoryProvider,
        ILogger<JsonUserSettingsStore> logger)
    {
        _directoryProvider = directoryProvider;
        _logger = logger;
    }

    public async Task<UserSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _accessLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var filePath = GetFilePath();

            if (!File.Exists(filePath))
            {
                return new UserSettings();
            }

            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);

            var document =
                await JsonSerializer
                    .DeserializeAsync<StorageDocument>(
                        stream,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (document is null)
            {
                _logger.LogWarning(
                    "User settings file {FilePath} is empty.",
                    filePath);

                return new UserSettings();
            }

            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                _logger.LogWarning(
                    "Unsupported user settings schema {SchemaVersion} in {FilePath}.",
                    document.SchemaVersion,
                    filePath);

                return new UserSettings();
            }

            return (document.Settings ?? new UserSettings())
                .Normalize();
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "User settings contain invalid JSON.");

            return new UserSettings();
        }
        catch (IOException exception)
        {
            _logger.LogWarning(
                exception,
                "User settings could not be read.");

            return new UserSettings();
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(
                exception,
                "Access to user settings was denied.");

            return new UserSettings();
        }
        finally
        {
            _accessLock.Release();
        }
    }

    public async Task SaveAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _accessLock
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        string? temporaryFilePath = null;

        try
        {
            var directoryPath =
                _directoryProvider
                    .GetApplicationDataDirectory();

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
                Settings = settings.Normalize()
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

                await stream
                    .FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(
                temporaryFilePath,
                destinationFilePath,
                overwrite: true);

            temporaryFilePath = null;

            _logger.LogInformation(
                "Saved user settings to {FilePath}.",
                destinationFilePath);
        }
        finally
        {
            if (temporaryFilePath is not null)
            {
                TryDeleteTemporaryFile(
                    temporaryFilePath);
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
            _directoryProvider
                .GetApplicationDataDirectory(),
            FileName);
    }

    private void TryDeleteTemporaryFile(
        string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "Temporary user settings file {FilePath} could not be deleted.",
                filePath);
        }
    }

    private sealed class StorageDocument
    {
        public int SchemaVersion
        {
            get;
            init;
        }

        public UserSettings? Settings
        {
            get;
            init;
        }
    }
}
