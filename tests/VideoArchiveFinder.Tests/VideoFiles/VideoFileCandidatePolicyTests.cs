using VideoArchiveFinder.Application.VideoFiles;

namespace VideoArchiveFinder.Tests.VideoFiles;

public sealed class VideoFileCandidatePolicyTests
{
    private readonly VideoFileCandidatePolicy _policy =
        new();

    [Theory]
    [InlineData(@"C:\Archive\video.mp4")]
    [InlineData(@"C:\Archive\video.MOV")]
    [InlineData(@"C:\Archive\video.mts")]
    [InlineData(@"C:\Archive\video.M2TS")]
    [InlineData(@"C:\Archive\video.avi")]
    [InlineData(@"C:\Archive\video.mkv")]
    [InlineData(@"C:\Archive\video.mpeg")]
    [InlineData(@"C:\Archive\video.mpg")]
    [InlineData(@"C:\Archive\video.wmv")]
    [InlineData(@"\\server\share\folder\video.MXF")]
    public void IsCandidate_ReturnsTrue_ForSupportedExtension(
        string filePath)
    {
        var result = _policy.IsCandidate(filePath);

        Assert.True(result);
    }

    [Theory]
    [InlineData(@"C:\Archive\document.txt")]
    [InlineData(@"C:\Archive\image.jpg")]
    [InlineData(@"C:\Archive\video.mp4.tmp")]
    [InlineData(@"C:\Archive\file-without-extension")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsCandidate_ReturnsFalse_ForUnsupportedPath(
        string filePath)
    {
        var result = _policy.IsCandidate(filePath);

        Assert.False(result);
    }
}
