using VideoArchiveFinder.Application.ExternalTools;

namespace VideoArchiveFinder.Infrastructure.ExternalTools;

public sealed class BundledFfmpegToolsLocator
    : IFfmpegToolsLocator
{
    private readonly string _applicationBaseDirectory;

    public BundledFfmpegToolsLocator()
        : this(AppContext.BaseDirectory)
    {
    }

    public BundledFfmpegToolsLocator(
        string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            applicationBaseDirectory);

        _applicationBaseDirectory =
            Path.GetFullPath(applicationBaseDirectory);
    }

    public FfmpegToolsStatus Locate()
    {
        var toolsDirectory = Path.Combine(
            _applicationBaseDirectory,
            "app",
            "tools");

        var ffmpegPath = Path.Combine(
            toolsDirectory,
            "ffmpeg.exe");

        var ffprobePath = Path.Combine(
            toolsDirectory,
            "ffprobe.exe");

        return new FfmpegToolsStatus(
            toolsDirectory,
            ffmpegPath,
            ffprobePath,
            File.Exists(ffmpegPath),
            File.Exists(ffprobePath));
    }
}
