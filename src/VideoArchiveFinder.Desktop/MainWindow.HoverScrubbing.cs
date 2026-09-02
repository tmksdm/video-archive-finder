using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.ExternalTools;
using VideoArchiveFinder.Desktop.ViewModels;

namespace VideoArchiveFinder.Desktop;

public partial class MainWindow
{
    private static readonly TimeSpan HoverScrubSeekInterval =
        TimeSpan.FromMilliseconds(75);

    private static readonly TimeSpan HoverScrubIdlePauseDelay =
        TimeSpan.FromMilliseconds(180);

    private readonly ILogger<MainWindow> _hoverScrubLogger;
    private readonly ILibVlcRuntimeLocator
        _libVlcRuntimeLocator;

    private LibVlcRuntimeStatus? _hoverScrubRuntimeStatus;

    private DispatcherTimer? _hoverScrubSeekTimer;
    private LibVLC? _hoverScrubLibVlc;
    private MediaPlayer? _hoverScrubMediaPlayer;
    private Media? _hoverScrubMedia;
    private StreamMediaInput? _hoverScrubMediaInput;
    private Stream? _hoverScrubMediaStream;
    private FrameworkElement? _hoverScrubTarget;
    private VideoFileCardViewModel? _hoverScrubVideoFile;
    private double? _hoverScrubPendingPosition;
    private DateTime _hoverScrubLastInputUtc;
    private Task _hoverScrubCleanupTask =
        Task.CompletedTask;
    private int _hoverScrubSessionVersion;
    private bool _isHoverScrubMediaReady;
    private bool _isHoverScrubPaused;
    private bool _isHoverScrubOverlayReady;
    private bool _isHoverScrubbing;
    private bool _isHoverScrubDisposed;

    private void InitializeHoverScrubbing()
    {
        _hoverScrubRuntimeStatus =
            _libVlcRuntimeLocator.Locate();

        if (!_hoverScrubRuntimeStatus.IsReady)
        {
            return;
        }

        _hoverScrubSeekTimer = new DispatcherTimer
        {
            Interval = HoverScrubSeekInterval
        };

        _hoverScrubSeekTimer.Tick +=
            HoverScrubSeekTimer_Tick;
    }

    private async void VideoPreview_MouseEnter(
        object sender,
        MouseEventArgs e)
    {
        if (_isHoverScrubDisposed ||
            _hoverScrubRuntimeStatus?.IsReady != true ||
            sender is not FrameworkElement target ||
            target.DataContext is not
                VideoFileCardViewModel videoFile ||
            !videoFile.IsAvailable)
        {
            return;
        }

        StopHoverScrubbing();

        _hoverScrubTarget = target;
        _hoverScrubVideoFile = videoFile;
        _isHoverScrubbing = true;
        _isHoverScrubOverlayReady = false;

        target.LayoutUpdated +=
            HoverScrubTarget_LayoutUpdated;
        target.Unloaded +=
            HoverScrubTarget_Unloaded;
        target.DataContextChanged +=
            HoverScrubTarget_DataContextChanged;
        target.MouseMove +=
            HoverScrubTarget_MouseMove;
        target.MouseLeave +=
            HoverScrubTarget_MouseLeave;

        UpdateHoverScrubOverlayPlacement();
        UpdateHoverScrubPendingPosition(
            e.GetPosition(target).X,
            target.ActualWidth);

        HoverScrubOverlay.Visibility =
            Visibility.Visible;

        var sessionVersion =
            ++_hoverScrubSessionVersion;

        await OpenHoverScrubMediaAsync(
            sessionVersion,
            videoFile.FullPath);
    }

