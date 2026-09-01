using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;
using VideoArchiveFinder.Desktop.ViewModels;
using VideoArchiveFinder.Infrastructure;
using VideoArchiveFinder.Desktop.Services;
using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Application.ExternalTools;
using VideoArchiveFinder.Application.Thumbnails;


namespace VideoArchiveFinder.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private CancellationTokenSource?
        _cacheMaintenanceCancellation;
    private Task? _cacheMaintenanceTask;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var applicationDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoArchiveFinder");

        var logsPath = Path.Combine(applicationDataPath, "Logs");
        Directory.CreateDirectory(logsPath);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logsPath, "video-archive-finder-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddVideoArchiveFinderInfrastructure();
                services.AddSingleton<
                    IThumbnailImageLoader,
                    WpfThumbnailImageLoader>();
                services.AddSingleton<IWindowsShellService, WindowsShellService>();
                services.AddSingleton<IClipboardService, WindowsClipboardService>();
                services.AddSingleton<
    IAppThemeService,
    AppThemeService>();
                services.AddSingleton<ILocalFolderPicker, WindowsLocalFolderPicker>();
                services.AddSingleton<
                    IUncPathInputDialog,
                    WindowsUncPathInputDialog>();
                services.AddSingleton<
                    IArchiveSourceRemovalConfirmationDialog,
                    WindowsArchiveSourceRemovalConfirmationDialog>();
                services.AddSingleton<FolderSearchViewModel>();
                services.AddSingleton<FolderVideoFilesViewModel>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        var ffmpegToolsLocator =
            _host.Services.GetRequiredService<
                IFfmpegToolsLocator>();

        var ffmpegToolsStatus =
            ffmpegToolsLocator.Locate();

        if (!ffmpegToolsStatus.IsReady)
        {
            Log.Warning(
                "FFmpeg tools are unavailable. {DiagnosticMessage}",
                ffmpegToolsStatus.DiagnosticMessage);

            System.Windows.MessageBox.Show(
                "Функции анализа видео и создания превью " +
                "временно недоступны.\n\n" +
                ffmpegToolsStatus.DiagnosticMessage,
                "Не найдены FFmpeg и FFprobe",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }

        var libVlcRuntimeLocator =
            _host.Services.GetRequiredService<
                ILibVlcRuntimeLocator>();

        var libVlcRuntimeStatus =
            libVlcRuntimeLocator.Locate();

        if (!libVlcRuntimeStatus.IsReady)
        {
            Log.Warning(
                "LibVLC is unavailable. {DiagnosticMessage}",
                libVlcRuntimeStatus.DiagnosticMessage);

            System.Windows.MessageBox.Show(
                libVlcRuntimeStatus.DiagnosticMessage + "\n\n" +
                "Поиск, список видео и статические миниатюры " +
                "продолжат работать.",
                "Hover-просмотр недоступен",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }


        var appThemeService =
            _host.Services.GetRequiredService<IAppThemeService>();

        await appThemeService.InitializeAsync();


        var indexDatabaseInitializer =
            _host.Services.GetRequiredService<IIndexDatabaseInitializer>();

        await indexDatabaseInitializer.InitializeAsync();

        var cacheMaintenanceService =
            _host.Services.GetRequiredService<
                IThumbnailCacheMaintenanceService>();

        var viewModel =
            _host.Services.GetRequiredService<MainWindowViewModel>();

        await viewModel.VideoFiles.LoadSettingsAsync();
        await viewModel.InitializeAsync();


        var mainWindow =
            _host.Services.GetRequiredService<MainWindow>();

        MainWindow = mainWindow;
        mainWindow.Show();

        _cacheMaintenanceCancellation = new();
        _cacheMaintenanceTask =
            RunCacheMaintenanceAsync(
                cacheMaintenanceService,
                _cacheMaintenanceCancellation.Token);


        Log.Information("Video Archive Finder started");
    }

    protected override async void OnExit(System.Windows.ExitEventArgs e)
    {
        Log.Information("Video Archive Finder stopped");

        if (_host is not null)
        {
            if (_cacheMaintenanceCancellation is not null)
            {
                await _cacheMaintenanceCancellation
                    .CancelAsync();
            }

            if (_cacheMaintenanceTask is not null)
            {
                await _cacheMaintenanceTask;
            }

            _cacheMaintenanceCancellation?.Dispose();

            var viewModel =
                _host.Services.GetService<MainWindowViewModel>();

            if (viewModel is not null)
            {
                await viewModel.VideoFiles.SaveSettingsAsync();
            }

            await _host.StopAsync();
            _host.Dispose();
        }


        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }

    private static async Task RunCacheMaintenanceAsync(
        IThumbnailCacheMaintenanceService
            cacheMaintenanceService,
        CancellationToken cancellationToken)
    {
        try
        {
            await cacheMaintenanceService
                .TrimAsync(
                    cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown is expected.
        }
        catch (Exception exception)
        {
            Log.Warning(
                exception,
                "Startup thumbnail cache maintenance failed.");
        }
    }
}

