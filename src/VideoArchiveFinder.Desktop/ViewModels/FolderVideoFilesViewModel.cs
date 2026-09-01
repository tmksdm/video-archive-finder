using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Application.Settings;
using VideoArchiveFinder.Application.Thumbnails;
using VideoArchiveFinder.Desktop.Services;

namespace VideoArchiveFinder.Desktop.ViewModels;

public partial class FolderVideoFilesViewModel
    : ObservableObject, IDisposable
{
    private readonly IVideoFolderRefreshService
        _videoFolderRefreshService;

    private readonly IFolderIndexRepository
        _folderIndexRepository;

    private readonly IThumbnailImageLoader
        _thumbnailImageLoader;

    private readonly IStaticThumbnailGenerationQueue
        _thumbnailGenerationQueue;

    private readonly IVideoFileAnalysisQueue
        _videoFileAnalysisQueue;

    private readonly IStaticThumbnailStateChangeSource
        _thumbnailStateChangeSource;

    private readonly IWindowsShellService
        _windowsShellService;

    private readonly IUserSettingsStore
        _userSettingsStore;

    private readonly ILogger<FolderVideoFilesViewModel>
        _logger;

    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource?
        _thumbnailLoadCancellation;
    private int _loadVersion;
    private bool _isDisposed;

    [ObservableProperty]
    private FolderSearchTreeNode? _selectedFolder;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isGridView = true;

    [ObservableProperty]
    private double _gridCardWidth =
        UserSettings.DefaultGridCardWidth;


    public double GridPreviewHeight =>
        GridCardWidth * 9d / 16d;

    [ObservableProperty]
    private string _statusText =
        "Выберите папку в результатах поиска";


    public FolderVideoFilesViewModel(
        IVideoFolderRefreshService videoFolderRefreshService,
        IFolderIndexRepository folderIndexRepository,
        IThumbnailImageLoader thumbnailImageLoader,
        IStaticThumbnailGenerationQueue
            thumbnailGenerationQueue,
        IVideoFileAnalysisQueue
            videoFileAnalysisQueue,
        IStaticThumbnailStateChangeSource
            thumbnailStateChangeSource,
        IWindowsShellService windowsShellService,
        IUserSettingsStore userSettingsStore,
        ILogger<FolderVideoFilesViewModel> logger)

    {
        _videoFolderRefreshService =
            videoFolderRefreshService;

        _folderIndexRepository =
            folderIndexRepository;

        _thumbnailImageLoader =
            thumbnailImageLoader;

        _thumbnailGenerationQueue =
            thumbnailGenerationQueue;

        _videoFileAnalysisQueue =
            videoFileAnalysisQueue;

        _thumbnailStateChangeSource =
            thumbnailStateChangeSource;

        _thumbnailStateChangeSource.StateChanged +=
            OnStaticThumbnailStateChanged;

        _windowsShellService =
            windowsShellService;

        _userSettingsStore =
            userSettingsStore;

        _logger = logger;

    }

    public async Task LoadSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings =
                await _userSettingsStore.LoadAsync(
                    cancellationToken);

            IsGridView =
                settings.VideoFilesViewMode ==
                VideoFilesViewMode.Grid;

            GridCardWidth =
                settings.GridCardWidth;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not load video view settings.");
        }
    }

    public async Task SaveSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var currentSettings =
                await _userSettingsStore.LoadAsync(
                    cancellationToken);

            var settings = currentSettings with
            {
                VideoFilesViewMode =
                    IsGridView
                        ? VideoFilesViewMode.Grid
                        : VideoFilesViewMode.List,

                GridCardWidth =
                    GridCardWidth
            };

            await _userSettingsStore.SaveAsync(
                settings,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not save video view settings.");
        }
    }



    public ObservableCollection<VideoFileCardViewModel> Files
    {
        get;
    } = [];

    public ObservableCollection<FolderSearchTreeNode> ChildFolders
    {
        get;
    } = [];

    public bool HasFiles => Files.Count > 0;
    public bool HasChildFolders => ChildFolders.Count > 0;
    public bool IsListView => !IsGridView;


    public async Task SelectFolderAsync(
        FolderSearchTreeNode? folder,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            _isDisposed,
            this);

        var thumbnailCancellationToken =
            ResetThumbnailLoading(
                cancellationToken);

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
        ChildFolders.Clear();
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(HasChildFolders));

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
        StatusText = "Загрузка содержимого папки...";

        try
        {
            var filesTask = _videoFolderRefreshService
                .RefreshAsync(
                    folder.RootSourceId,
                    folder.FullPath,
                    currentCancellation.Token);

            var childFoldersTask = _folderIndexRepository
                .GetChildrenAsync(
                    folder.Id,
                    currentCancellation.Token);

            await Task.WhenAll(
                filesTask,
                childFoldersTask);

            var refreshResult = await filesTask;
            var files = refreshResult.Files;
            var childFolders = await childFoldersTask;

            if (version != _loadVersion)
            {
                return;
            }

            var cardsToQueue =
                new List<VideoFileCardViewModel>();

            var cardsToAnalyze =
                new List<VideoFileCardViewModel>();

            foreach (var childFolder in childFolders)
            {
                ChildFolders.Add(
                    new FolderSearchTreeNode(
                        Id: childFolder.Id,
                        FullPath: childFolder.FullPath,
                        Name: childFolder.Name,
                        RootSourceId:
                            childFolder.RootSourceId,
                        IsAvailable:
                            childFolder.IsAvailable,
                        IsMatch: false,
                        NameSegments:
                        [
                            new FolderNameTextSegment(
                                childFolder.Name,
                                IsHighlighted: false)
                        ],
                        Children: [],
                        DirectSubfolderCount:
                            childFolder.DirectSubfolderCount,
                        DirectVideoFileCount:
                            childFolder.DirectVideoFileCount));
            }

            foreach (var file in files)
            {
                var card =
                    new VideoFileCardViewModel(file);

                Files.Add(card);

                if (card.ThumbnailState ==
                        VideoFileThumbnailState.Succeeded &&
                    !string.IsNullOrWhiteSpace(
                        card.ThumbnailPath))
                {
                    _ = LoadSavedThumbnailAsync(
                        card,
                        thumbnailCancellationToken);
                }

                if (card.ThumbnailState ==
                        VideoFileThumbnailState.NotGenerated &&
                    card.HasVideoStream == true &&
                    card.IsAvailable)
                {
                    cardsToQueue.Add(card);
                }

                if (card.AnalysisState ==
                        VideoFileAnalysisState.NotAnalyzed &&
                    card.IsAvailable)
                {
                    cardsToAnalyze.Add(card);
                }
            }


            if (cardsToQueue.Count > 0)
            {
                _ = QueueMissingThumbnailsAsync(
                    cardsToQueue,
                    thumbnailCancellationToken);
            }

            if (cardsToAnalyze.Count > 0)
            {
                _ = QueueVideoAnalysisAsync(
                    cardsToAnalyze,
                    thumbnailCancellationToken);
            }


            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(HasChildFolders));

            StatusText =
                $"Видеофайлов: {files.Count}; " +
                $"вложенных папок: {childFolders.Count}" +
                (refreshResult.ErrorCount > 0
                    ? $"; ошибок чтения: " +
                      $"{refreshResult.ErrorCount}"
                    : string.Empty);
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
            ChildFolders.Clear();
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(HasChildFolders));

            StatusText =
                "Не удалось загрузить содержимое папки";

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
    VideoFileCardViewModel? videoFile)

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

    private CancellationToken ResetThumbnailLoading(
        CancellationToken cancellationToken)
    {
        var current =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        var previous =
            Interlocked.Exchange(
                ref _thumbnailLoadCancellation,
                current);

        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        return current.Token;
    }

    private async Task LoadSavedThumbnailAsync(
        VideoFileCardViewModel card,
        CancellationToken cancellationToken)
    {
        try
        {
            var thumbnailPath =
                card.ThumbnailPath;

            if (string.IsNullOrWhiteSpace(
                thumbnailPath))
            {
                return;
            }

            card.IsThumbnailLoading = true;

            var decodePixelWidth =
                Math.Clamp(
                    (int)Math.Ceiling(
                        GridCardWidth * 1.5),
                    160,
                    480);

            var image =
                await _thumbnailImageLoader.LoadAsync(
                    thumbnailPath,
                    decodePixelWidth,
                    cancellationToken);

            cancellationToken
                .ThrowIfCancellationRequested();

            if (!Files.Contains(card))
            {
                return;
            }

            card.SetThumbnailImage(image);
        }
        catch (OperationCanceledException)
            when (cancellationToken
                .IsCancellationRequested)
        {
            // Выбрана другая папка или закрывается приложение.
        }
        catch (Exception exception)
        {
            if (Files.Contains(card))
            {
                card.SetThumbnailLoadFailure();
            }

            _logger.LogWarning(
                exception,
                "Could not load the saved thumbnail " +
                "for {VideoPath}.",
                card.FullPath);
        }
    }


    private async Task QueueVideoAnalysisAsync(
        IReadOnlyList<VideoFileCardViewModel> cards,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var card in cards)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                if (!Files.Contains(card) ||
                    card.AnalysisState !=
                        VideoFileAnalysisState.NotAnalyzed)
                {
                    continue;
                }

                await _videoFileAnalysisQueue.EnqueueAsync(
                    new VideoFileAnalysisRequest(
                        RootSourceId:
                            card.RootSourceId,
                        FullPath:
                            card.FullPath,
                        SizeBytes:
                            card.SizeBytes,
                        LastWriteTimeUtc:
                            card.LastWriteTimeUtc),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Выбрана другая папка или закрывается приложение.
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not enqueue video analysis " +
                "for the selected folder.");
        }
    }

    private async Task QueueMissingThumbnailsAsync(
        IReadOnlyList<VideoFileCardViewModel> cards,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var card in cards)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                if (!Files.Contains(card) ||
                    card.ThumbnailState !=
                        VideoFileThumbnailState.NotGenerated)
                {
                    continue;
                }

                await _thumbnailGenerationQueue.EnqueueAsync(
                    new StaticThumbnailRequest(
                        RootSourceId:
                            card.RootSourceId,
                        VideoPath:
                            card.FullPath,
                        SizeBytes:
                            card.SizeBytes,
                        LastWriteTimeUtc:
                            card.LastWriteTimeUtc),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Выбрана другая папка или закрывается приложение.
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not enqueue static thumbnails " +
                "for the selected folder.");
        }
    }


    private void OnStaticThumbnailStateChanged(
        object? sender,
        StaticThumbnailStateChangedEventArgs eventArgs)
    {
        if (_isDisposed)
        {
            return;
        }

        var dispatcher =
            System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null ||
            dispatcher.HasShutdownStarted ||
            dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            ApplyThumbnailStateChange(eventArgs);
            return;
        }

        _ = dispatcher.InvokeAsync(
            () => ApplyThumbnailStateChange(eventArgs));
    }

    private void ApplyThumbnailStateChange(
        StaticThumbnailStateChangedEventArgs eventArgs)
    {
        if (_isDisposed)
        {
            return;
        }

        var request = eventArgs.Request;

        var card = Files.FirstOrDefault(
            item =>
                item.RootSourceId ==
                    request.RootSourceId &&
                string.Equals(
                    item.FullPath,
                    request.VideoPath,
                    StringComparison.OrdinalIgnoreCase) &&
                item.SizeBytes ==
                    request.SizeBytes &&
                item.LastWriteTimeUtc ==
                    request.LastWriteTimeUtc);

        if (card is null)
        {
            return;
        }

        card.ApplyThumbnailState(
            eventArgs.State,
            eventArgs.ThumbnailPath);

        if (eventArgs.State !=
                VideoFileThumbnailState.Succeeded ||
            string.IsNullOrWhiteSpace(
                eventArgs.ThumbnailPath))
        {
            return;
        }

        var cancellation =
            _thumbnailLoadCancellation;

        if (cancellation is null ||
            cancellation.IsCancellationRequested)
        {
            return;
        }

        _ = LoadSavedThumbnailAsync(
            card,
            cancellation.Token);
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

        _thumbnailStateChangeSource.StateChanged -=
            OnStaticThumbnailStateChanged;

        Interlocked.Increment(
            ref _loadVersion);

        var cancellation = Interlocked.Exchange(
            ref _loadCancellation,
            null);

        cancellation?.Cancel();

        var thumbnailCancellation =
            Interlocked.Exchange(
                ref _thumbnailLoadCancellation,
                null);

        if (thumbnailCancellation is not null)
        {
            thumbnailCancellation.Cancel();
            thumbnailCancellation.Dispose();
        }
    }
}
