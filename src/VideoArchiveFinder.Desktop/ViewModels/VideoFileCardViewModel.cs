using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media.Imaging;
using VideoArchiveFinder.Application.VideoFiles;

namespace VideoArchiveFinder.Desktop.ViewModels;

public partial class VideoFileCardViewModel
    : ObservableObject
{
    public VideoFileCardViewModel(
        IndexedVideoFile videoFile)
    {
        ArgumentNullException.ThrowIfNull(videoFile);

        VideoFile = videoFile;

        _thumbnailState =
            videoFile.ThumbnailState;

        _thumbnailPath =
            videoFile.ThumbnailPath;
    }

    public IndexedVideoFile VideoFile { get; }

    public long Id =>
        VideoFile.Id;

    public string FullPath =>
        VideoFile.FullPath;

    public string Name =>
        VideoFile.Name;

    public string NormalizedName =>
        VideoFile.NormalizedName;

    public string Extension =>
        VideoFile.Extension;

    public long SizeBytes =>
        VideoFile.SizeBytes;

    public DateTimeOffset LastWriteTimeUtc =>
        VideoFile.LastWriteTimeUtc;

    public string FolderFullPath =>
        VideoFile.FolderFullPath;

    public Guid RootSourceId =>
        VideoFile.RootSourceId;

    public bool IsAvailable =>
        VideoFile.IsAvailable;

    public bool? HasVideoStream =>
        VideoFile.HasVideoStream;

    public TimeSpan? Duration =>
        VideoFile.Duration;

    public int? Width =>
        VideoFile.Width;

    public int? Height =>
        VideoFile.Height;

    public string? Codec =>
        VideoFile.Codec;

    public VideoFileAnalysisState AnalysisState =>
        VideoFile.AnalysisState;

    [ObservableProperty]
    private VideoFileThumbnailState _thumbnailState;

    [ObservableProperty]
    private string? _thumbnailPath;

    [ObservableProperty]
    private BitmapSource? _thumbnailImage;

    [ObservableProperty]
    private bool _isThumbnailLoading;

    [ObservableProperty]
    private bool _hasThumbnailLoadError;

    public bool HasThumbnailImage =>
        ThumbnailImage is not null;

    public bool IsThumbnailPending =>
        ThumbnailState ==
            VideoFileThumbnailState.Pending ||
        IsThumbnailLoading;

    public bool IsThumbnailFailed =>
        ThumbnailState ==
            VideoFileThumbnailState.Failed ||
        HasThumbnailLoadError;

    public bool IsThumbnailPlaceholderVisible =>
        !HasThumbnailImage;

    public void ApplyThumbnailState(
        VideoFileThumbnailState state,
        string? thumbnailPath)
    {
        var normalizedPath =
            state == VideoFileThumbnailState.Succeeded
                ? thumbnailPath
                : null;

        var pathChanged =
            !string.Equals(
                ThumbnailPath,
                normalizedPath,
                StringComparison.OrdinalIgnoreCase);

        ThumbnailState = state;
        ThumbnailPath = normalizedPath;
        HasThumbnailLoadError = false;

        if (state !=
                VideoFileThumbnailState.Succeeded ||
            pathChanged)
        {
            ThumbnailImage = null;
        }
    }

    public void SetThumbnailImage(
        BitmapSource thumbnailImage)
    {
        ArgumentNullException.ThrowIfNull(
            thumbnailImage);

        ThumbnailImage = thumbnailImage;
        HasThumbnailLoadError = false;
        IsThumbnailLoading = false;
    }

    public void SetThumbnailLoadFailure()
    {
        ThumbnailImage = null;
        HasThumbnailLoadError = true;
        IsThumbnailLoading = false;
    }

    partial void OnThumbnailStateChanged(
        VideoFileThumbnailState value)
    {
        OnPropertyChanged(
            nameof(IsThumbnailPending));

        OnPropertyChanged(
            nameof(IsThumbnailFailed));
    }

    partial void OnThumbnailImageChanged(
        BitmapSource? value)
    {
        OnPropertyChanged(
            nameof(HasThumbnailImage));

        OnPropertyChanged(
            nameof(IsThumbnailPlaceholderVisible));
    }

    partial void OnIsThumbnailLoadingChanged(
        bool value)
    {
        OnPropertyChanged(
            nameof(IsThumbnailPending));
    }

    partial void OnHasThumbnailLoadErrorChanged(
        bool value)
    {
        OnPropertyChanged(
            nameof(IsThumbnailFailed));
    }
}
