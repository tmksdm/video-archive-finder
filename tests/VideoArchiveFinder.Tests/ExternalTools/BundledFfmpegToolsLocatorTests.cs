using VideoArchiveFinder.Infrastructure.ExternalTools;

namespace VideoArchiveFinder.Tests.ExternalTools;

public sealed class BundledFfmpegToolsLocatorTests
    : IDisposable
{
    private readonly string _baseDirectory = Path.Combine(
        Path.GetTempPath(),
        nameof(BundledFfmpegToolsLocatorTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Locate_ToolsDirectoryDoesNotExist_ReportsBothFilesMissing()
    {
        var locator = new BundledFfmpegToolsLocator(
            _baseDirectory);

        var result = locator.Locate();

        var expectedToolsDirectory = Path.Combine(
            _baseDirectory,
            "app",
            "tools");

        Assert.False(result.IsReady);
        Assert.False(result.FfmpegExists);
        Assert.False(result.FfprobeExists);
        Assert.Equal(
            expectedToolsDirectory,
            result.ToolsDirectory);

        Assert.Equal(
            ["ffmpeg.exe", "ffprobe.exe"],
            result.MissingFileNames);

        Assert.Contains(
            expectedToolsDirectory,
            result.DiagnosticMessage);
    }

    [Fact]
    public void Locate_BothFilesExist_ReportsReady()
    {
        CreateToolFile("ffmpeg.exe");
        CreateToolFile("ffprobe.exe");

        var locator = new BundledFfmpegToolsLocator(
            _baseDirectory);

        var result = locator.Locate();

        Assert.True(result.IsReady);
        Assert.True(result.FfmpegExists);
        Assert.True(result.FfprobeExists);
        Assert.Empty(result.MissingFileNames);

        Assert.Equal(
            "FFmpeg и FFprobe готовы к использованию.",
            result.DiagnosticMessage);
    }

    [Theory]
    [InlineData(true, false, "ffprobe.exe")]
    [InlineData(false, true, "ffmpeg.exe")]
    public void Locate_OneFileIsMissing_ReportsExpectedFile(
        bool createFfmpeg,
        bool createFfprobe,
        string expectedMissingFile)
    {
        if (createFfmpeg)
        {
            CreateToolFile("ffmpeg.exe");
        }

        if (createFfprobe)
        {
            CreateToolFile("ffprobe.exe");
        }

        var locator = new BundledFfmpegToolsLocator(
            _baseDirectory);

        var result = locator.Locate();

        Assert.False(result.IsReady);
        Assert.Equal(
            [expectedMissingFile],
            result.MissingFileNames);

        Assert.Contains(
            expectedMissingFile,
            result.DiagnosticMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(
                _baseDirectory,
                recursive: true);
        }
    }

    private void CreateToolFile(
        string fileName)
    {
        var toolsDirectory = Path.Combine(
            _baseDirectory,
            "app",
            "tools");

        Directory.CreateDirectory(toolsDirectory);

        File.WriteAllText(
            Path.Combine(toolsDirectory, fileName),
            string.Empty);
    }
}
