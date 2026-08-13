using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Infrastructure.ExternalTools;

namespace VideoArchiveFinder.Tests.ExternalTools;

public sealed class SystemExternalProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_ProcessCompletes_CapturesOutputAndExitCode()
    {
        var runner = CreateRunner();

        var request = new ExternalProcessRequest(
            GetCommandProcessorPath(),
            [
                "/d",
                "/s",
                "/c",
                "echo test-output " +
                "& echo test-error 1>&2 " +
                "& exit /b 7"
            ],
            TimeSpan.FromSeconds(10));

        var result = await runner.RunAsync(request);

        Assert.Equal(
            ExternalProcessRunStatus.Completed,
            result.Status);

        Assert.True(result.IsCompleted);
        Assert.Equal(7, result.ExitCode);

        Assert.Contains(
            "test-output",
            result.StandardOutput);

        Assert.Contains(
            "test-error",
            result.StandardError);
    }

    [Fact]
    public async Task RunAsync_ExecutableDoesNotExist_ReturnsFailedToStart()
    {
        var runner = CreateRunner();

        var missingExecutable = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            "missing-tool.exe");

        var request = new ExternalProcessRequest(
            missingExecutable,
            Array.Empty<string>(),
            TimeSpan.FromSeconds(10));

        var result = await runner.RunAsync(request);

        Assert.Equal(
            ExternalProcessRunStatus.FailedToStart,
            result.Status);

        Assert.False(result.IsCompleted);
        Assert.Null(result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(result.StandardError);

        Assert.Contains(
            "Не удалось запустить",
            result.DiagnosticMessage);
    }

    [Fact]
    public async Task RunAsync_ProcessExceedsTimeout_ReturnsTimedOut()
    {
        var runner = CreateRunner();

        var request = CreateSleepingProcessRequest(
            timeout: TimeSpan.FromMilliseconds(200));

        var result = await runner.RunAsync(request);

        Assert.Equal(
            ExternalProcessRunStatus.TimedOut,
            result.Status);

        Assert.False(result.IsCompleted);
        Assert.Null(result.ExitCode);

        Assert.Contains(
            "не завершился",
            result.DiagnosticMessage);
    }

    [Fact]
    public async Task RunAsync_CancellationRequested_ThrowsCancellation()
    {
        var runner = CreateRunner();

        var request = CreateSleepingProcessRequest(
            timeout: TimeSpan.FromSeconds(30));

        using var cancellationSource =
            new CancellationTokenSource(
                TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(
                request,
                cancellationSource.Token));
    }

    [Fact]
    public async Task RunAsync_InvalidTimeout_ThrowsArgumentException()
    {
        var runner = CreateRunner();

        var request = new ExternalProcessRequest(
            GetCommandProcessorPath(),
            Array.Empty<string>(),
            TimeSpan.Zero);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => runner.RunAsync(request));
    }

    private static SystemExternalProcessRunner CreateRunner()
    {
        return new SystemExternalProcessRunner(
            NullLogger<SystemExternalProcessRunner>.Instance);
    }

    private static ExternalProcessRequest
        CreateSleepingProcessRequest(
            TimeSpan timeout)
    {
        return new ExternalProcessRequest(
            GetPowerShellPath(),
            [
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "Start-Sleep -Seconds 10"
            ],
            timeout);
    }

    private static string GetCommandProcessorPath()
    {
        var windowsDirectory =
            Environment.GetFolderPath(
                Environment.SpecialFolder.Windows);

        return Path.Combine(
            windowsDirectory,
            "System32",
            "cmd.exe");
    }

    private static string GetPowerShellPath()
    {
        var windowsDirectory =
            Environment.GetFolderPath(
                Environment.SpecialFolder.Windows);

        return Path.Combine(
            windowsDirectory,
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
    }
}
