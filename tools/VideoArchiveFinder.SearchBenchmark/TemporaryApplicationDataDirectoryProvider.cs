using Microsoft.Data.Sqlite;
using VideoArchiveFinder.Application.Storage;

namespace VideoArchiveFinder.SearchBenchmark;

internal sealed class TemporaryApplicationDataDirectoryProvider
    : IApplicationDataDirectoryProvider,
      IDisposable
{
    private readonly string _directoryPath =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.SearchBenchmark",
            Guid.NewGuid().ToString("N"));

    public string GetApplicationDataDirectory()
    {
        return _directoryPath;
    }

    public void Dispose()
    {
        var databasePath = Path.Combine(
            _directoryPath,
            "Index",
            "video-archive-finder.db");

        ClearPool(
            databasePath,
            SqliteOpenMode.ReadOnly);

        ClearPool(
            databasePath,
            SqliteOpenMode.ReadWriteCreate);

        if (!Directory.Exists(_directoryPath))
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(
                    _directoryPath,
                    recursive: true);

                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
            catch (IOException)
            {
                Console.WriteLine(
                    $"Не удалось удалить временную папку: {_directoryPath}");
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine(
                    $"Нет доступа для удаления временной папки: {_directoryPath}");
            }
        }
    }

    private static void ClearPool(
        string databasePath,
        SqliteOpenMode mode)
    {
        var connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = mode,
                Cache = SqliteCacheMode.Shared,
                Pooling = true
            }.ToString();

        using var connection =
            new SqliteConnection(connectionString);

        SqliteConnection.ClearPool(connection);
    }
}
