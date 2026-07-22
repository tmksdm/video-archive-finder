using Microsoft.Extensions.Logging;
using VideoArchiveFinder.Application.ArchiveSources;

namespace VideoArchiveFinder.Infrastructure.ArchiveSources;

public sealed class ArchiveSourceAvailabilityChecker :
    IArchiveSourceAvailabilityChecker
{
    private static readonly TimeSpan DefaultTimeout =
        TimeSpan.FromSeconds(3);

    private const int DefaultMaximumConcurrentChecks = 2;

    private readonly IArchivePathProbe _pathProbe;
    private readonly ILogger<ArchiveSourceAvailabilityChecker> _logger;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _concurrencyGate;

    public ArchiveSourceAvailabilityChecker(
        IArchivePathProbe pathProbe,
        ILogger<ArchiveSourceAvailabilityChecker> logger)
        : this(
            pathProbe,
            logger,
            DefaultTimeout,
            DefaultMaximumConcurrentChecks)
    {
    }

    public ArchiveSourceAvailabilityChecker(
        IArchivePathProbe pathProbe,
        ILogger<ArchiveSourceAvailabilityChecker> logger,
        TimeSpan timeout,
        int maximumConcurrentChecks)
    {
        ArgumentNullException.ThrowIfNull(pathProbe);
        ArgumentNullException.ThrowIfNull(logger);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Timeout must be greater than zero.");
        }

        if (maximumConcurrentChecks <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumConcurrentChecks),
                "Maximum concurrent checks must be greater than zero.");
        }

        _pathProbe = pathProbe;
        _logger = logger;
        _timeout = timeout;
        _concurrencyGate = new SemaphoreSlim(
            maximumConcurrentChecks,
            maximumConcurrentChecks);
    }

    public async Task<ArchiveSourceAvailability> CheckAsync(
        string fullPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException(
                "Archive source path cannot be empty.",
                nameof(fullPath));
        }

        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutCancellation.CancelAfter(_timeout);

        var enteredGate = false;

        try
        {
            await _concurrencyGate.WaitAsync(
                    timeoutCancellation.Token)
                .ConfigureAwait(false);

            enteredGate = true;

            var probeTask = Task.Run(
                () => _pathProbe.DirectoryExists(fullPath));

            try
            {
                var isAvailable = await probeTask
                    .WaitAsync(timeoutCancellation.Token)
                    .ConfigureAwait(false);

                return isAvailable
                    ? ArchiveSourceAvailability.Available
                    : ArchiveSourceAvailability.Unavailable;
            }
            catch (OperationCanceledException)
            {
                ReleaseGateWhenProbeCompletes(probeTask);
                enteredGate = false;

                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                _logger.LogWarning(
                    "Availability check timed out for archive source path {SourcePath}.",
                    fullPath);

                return ArchiveSourceAvailability.TimedOut;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Availability check failed for archive source path {SourcePath}.",
                    fullPath);

                return ArchiveSourceAvailability.Unavailable;
            }
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Availability check timed out while waiting for an available check slot.");

            return ArchiveSourceAvailability.TimedOut;
        }
        finally
        {
            if (enteredGate)
            {
                _concurrencyGate.Release();
            }
        }
    }

    private void ReleaseGateWhenProbeCompletes(Task probeTask)
    {
        _ = ReleaseGateWhenProbeCompletesAsync(probeTask);
    }

    private async Task ReleaseGateWhenProbeCompletesAsync(Task probeTask)
    {
        try
        {
            await probeTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "A timed-out archive source availability probe completed with an error.");
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }
}
