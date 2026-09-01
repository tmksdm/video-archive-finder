namespace VideoArchiveFinder.Infrastructure.Thumbnails;

public sealed class ThumbnailCacheLimitCalculator
{
    public const long DefaultMaximumSizeBytes =
        20L * 1024 * 1024 * 1024;

    public const long DefaultMinimumFreeSpaceReserveBytes =
        10L * 1024 * 1024 * 1024;

    private readonly long _maximumSizeBytes;
    private readonly long _minimumFreeSpaceReserveBytes;

    public ThumbnailCacheLimitCalculator()
        : this(
            DefaultMaximumSizeBytes,
            DefaultMinimumFreeSpaceReserveBytes)
    {
    }

    public ThumbnailCacheLimitCalculator(
        long maximumSizeBytes,
        long minimumFreeSpaceReserveBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumSizeBytes);

        ArgumentOutOfRangeException.ThrowIfNegative(
            minimumFreeSpaceReserveBytes);

        _maximumSizeBytes = maximumSizeBytes;
        _minimumFreeSpaceReserveBytes =
            minimumFreeSpaceReserveBytes;
    }

    public long CalculateMaximumSizeBytes(
        long totalSizeBytes,
        long availableFreeSpaceBytes,
        long currentCacheSizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            totalSizeBytes);

        ArgumentOutOfRangeException.ThrowIfNegative(
            availableFreeSpaceBytes);

        ArgumentOutOfRangeException.ThrowIfNegative(
            currentCacheSizeBytes);

        var capacityLimit = Math.Min(
            _maximumSizeBytes,
            totalSizeBytes / 20);

        var freeSpaceReserve = Math.Max(
            _minimumFreeSpaceReserveBytes,
            totalSizeBytes / 10);

        var reclaimableSpace = AddSaturating(
            availableFreeSpaceBytes,
            currentCacheSizeBytes);

        var reserveLimitedSize = Math.Max(
            0,
            reclaimableSpace - freeSpaceReserve);

        return Math.Min(
            capacityLimit,
            reserveLimitedSize);
    }

    public static long CalculateCleanupTargetBytes(
        long maximumSizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumSizeBytes);

        return maximumSizeBytes -
               maximumSizeBytes / 10;
    }

    private static long AddSaturating(
        long first,
        long second)
    {
        return first > long.MaxValue - second
            ? long.MaxValue
            : first + second;
    }
}
