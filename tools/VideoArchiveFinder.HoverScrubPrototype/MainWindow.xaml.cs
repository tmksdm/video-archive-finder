using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.Win32;

namespace VideoArchiveFinder.HoverScrubPrototype;

public partial class MainWindow : Window
{
    private static readonly TimeSpan SeekInterval =
        TimeSpan.FromMilliseconds(40);

    private readonly DispatcherTimer _seekTimer;

    private LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;
    private Media? _activeMedia;

    private string? _selectedFilePath;
    private double? _pendingPosition;

    private int _sessionVersion;
    private bool _isHovering;
    private bool _isMediaReady;
    private bool _isDisposed;

    public MainWindow()
    {
        InitializeComponent();

        _seekTimer = new DispatcherTimer
        {
            Interval = SeekInterval
        };

        _seekTimer.Tick += SeekTimer_Tick;
        Closed += MainWindow_Closed;
    }

    private void SelectVideoButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите локальное тестовое видео",
            CheckFileExists = true,
            Multiselect = false,
            Filter =
                "Видеофайлы|" +
                "*.mp4;*.mov;*.mts;*.m2ts;*.avi;*.mkv;" +
                "*.mpeg;*.mpg;*.wmv;*.mxf|" +
                "Все файлы|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ReleaseActiveMedia();

        _selectedFilePath = dialog.FileName;

        SelectedFileText.Text = _selectedFilePath;
        VideoHintText.Text =
            "Наведите курсор и перемещайте его влево и вправо";

