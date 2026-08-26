using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.Thumbnails;
using VideoArchiveFinder.Desktop.Services;

namespace VideoArchiveFinder.Desktop.Views;

public sealed partial class CacheSettingsDialog : Window
{
    private readonly IThumbnailCacheService
        _thumbnailCacheService;

    private readonly IWindowsShellService
        _windowsShellService;

    private readonly ILogger<CacheSettingsDialog>
        _logger;

    private bool _isBusy;

    private string? _cacheDirectoryPath;

    public bool WasCacheCleared
    {
        get;
        private set;
    }

    public CacheSettingsDialog(
        IThumbnailCacheService thumbnailCacheService,
        IWindowsShellService windowsShellService,
        ILogger<CacheSettingsDialog> logger)
    {
        InitializeComponent();

        _thumbnailCacheService =
            thumbnailCacheService;

        _windowsShellService = windowsShellService;
        _logger = logger;
    }

    private async void CacheSettingsDialog_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= CacheSettingsDialog_Loaded;

        await RefreshCacheInfoAsync();
    }

    private async Task RefreshCacheInfoAsync()
    {
        try
        {
            SetBusy(true);

            var cacheInfo =
                await _thumbnailCacheService.GetInfoAsync();

            _cacheDirectoryPath =
                cacheInfo.DirectoryPath;

            CachePathText.Text =
                $"Путь: {cacheInfo.DirectoryPath}";

            CacheInfoText.Text =
                $"Размер: {FormatSizeBytes(cacheInfo.SizeBytes)}. " +
                $"Файлов: {cacheInfo.FileCount}.";
        }
        catch (Exception exception)
        {
            CacheInfoText.Text =
                "Не удалось получить сведения о кэше.";

            _logger.LogError(
                exception,
                "Could not load thumbnail cache info.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenFolderButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                _cacheDirectoryPath))
        {
            return;
        }

        try
        {
            _windowsShellService.OpenFolder(
                _cacheDirectoryPath);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Не удалось открыть папку кэша." +
                Environment.NewLine +
                Environment.NewLine +
                exception.Message,
                "Кэш миниатюр",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _logger.LogError(
                exception,
                "Could not open the thumbnail cache folder.");
        }
    }

    private async void ClearCacheButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            "Удалить все сохранённые миниатюры? " +
            "Они будут созданы заново по мере просмотра.",
            "Очистка кэша миниатюр",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            SetBusy(true);
            CacheInfoText.Text =
                "Очистка кэша...";

            var clearResult =
                await _thumbnailCacheService.ClearAsync();

            WasCacheCleared = true;

            MessageBox.Show(
                this,
                $"Кэш очищен. Удалено файлов: " +
                $"{clearResult.DeletedFileCount} " +
                $"({FormatSizeBytes(clearResult.DeletedSizeBytes)}).",
                "Очистка кэша миниатюр",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Не удалось очистить кэш миниатюр." +
                Environment.NewLine +
                Environment.NewLine +
                exception.Message,
                "Очистка кэша миниатюр",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _logger.LogError(
                exception,
                "Could not clear the thumbnail cache.");
        }
        finally
        {
            SetBusy(false);
        }

        await RefreshCacheInfoAsync();
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;

        OpenFolderButton.IsEnabled = !isBusy;
        ClearCacheButton.IsEnabled = !isBusy;
    }

    private static string FormatSizeBytes(
        long sizeBytes)
    {
        const long BytesPerKilobyte = 1024;
        const long BytesPerMegabyte = 1024 * 1024;
        const long BytesPerGigabyte = 1024 * 1024 * 1024;

        return sizeBytes switch
        {
            >= BytesPerGigabyte =>
                $"{sizeBytes / (double)BytesPerGigabyte:0.##} ГБ",
            >= BytesPerMegabyte =>
                $"{sizeBytes / (double)BytesPerMegabyte:0.##} МБ",
            >= BytesPerKilobyte =>
                $"{sizeBytes / (double)BytesPerKilobyte:0.##} КБ",
            _ => $"{sizeBytes} Б"
        };
    }
}
