namespace VideoArchiveFinder.Application.ExternalTools;

public sealed record FfprobeRunResult(
    FfprobeRunStatus Status,
    string JsonOutput,
    int? ExitCode,
    string DiagnosticMessage)
{
    public bool IsSuccess =>
        Status == FfprobeRunStatus.Succeeded;
}
