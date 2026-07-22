using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.ArchiveSources;
using VideoArchiveFinder.Infrastructure.ArchiveSources;

namespace VideoArchiveFinder.Tests.ArchiveSources;

public sealed class ArchiveSourceAvailabilityCheckerTests
{
    [Fact]
    public async Task CheckAsync_ExistingDirectory_ReturnsAvailable()
    {
        var checker = CreateChecker(
            _ => true);

        var result = await checker.CheckAsync(
            @"C:\Available Archive");

        Assert.Equal(
            ArchiveSourceAvailability.Available,
            result);
    }

    [Fact]
    public async Task CheckAsync_MissingDirectory_ReturnsUnavailable()
    {
        var checker = CreateChecker(
            _ => false);

        var result = await checker.CheckAsync(
            @"C:\Missing Archive");

        Assert.Equal(
            ArchiveSourceAvailability.Unavailable,
            result);
    }

    [Fact]
    public async Task CheckAsync_SlowDirectoryCheck_ReturnsTimedOut()
    {
        var checker = CreateChecker(
            _ =>
            {
                Thread.Sleep(250);
                return true;
            },
            TimeSpan.FromMilliseconds(30));

        var result = await checker.CheckAsync(
            @"\\server\share");

        Assert.Equal(
            ArchiveSourceAvailability.TimedOut,
            result);
    }

    [Fact]
    public async Task CheckAsync_CancellationRequested_ThrowsCancellation()
    {
        var checker = CreateChecker(
            _ =>
            {
                Thread.Sleep(250);
                return true;
            });

        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => checker.CheckAsync(
                @"\\server\share",
                cancellation.Token));
    }

    private static ArchiveSourceAvailabilityChecker CreateChecker(
        Func<string, bool> probe,
        TimeSpan? timeout = null)
    {
        return new ArchiveSourceAvailabilityChecker(
            new TestArchivePathProbe(probe),
            NullLogger<ArchiveSourceAvailabilityChecker>.Instance,
            timeout ?? TimeSpan.FromSeconds(1),
            maximumConcurrentChecks: 2);
    }

    private sealed class TestArchivePathProbe(
        Func<string, bool> probe)
        : IArchivePathProbe
    {
        public bool DirectoryExists(string fullPath)
        {
            return probe(fullPath);
        }
    }
}
