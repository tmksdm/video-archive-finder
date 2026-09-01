using VideoArchiveFinder.Infrastructure.Thumbnails;

namespace VideoArchiveFinder.Tests.Thumbnails;

public sealed class ThumbnailCacheLimitCalculatorTests
{
    private const long Gibibyte =
        1024L * 1024 * 1024;

    private readonly ThumbnailCacheLimitCalculator
        _calculator = new();

    [Fact]
    public void CalculateMaximumSizeBytes_UsesFivePercentOfDrive()
    {
        var result = _calculator.CalculateMaximumSizeBytes(
            100 * Gibibyte,
            50 * Gibibyte,
            1 * Gibibyte);

        Assert.Equal(5 * Gibibyte, result);
    }

    [Fact]
    public void CalculateMaximumSizeBytes_CapsLimitAtTwentyGibibytes()
    {
        var result = _calculator.CalculateMaximumSizeBytes(
            1_000 * Gibibyte,
            500 * Gibibyte,
            1 * Gibibyte);

        Assert.Equal(20 * Gibibyte, result);
    }

    [Fact]
    public void CalculateMaximumSizeBytes_ReducesLimitToKeepFreeSpaceReserve()
    {
        var result = _calculator.CalculateMaximumSizeBytes(
            100 * Gibibyte,
            8 * Gibibyte,
            5 * Gibibyte);

        Assert.Equal(3 * Gibibyte, result);
    }

    [Fact]
    public void CalculateCleanupTargetBytes_ReturnsNinetyPercent()
    {
        var result = ThumbnailCacheLimitCalculator
            .CalculateCleanupTargetBytes(1_000);

        Assert.Equal(900, result);
    }
}
