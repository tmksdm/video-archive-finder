using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.VideoFiles;

namespace VideoArchiveFinder.Desktop.ViewModels;

public partial class FolderVideoFilesViewModel
    : ObservableObject, IDisposable
{
    private readonly IVideoFileIndexRepository
        _videoFileIndexRepository;

    private readonly ILogger<FolderVideoFilesViewModel>
        _logger;

    private CancellationTokenSource? _loadCancellation;
    private int _loadVersion;
    private bool _isDisposed;

    [ObservableProperty]
    private FolderSearchTreeNode? _selectedFolder;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText =
        "Выберите папку в результатах поиска";

    public FolderVideoFilesViewModel(
        IVideoFileIndexRepository videoFileIndexRepository,
        ILogger<FolderVideoFilesViewModel> logger)
    {
        _videoFileIndexRepository =
            videoFileIndexRepository;

        _logger = logger;
    }

    public ObservableCollection<IndexedVideoFile> Files
    {
        get;
    } = [];

    public bool HasFiles => Files.Count > 0;

    public async Task SelectFolderAsync(
        FolderSearchTreeNode? folder,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            _isDisposed,
            this);

        var version = Interlocked.Increment(
            ref _loadVersion);

        var currentCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        var previousCancellation = Interlocked.Exchange(
            ref _loadCancellation,
            currentCancellation);

        previousCancellation?.Cancel();

        SelectedFolder = folder;
        Files.Clear();
        OnPropertyChanged(nameof(HasFiles));

        if (folder is null)
        {
            StatusText =
                "Выберите папку в результатах поиска";

            CompleteLoad(
                version,
                currentCancellation);

            return;
        }

        if (!folder.IsAvailable)
        {
            StatusText = "Выбранная папка недоступна";

            CompleteLoad(
                version,
                currentCancellation);

            return;
        }

        IsLoading = true;
        StatusText = "Загрузка видеофайлов...";

        try
        {
            var files =
                await _videoFileIndexRepository
                    .GetByFolderPathAsync(
                        folder.RootSourceId,
                        folder.FullPath,
                        currentCancellation.Token);

            if (version != _loadVersion)
            {
                return;
            }

            foreach (var file in files)
            {
                Files.Add(file);
            }

            OnPropertyChanged(nameof(HasFiles));

            StatusText = files.Count == 0
                ? "В выбранной папке нет проиндексированных видеофайлов"
                : $"Найдено видеофайлов: {files.Count}";
        }
        catch (OperationCanceledException)
            when (currentCancellation.IsCancellationRequested)
        {
            // Отмена при выборе другой папки ожидаема.
        }
        catch (Exception exception)
        {
            if (version != _loadVersion)
            {
                return;
            }

            Files.Clear();
            OnPropertyChanged(nameof(HasFiles));

            StatusText =
                "Не удалось загрузить видеофайлы";

            _logger.LogError(
                exception,
                "Could not load video files for folder {FolderPath}.",
                folder.FullPath);
        }
        finally
        {
            CompleteLoad(
                version,
                currentCancellation);
        }
    }

    private void CompleteLoad(
        int version,
        CancellationTokenSource cancellation)
    {
        if (version == _loadVersion)
        {
            IsLoading = false;
        }

        Interlocked.CompareExchange(
            ref _loadCancellation,
            null,
            cancellation);

        cancellation.Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        Interlocked.Increment(
            ref _loadVersion);

        var cancellation = Interlocked.Exchange(
            ref _loadCancellation,
            null);

        cancellation?.Cancel();
    }
}