        PositionText.Text = "Позиция: —";
        StatusText.Text =
            "Видео выбрано. Проигрыватель откроет файл при наведении.";
    }

    private async void ScrubInputLayer_MouseEnter(
        object sender,
        MouseEventArgs e)
    {
        if (_isDisposed ||
            string.IsNullOrWhiteSpace(_selectedFilePath))
        {
            return;
        }

        if (!File.Exists(_selectedFilePath))
        {
            StatusText.Text = "Выбранный файл больше не существует.";
            return;
        }

        _isHovering = true;
        _isMediaReady = false;

        var width = ScrubInputLayer.ActualWidth;

        if (width > 0)
        {
            var mousePosition =
                e.GetPosition(ScrubInputLayer);

            _pendingPosition = Math.Clamp(
                mousePosition.X / width,
                0d,
                1d);
        }

        var sessionVersion = ++_sessionVersion;

        await OpenSelectedMediaAsync(sessionVersion);
    }

    private void ScrubInputLayer_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_isHovering ||
            string.IsNullOrWhiteSpace(_selectedFilePath))
        {
            return;
        }

        var width = ScrubInputLayer.ActualWidth;

        if (width <= 0)
        {
            return;
        }

        var mousePosition =
            e.GetPosition(ScrubInputLayer);

        _pendingPosition = Math.Clamp(
            mousePosition.X / width,
            0d,
            1d);

        if (_isMediaReady &&
            !_seekTimer.IsEnabled)
        {
            _seekTimer.Start();
        }
    }

    private void ScrubInputLayer_MouseLeave(
        object sender,
        MouseEventArgs e)
    {
        _isHovering = false;

        ReleaseActiveMedia();

        PositionText.Text = "Позиция: —";

        if (!string.IsNullOrWhiteSpace(_selectedFilePath))
        {
            VideoHintText.Text =
                "Наведите курсор и перемещайте его влево и вправо";

            StatusText.Text =
                "Просмотр остановлен. Файл освобождён.";
        }
    }

    private async Task OpenSelectedMediaAsync(
        int sessionVersion)
    {
        try
        {
            EnsurePlayerCreated();

            if (_libVlc is null ||
                _mediaPlayer is null ||
                string.IsNullOrWhiteSpace(_selectedFilePath))
            {
                return;
            }

            StatusText.Text = "Открытие видео…";
            VideoHintText.Text = string.Empty;

            var media = new Media(
                _libVlc,
                _selectedFilePath,
                FromType.FromPath);

            _activeMedia = media;
            _mediaPlayer.Media = media;
            _mediaPlayer.Mute = true;
            _mediaPlayer.Volume = 0;

            var started =
                await StartPlaybackAndWaitAsync(_mediaPlayer);

            if (!IsCurrentSession(sessionVersion))
            {
                return;
            }

            if (!started)
            {
                ReleaseActiveMedia();

                VideoHintText.Text =
                    "Не удалось открыть или декодировать видео";

                StatusText.Text =
                    "LibVLC не сообщил о начале воспроизведения.";

                return;
            }

            /*
             * Небольшая задержка позволяет LibVLC вывести первый кадр.
             * После этого проигрыватель остаётся на паузе.
             */
            await Task.Delay(100);

            if (!IsCurrentSession(sessionVersion))
            {
                return;
            }

            _mediaPlayer.SetPause(true);

            await Task.Delay(50);

            if (!IsCurrentSession(sessionVersion))
            {
                return;
            }

            _isMediaReady = true;
            _pendingPosition ??= 0d;
            _seekTimer.Start();

            StatusText.Text =
                "Пауза без звука. Перемещайте курсор по области видео.";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            if (IsCurrentSession(sessionVersion))
            {
                ReleaseActiveMedia();

                VideoHintText.Text =
                    "Ошибка открытия видео";

                StatusText.Text = exception.Message;

                MessageBox.Show(
                    this,
                    $"Не удалось открыть видео.{Environment.NewLine}" +
                    $"{Environment.NewLine}{exception.Message}",
                    "Hover Scrubbing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private void EnsurePlayerCreated()
    {
        if (_mediaPlayer is not null)
        {
            return;
        }

        Core.Initialize();

        _libVlc = new LibVLC(
            "--no-audio",
            "--no-video-title-show");

        _mediaPlayer = new MediaPlayer(_libVlc)
        {
            Mute = true,
            Volume = 0
        };

        ScrubVideoView.MediaPlayer = _mediaPlayer;
    }

    private static async Task<bool> StartPlaybackAndWaitAsync(
        MediaPlayer mediaPlayer)
    {
        var result = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<EventArgs>? playingHandler = null;
        EventHandler<EventArgs>? errorHandler = null;

        playingHandler = (_, _) =>
            result.TrySetResult(true);

        errorHandler = (_, _) =>
            result.TrySetResult(false);

        mediaPlayer.Playing += playingHandler;
        mediaPlayer.EncounteredError += errorHandler;

        try
        {
            if (!mediaPlayer.Play())
            {
                return false;
            }

            var completedTask = await Task.WhenAny(
                result.Task,
                Task.Delay(TimeSpan.FromSeconds(8)));

            return completedTask == result.Task &&
                   await result.Task;
        }
        finally
        {
            mediaPlayer.Playing -= playingHandler;
            mediaPlayer.EncounteredError -= errorHandler;
        }
    }

    private void SeekTimer_Tick(
        object? sender,
        EventArgs e)
    {
        if (!_isMediaReady ||
            !_isHovering ||
            _mediaPlayer is null ||
            _activeMedia is null)
        {
            _seekTimer.Stop();
            return;
        }

        if (_pendingPosition is null)
        {
            _seekTimer.Stop();
            return;
        }

        var position = _pendingPosition.Value;
        _pendingPosition = null;

        var mediaPlayer = _mediaPlayer;

        if (!mediaPlayer.IsSeekable)
        {
            StatusText.Text =
                "Этот файл пока не разрешает переход по времени.";

            return;
        }

        try
        {
            var duration = mediaPlayer.Length;

            var maximumPosition =
                duration > 250
                    ? 1d - (250d / duration)
                    : 0.99d;

            /*
             * Position в первом варианте прототипа оказался отзывчивее Time.
             * При этом оставляем небольшой отступ от конца, чтобы LibVLC
             * не переходил в состояние Ended.
             */
            var safePosition = Math.Clamp(
                position,
                0d,
                maximumPosition);

            mediaPlayer.Position = (float)safePosition;

            /*
             * LibVLC 3 иногда не обновляет видеовыход после перехода
             * в состоянии паузы. Запрашиваем декодирование следующего
             * кадра, не запуская обычное воспроизведение.
             */
            mediaPlayer.NextFrame();

            PositionText.Text =
                $"Позиция: {position:P0}";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);

            _seekTimer.Stop();

            StatusText.Text =
                $"Ошибка перехода по времени: {exception.Message}";
        }
    }

    private bool IsCurrentSession(
        int sessionVersion)
    {
        return !_isDisposed &&
               _isHovering &&
               sessionVersion == _sessionVersion;
    }

    private void ReleaseActiveMedia()
    {
        _sessionVersion++;
        _isMediaReady = false;

        _seekTimer.Stop();
        _pendingPosition = null;

        var media = _activeMedia;
        _activeMedia = null;

        if (_mediaPlayer is not null)
        {
            try
            {
                _mediaPlayer.Stop();
                _mediaPlayer.Media = null;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }
        }

        media?.Dispose();
    }

    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
        _isDisposed = true;
        _isHovering = false;

        ReleaseActiveMedia();

        ScrubVideoView.MediaPlayer = null;

        _mediaPlayer?.Dispose();
        _mediaPlayer = null;

        _libVlc?.Dispose();
        _libVlc = null;
    }
}
