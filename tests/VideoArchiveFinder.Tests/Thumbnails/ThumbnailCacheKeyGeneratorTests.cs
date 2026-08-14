using VideoArchiveFinder.Application.Thumbnails;
using VideoArchiveFinder.Infrastructure.Thumbnails;

namespace VideoArchiveFinder.Tests.Thumbnails;

public sealed class ThumbnailCacheKeyGeneratorTests
{
    private readonly ThumbnailCacheKeyGenerator _generator =
        new();

    [Fact]
    public void GenerateKey_SameRequest_ReturnsSameSha256Key()
    {
        var request = CreateRequest();

        var firstKey = _generator.GenerateKey(request);
        var secondKey = _generator.GenerateKey(request);

        Assert.Equal(firstKey, secondKey);
        Assert.Matches("^[0-9a-f]{64}$", firstKey);
    }

    [Fact]
    public void GenerateKey_EquivalentRelativeAndAbsolutePaths_ReturnSameKey()
    {
        var relativePath = Path.Combine(
            ".",
            "archive",
            "video.mp4");

        var absolutePath = Path.GetFullPath(relativePath);

        var relativeRequest = CreateRequest(
            videoPath: relativePath);

        var absoluteRequest = CreateRequest(
            videoPath: absolutePath);

        var relativeKey = _generator.GenerateKey(
            relativeRequest);

        var absoluteKey = _generator.GenerateKey(
            absoluteRequest);

        Assert.Equal(relativeKey, absoluteKey);
    }

    [Fact]
    public void GenerateKey_SizeChanges_ReturnsDifferentKey()
    {
        var firstRequest = CreateRequest(
            sizeBytes: 1_000);

        var secondRequest = CreateRequest(
            sizeBytes: 1_001);

        Assert.NotEqual(
            _generator.GenerateKey(firstRequest),
            _generator.GenerateKey(secondRequest));
    }

    [Fact]
    public void GenerateKey_LastWriteTimeChanges_ReturnsDifferentKey()
    {
        var firstRequest = CreateRequest(
            lastWriteTimeUtc:
                new DateTimeOffset(
                    2026,
                    8,
                    14,
                    10,
                    0,
                    0,
                    TimeSpan.Zero));

        var secondRequest = CreateRequest(
            lastWriteTimeUtc:
                new DateTimeOffset(
                    2026,
                    8,
                    14,
                    10,
                    0,
                    1,
                    TimeSpan.Zero));

        Assert.NotEqual(
            _generator.GenerateKey(firstRequest),
            _generator.GenerateKey(secondRequest));
    }

    [Fact]
    public void GenerateKey_VideoPathChanges_ReturnsDifferentKey()
    {
        var firstRequest = CreateRequest(
            videoPath: Path.Combine(
                "archive",
                "first.mp4"));

        var secondRequest = CreateRequest(
            videoPath: Path.Combine(
                "archive",
                "second.mp4"));

        Assert.NotEqual(
            _generator.GenerateKey(firstRequest),
            _generator.GenerateKey(secondRequest));
    }

    [Fact]
    public void GenerateKey_SameInstantWithDifferentOffset_ReturnsSameKey()
    {
        var utcTime = new DateTimeOffset(
            2026,
            8,
            14,
            10,
            0,
            0,
            TimeSpan.Zero);

        var offsetTime = utcTime.ToOffset(
            TimeSpan.FromHours(3));

        var utcRequest = CreateRequest(
            lastWriteTimeUtc: utcTime);

        var offsetRequest = CreateRequest(
            lastWriteTimeUtc: offsetTime);

        Assert.Equal(
            _generator.GenerateKey(utcRequest),
            _generator.GenerateKey(offsetRequest));
    }

    [Fact]
    public void GenerateKey_BlankVideoPath_ThrowsArgumentException()
    {
        var request = CreateRequest(
            videoPath: " ");

        Assert.Throws<ArgumentException>(
            () => _generator.GenerateKey(request));
    }

    [Fact]
    public void GenerateKey_NegativeSize_ThrowsArgumentOutOfRangeException()
    {
        var request = CreateRequest(
            sizeBytes: -1);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _generator.GenerateKey(request));
    }

    private static StaticThumbnailRequest CreateRequest(
        string? videoPath = null,
        long sizeBytes = 1_000,
        DateTimeOffset? lastWriteTimeUtc = null)
    {
        return new StaticThumbnailRequest(
            videoPath ?? Path.Combine(
                "archive",
                "Видео",
                "пример.mp4"),
            sizeBytes,
            lastWriteTimeUtc ??
            new DateTimeOffset(
                2026,
                8,
                14,
                10,
                0,
                0,
                TimeSpan.Zero));
    }
}
