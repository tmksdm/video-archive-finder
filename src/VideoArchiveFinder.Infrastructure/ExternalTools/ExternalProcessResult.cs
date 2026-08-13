namespace VideoArchiveFinder.Infrastructure.ExternalTools;

public sealed record ExternalProcessResult(
    ExternalProcessRunStatus Status,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    string DiagnosticMessage)
{
    public bool IsCompleted =>
        Status == ExternalProcessRunStatus.Completed;
}
