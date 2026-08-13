using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VideoArchiveFinder.Infrastructure.ExternalTools;

public sealed class SystemExternalProcessRunner
    : IExternalProcessRunner
{
    private static readonly TimeSpan OutputDrainTimeout =
        TimeSpan.FromSeconds(5);

    private readonly ILogger<SystemExternalProcessRunner> _logger;

    public SystemExternalProcessRunner(
        ILogger<SystemExternalProcessRunner> logger)
    {
        _logger = logger;
    }

    public async Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.FileName);

        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Ограничение времени должно быть больше нуля.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = CreateStartInfo(request)
        };

        try
        {
            if (!process.Start())
            {
                return CreateStartFailure(
                    "Операционная система не запустила процесс.");
            }
        }
        catch (Exception exception) when (
            exception is Win32Exception or
            InvalidOperationException)
        {
            _logger.LogWarning(
                exception,
                "Не удалось запустить внешний инструмент {FileName}.",
                request.FileName);

            return CreateStartFailure(exception.Message);
        }

        var standardOutputTask =
            process.StandardOutput.ReadToEndAsync();

        var standardErrorTask =
            process.StandardError.ReadToEndAsync();

        using var timeoutSource =
            new CancellationTokenSource(request.Timeout);

        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(
                linkedSource.Token);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            await StopProcessAsync(process);

            await ObserveOutputAsync(
                standardOutputTask,
                standardErrorTask);

            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException)
            when (timeoutSource.IsCancellationRequested)
        {
            await StopProcessAsync(process);

            var standardOutput =
                await ReadOutputSafelyAsync(
                    standardOutputTask);

            var standardError =
                await ReadOutputSafelyAsync(
                    standardErrorTask);

            return new ExternalProcessResult(
                ExternalProcessRunStatus.TimedOut,
                null,
                standardOutput,
                standardError,
                $"Внешний процесс не завершился за " +
                $"{request.Timeout.TotalSeconds:0.#} с.");
        }

        await Task.WhenAll(
            standardOutputTask,
            standardErrorTask);

        return new ExternalProcessResult(
            ExternalProcessRunStatus.Completed,
            process.ExitCode,
            standardOutputTask.Result,
            standardErrorTask.Result,
            "Внешний процесс завершён.");
    }

    private static ProcessStartInfo CreateStartInfo(
        ExternalProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private ExternalProcessResult CreateStartFailure(
        string details)
    {
        return new ExternalProcessResult(
            ExternalProcessRunStatus.FailedToStart,
            null,
            string.Empty,
            string.Empty,
            $"Не удалось запустить внешний процесс. {details}");
    }

    private async Task StopProcessAsync(
        Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process
                .WaitForExitAsync()
                .WaitAsync(OutputDrainTimeout);
        }
        catch (Exception exception) when (
            exception is Win32Exception or
            InvalidOperationException or
            NotSupportedException or
            TimeoutException)
        {
            _logger.LogWarning(
                exception,
                "Не удалось штатно завершить внешний процесс.");
        }
    }

    private async Task ObserveOutputAsync(
        Task<string> standardOutputTask,
        Task<string> standardErrorTask)
    {
        await ReadOutputSafelyAsync(standardOutputTask);
        await ReadOutputSafelyAsync(standardErrorTask);
    }

    private async Task<string> ReadOutputSafelyAsync(
        Task<string> outputTask)
    {
        try
        {
            return await outputTask.WaitAsync(
                OutputDrainTimeout);
        }
        catch (Exception exception) when (
            exception is IOException or
            ObjectDisposedException or
            TimeoutException)
        {
            _logger.LogWarning(
                exception,
                "Не удалось полностью прочитать вывод внешнего процесса.");

            ObserveLaterFailure(outputTask);
            return string.Empty;
        }
    }

    private static void ObserveLaterFailure(
        Task<string> outputTask)
    {
        _ = outputTask.ContinueWith(
            completedTask =>
            {
                _ = completedTask.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted |
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
