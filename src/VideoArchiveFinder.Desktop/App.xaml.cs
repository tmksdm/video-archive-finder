using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;
using VideoArchiveFinder.Desktop.ViewModels;
using VideoArchiveFinder.Infrastructure;
using VideoArchiveFinder.Desktop.Services;

namespace VideoArchiveFinder.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;

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
                services.AddSingleton<IWindowsShellService, WindowsShellService>();
                services.AddSingleton<IClipboardService, WindowsClipboardService>();
                services.AddSingleton<ILocalFolderPicker, WindowsLocalFolderPicker>();
                services.AddSingleton<
                    IUncPathInputDialog,
                    WindowsUncPathInputDialog>();
                services.AddSingleton<
                    IArchiveSourceRemovalConfirmationDialog,
                    WindowsArchiveSourceRemovalConfirmationDialog>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        var viewModel =
            _host.Services.GetRequiredService<MainWindowViewModel>();

        await viewModel.InitializeAsync();

        var mainWindow =
            _host.Services.GetRequiredService<MainWindow>();

        MainWindow = mainWindow;
        mainWindow.Show();


        Log.Information("Video Archive Finder started");
    }

    protected override async void OnExit(System.Windows.ExitEventArgs e)
    {
        Log.Information("Video Archive Finder stopped");

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }
}