    private void HoverScrubInputLayer_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_isHoverScrubbing)
        {
            return;
        }

        var width =
            HoverScrubInputLayer.ActualWidth;

        UpdateHoverScrubPendingPosition(
            e.GetPosition(HoverScrubInputLayer).X,
            width);

        _hoverScrubLastInputUtc =
            DateTime.UtcNow;

        ResumeHoverScrubPlayback();

        if (_isHoverScrubMediaReady &&
            _hoverScrubSeekTimer is not null &&
            !_hoverScrubSeekTimer.IsEnabled)
        {
            _hoverScrubSeekTimer.Start();
        }
    }

    private void HoverScrubInputLayer_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (_hoverScrubTarget is not { } target ||
            FindVisualAncestor<ScrollViewer>(target) is not
                { } scrollViewer)
        {
            return;
        }

        var forwardedEvent = new MouseWheelEventArgs(
            e.MouseDevice,
            e.Timestamp,
            e.Delta)
        {
            RoutedEvent = Mouse.MouseWheelEvent,
            Source = scrollViewer
        };

        scrollViewer.RaiseEvent(forwardedEvent);
        e.Handled = forwardedEvent.Handled;
    }

    private void HoverScrubInputLayer_MouseLeave(
        object sender,
        MouseEventArgs e)
    {
        StopHoverScrubbing();
    }

    private void HoverScrubTarget_LayoutUpdated(
        object? sender,
        EventArgs e)
    {
        UpdateHoverScrubOverlayPlacement();
    }

    private void HoverScrubTarget_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (_isHoverScrubOverlayReady ||
            sender is not FrameworkElement target)
        {
            return;
        }

        UpdateHoverScrubPendingPosition(
            e.GetPosition(target).X,
            target.ActualWidth);

        _hoverScrubLastInputUtc =
            DateTime.UtcNow;
    }

    private void HoverScrubTarget_MouseLeave(
        object sender,
        MouseEventArgs e)
    {
        if (!_isHoverScrubOverlayReady)
        {
            StopHoverScrubbing();
        }
    }

    private void HoverScrubTarget_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        StopHoverScrubbing();
    }

    private void HoverScrubTarget_DataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(
                e.NewValue,
                _hoverScrubVideoFile))
        {
            StopHoverScrubbing();
        }
    }

    private void UpdateHoverScrubPendingPosition(
        double horizontalPosition,
        double width)
    {
        if (width <= 0)
        {
            return;
        }

        _hoverScrubPendingPosition = Math.Clamp(
            horizontalPosition / width,
            0d,
            1d);
    }

    private void UpdateHoverScrubOverlayPlacement()
    {
        var target = _hoverScrubTarget;

        if (target is null ||
            !target.IsLoaded ||
            !target.IsVisible ||
            target.ActualWidth <= 0 ||
            target.ActualHeight <= 0 ||
            !ReferenceEquals(
                target.DataContext,
                _hoverScrubVideoFile))
        {
            StopHoverScrubbing();
            return;
        }

        try
        {
            var targetPosition =
                target.TranslatePoint(
                    new Point(),
                    RootLayout);

            Canvas.SetLeft(
                HoverScrubOverlay,
                _isHoverScrubOverlayReady
                    ? targetPosition.X
                    : -target.ActualWidth - 2);
            Canvas.SetTop(
                HoverScrubOverlay,
                _isHoverScrubOverlayReady
                    ? targetPosition.Y
                    : 0);

            HoverScrubOverlay.Width =
                target.ActualWidth;
            HoverScrubOverlay.Height =
                target.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            StopHoverScrubbing();
        }
    }

    private async Task OpenHoverScrubMediaAsync(
        int sessionVersion,
        string filePath)
    {
        try
        {
            var pendingCleanup =
                _hoverScrubCleanupTask;

            await pendingCleanup;

            if (!IsCurrentHoverScrubSession(
                    sessionVersion))
            {
                return;
            }

            if (!EnsureHoverScrubPlayerCreated())
            {
                StopHoverScrubbing();
                return;
            }

            if (_hoverScrubLibVlc is null ||
                _hoverScrubMediaPlayer is null)
            {
                return;
            }

            var resources = await Task.Run(
                () => CreateHoverScrubMediaResources(
                    _hoverScrubLibVlc,
                    filePath));

            if (!IsCurrentHoverScrubSession(
                    sessionVersion))
            {
                await Task.Run(
                    () => DisposeHoverScrubMediaResources(
                        resources.Media,
                        resources.Input,
                        resources.Stream));

                return;
            }

            _hoverScrubMedia = resources.Media;
            _hoverScrubMediaInput = resources.Input;
            _hoverScrubMediaStream = resources.Stream;
            _hoverScrubMediaPlayer.Media =
                resources.Media;
            _hoverScrubMediaPlayer.Mute = true;
            _hoverScrubMediaPlayer.Volume = 0;

            var started =
                await StartHoverScrubPlaybackAsync(
                    _hoverScrubMediaPlayer);

            if (!IsCurrentHoverScrubSession(
                    sessionVersion))
            {
                return;
            }

            if (!started)
            {
                _hoverScrubLogger.LogWarning(
                    "LibVLC could not start hover scrubbing for {VideoPath}",
                    filePath);

                StopHoverScrubbing();
                return;
            }

            _isHoverScrubMediaReady = true;
            _isHoverScrubPaused = false;
            _hoverScrubPendingPosition ??= 0d;

            var initialPosition =
                _hoverScrubPendingPosition.Value;

            _hoverScrubPendingPosition = null;

            var firstFrameReady =
                await SeekAndWaitForHoverScrubFrameAsync(
                    _hoverScrubMediaPlayer,
                    initialPosition);

            if (!IsCurrentHoverScrubSession(
                    sessionVersion))
            {
                return;
            }

            if (firstFrameReady)
            {
                DetachHoverScrubTargetPrimingEvents();
                _isHoverScrubOverlayReady = true;
                UpdateHoverScrubOverlayPlacement();
            }
            else
            {
                _hoverScrubLogger.LogWarning(
                    "LibVLC did not produce the first hover-scrub frame for {VideoPath}",
                    filePath);

                StopHoverScrubbing();
                return;
            }

            _hoverScrubLastInputUtc =
                DateTime.UtcNow;
            _hoverScrubSeekTimer?.Start();
        }
        catch (Exception exception)
        {
            if (!IsCurrentHoverScrubSession(
                    sessionVersion))
            {
                return;
            }

            _hoverScrubLogger.LogWarning(
                exception,
                "Hover scrubbing failed for {VideoPath}",
                filePath);

            StopHoverScrubbing();
        }
    }

    private bool EnsureHoverScrubPlayerCreated()
    {
        if (_hoverScrubMediaPlayer is not null)
        {
            return true;
        }

        var runtimeStatus =
            _libVlcRuntimeLocator.Locate();

        if (!runtimeStatus.IsReady)
        {
            _hoverScrubRuntimeStatus = runtimeStatus;
            _hoverScrubLogger.LogWarning(
                "{DiagnosticMessage}",
                runtimeStatus.DiagnosticMessage);
            return false;
        }

        Core.Initialize(
            runtimeStatus.RuntimeDirectory);

        _hoverScrubLibVlc = new LibVLC(
            "--no-audio",
            "--no-video-title-show");

        _hoverScrubMediaPlayer =
            new MediaPlayer(_hoverScrubLibVlc)
            {
                Mute = true,
                Volume = 0
            };

        HoverScrubVideoView.MediaPlayer =
            _hoverScrubMediaPlayer;

        return true;
    }

    private static (
        Media Media,
        StreamMediaInput? Input,
        Stream? Stream)
        CreateHoverScrubMediaResources(
        LibVLC libVlc,
        string filePath)
    {
        if (!filePath.StartsWith(
                @"\\",
                StringComparison.Ordinal))
        {
            return (
                new Media(
                    libVlc,
                    filePath,
                    FromType.FromPath),
                null,
                null);
        }

        var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.RandomAccess);

        var input =
            new StreamMediaInput(stream);

        return (
            new Media(libVlc, input),
            input,
            stream);
    }

    private static async Task<bool>
        StartHoverScrubPlaybackAsync(
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

    private void HoverScrubSeekTimer_Tick(
        object? sender,
        EventArgs e)
    {
        if (!_isHoverScrubMediaReady ||
            !_isHoverScrubbing ||
            _hoverScrubMediaPlayer is null ||
            _hoverScrubMedia is null ||
            _hoverScrubSeekTimer is null)
        {
            _hoverScrubSeekTimer?.Stop();
            return;
        }

        if (_hoverScrubPendingPosition is null)
        {
            if (DateTime.UtcNow -
                    _hoverScrubLastInputUtc >=
                HoverScrubIdlePauseDelay)
            {
                PauseHoverScrubPlayback();
                _hoverScrubSeekTimer.Stop();
            }

            return;
        }

        var position =
            _hoverScrubPendingPosition.Value;

        _hoverScrubPendingPosition = null;

        if (!_hoverScrubMediaPlayer.IsSeekable)
        {
            return;
        }

        try
        {
            if (!ResumeHoverScrubPlayback())
            {
                _hoverScrubPendingPosition = position;
                return;
            }

            _hoverScrubMediaPlayer.Position =
                GetSafeHoverScrubPosition(
                    _hoverScrubMediaPlayer,
                    position);
        }
        catch (Exception exception)
        {
            _hoverScrubLogger.LogWarning(
                exception,
                "Hover scrubbing seek failed for {VideoPath}",
                _hoverScrubVideoFile?.FullPath);

            StopHoverScrubbing();
        }
    }

    private static async Task<bool>
        SeekAndWaitForHoverScrubFrameAsync(
            MediaPlayer mediaPlayer,
            double position)
    {
        var frameReady = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<MediaPlayerTimeChangedEventArgs>?
            timeChangedHandler = null;

        timeChangedHandler = (_, _) =>
            frameReady.TrySetResult(true);

        mediaPlayer.TimeChanged +=
            timeChangedHandler;

        try
        {
            mediaPlayer.Position =
                GetSafeHoverScrubPosition(
                    mediaPlayer,
                    position);

            var completedTask = await Task.WhenAny(
                frameReady.Task,
                Task.Delay(TimeSpan.FromSeconds(2)));

            if (completedTask != frameReady.Task)
            {
                return false;
            }

            await Task.Delay(60);
            return true;
        }
        finally
        {
            mediaPlayer.TimeChanged -=
                timeChangedHandler;
        }
    }

    private static float GetSafeHoverScrubPosition(
        MediaPlayer mediaPlayer,
        double requestedPosition)
    {
        var duration = mediaPlayer.Length;

        if (duration <= 0)
        {
            return (float)Math.Clamp(
                requestedPosition,
                0d,
                0.98d);
        }

        var endMarginMilliseconds = Math.Clamp(
            duration * 0.05d,
            500d,
            2000d);

        var maximumPosition = Math.Max(
            0d,
            1d - (endMarginMilliseconds / duration));

        return (float)Math.Clamp(
            requestedPosition,
            0d,
            maximumPosition);
    }

    private bool ResumeHoverScrubPlayback()
    {
        var mediaPlayer =
            _hoverScrubMediaPlayer;

        if (mediaPlayer is null)
        {
            return false;
        }

        if (mediaPlayer.State is
                VLCState.Ended or
                VLCState.Stopped)
        {
            _isHoverScrubPaused = false;
            mediaPlayer.Play();
            return false;
        }

        if (mediaPlayer.State is
                VLCState.Opening or
                VLCState.Buffering)
        {
            return false;
        }

        if (!_isHoverScrubPaused &&
            mediaPlayer.State != VLCState.Paused)
        {
            return true;
        }

        mediaPlayer.SetPause(false);
        _isHoverScrubPaused = false;
        return true;
    }

    private void PauseHoverScrubPlayback()
    {
        if (_isHoverScrubPaused ||
            _hoverScrubMediaPlayer is null)
        {
            return;
        }

        _hoverScrubMediaPlayer.SetPause(true);
        _isHoverScrubPaused = true;
    }

    private bool IsCurrentHoverScrubSession(
        int sessionVersion)
    {
        return !_isHoverScrubDisposed &&
               _isHoverScrubbing &&
               sessionVersion ==
                   _hoverScrubSessionVersion;
    }

    private void StopHoverScrubbing()
    {
        _hoverScrubSessionVersion++;
        _isHoverScrubbing = false;
        _isHoverScrubMediaReady = false;
        _isHoverScrubPaused = false;
        _isHoverScrubOverlayReady = false;

        _hoverScrubSeekTimer?.Stop();
        _hoverScrubPendingPosition = null;

        if (_hoverScrubTarget is not null)
        {
            _hoverScrubTarget.LayoutUpdated -=
                HoverScrubTarget_LayoutUpdated;
            _hoverScrubTarget.Unloaded -=
                HoverScrubTarget_Unloaded;
            _hoverScrubTarget.DataContextChanged -=
                HoverScrubTarget_DataContextChanged;

            DetachHoverScrubTargetPrimingEvents();
        }

        _hoverScrubTarget = null;
        _hoverScrubVideoFile = null;

        HoverScrubOverlay.Visibility =
            Visibility.Collapsed;

        QueueHoverScrubMediaRelease();
    }

    private void DetachHoverScrubTargetPrimingEvents()
    {
        if (_hoverScrubTarget is null)
        {
            return;
        }

        _hoverScrubTarget.MouseMove -=
            HoverScrubTarget_MouseMove;
        _hoverScrubTarget.MouseLeave -=
            HoverScrubTarget_MouseLeave;
    }

    private void QueueHoverScrubMediaRelease()
    {
        var media = _hoverScrubMedia;
        _hoverScrubMedia = null;

        var mediaInput = _hoverScrubMediaInput;
        _hoverScrubMediaInput = null;

        var mediaStream = _hoverScrubMediaStream;
        _hoverScrubMediaStream = null;

        if (media is null &&
            mediaInput is null &&
            mediaStream is null)
        {
            return;
        }

        var mediaPlayer =
            _hoverScrubMediaPlayer;
        var previousCleanup =
            _hoverScrubCleanupTask;

        _hoverScrubCleanupTask = Task.Run(
            async () =>
            {
                await previousCleanup.ConfigureAwait(false);

                try
                {
                    mediaPlayer?.Stop();
                    if (mediaPlayer is not null)
                    {
                        mediaPlayer.Media = null;
                    }
                }
                catch (Exception exception)
                {
                    _hoverScrubLogger.LogDebug(
                        exception,
                        "LibVLC cleanup failed");
                }
                finally
                {
                    DisposeHoverScrubMediaResources(
                        media,
                        mediaInput,
                        mediaStream);
                }
            });
    }

    private static void DisposeHoverScrubMediaResources(
        Media? media,
        StreamMediaInput? mediaInput,
        Stream? mediaStream)
    {
        media?.Dispose();
        mediaInput?.Dispose();
        mediaStream?.Dispose();
    }

    private void DisposeHoverScrubbing()
    {
        if (_isHoverScrubDisposed)
        {
            return;
        }

        _isHoverScrubDisposed = true;

        StopHoverScrubbing();

        _hoverScrubCleanupTask
            .GetAwaiter()
            .GetResult();

        if (_hoverScrubSeekTimer is not null)
        {
            _hoverScrubSeekTimer.Tick -=
                HoverScrubSeekTimer_Tick;
            _hoverScrubSeekTimer = null;
        }

        HoverScrubVideoView.MediaPlayer = null;

        _hoverScrubMediaPlayer?.Dispose();
        _hoverScrubMediaPlayer = null;

        _hoverScrubLibVlc?.Dispose();
        _hoverScrubLibVlc = null;
    }
}
