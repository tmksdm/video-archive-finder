using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Desktop.Services;

namespace VideoArchiveFinder.Desktop.ViewModels;

public partial class FolderVideoFilesViewModel
    : ObservableObject, IDisposable
{
    private readonly IVideoFileIndexRepository
        _videoFileIndexRepository;

    private readonly IWindowsShellService
        _windowsShellService;

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
    private bool _isGridView = true;

    [ObservableProperty]
    private double _gridCardWidth = 240;

    public double GridPreviewHeight =>
        GridCardWidth * 9d / 16d;

    [ObservableProperty]
    private string _statusText =
        "Выберите папку в результатах поиска";


    public FolderVideoFilesViewModel(
        IVideoFileIndexRepository videoFileIndexRepository,
        IWindowsShellService windowsShellService,
        ILogger<FolderVideoFilesViewModel> logger)
    {
        _videoFileIndexRepository =
            videoFileIndexRepository;

        _windowsShellService =
            windowsShellService;

        _logger = logger;
    }

    public ObservableCollection<IndexedVideoFile> Files
    {
        get;
    } = [];

    public bool HasFiles => Files.Count > 0;
    public bool IsListView => !IsGridView;


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

    [RelayCommand]
    private void ShowGridView()
    {
        IsGridView = true;
    }

    [RelayCommand]
    private void ShowListView()
    {
        IsGridView = false;
    }

    partial void OnIsGridViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListView));
    }

    partial void OnGridCardWidthChanged(double value)
    {
        OnPropertyChanged(nameof(GridPreviewHeight));
    }


    [RelayCommand]
    private async Task OpenVideoAsync(
        IndexedVideoFile? videoFile)
    {
        if (videoFile is null)
        {
            return;
        }

        if (!videoFile.IsAvailable ||
            !await FileExistsAsync(videoFile.FullPath))
        {
            StatusText =
                $"Видеофайл недоступен: {videoFile.Name}";

            _logger.LogWarning(
                "Video file is unavailable: {VideoPath}.",
                videoFile.FullPath);

            return;
        }

        try
        {
            _windowsShellService.OpenFile(
                videoFile.FullPath);
        }
        catch (Exception exception)
        {
            StatusText =
                $"Не удалось открыть видеофайл: {videoFile.Name}";

            _logger.LogError(
                exception,
                "Could not open video file {VideoPath}.",
                videoFile.FullPath);
        }
    }

    private static Task<bool> FileExistsAsync(
        string filePath)
    {
        return Task.Run(
            () => File.Exists(filePath));
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
