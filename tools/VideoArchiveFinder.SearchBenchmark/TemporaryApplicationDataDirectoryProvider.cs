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
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_directoryPath))
        {
            return;
        }

        try
        {
            Directory.Delete(
                _directoryPath,
                recursive: true);
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
