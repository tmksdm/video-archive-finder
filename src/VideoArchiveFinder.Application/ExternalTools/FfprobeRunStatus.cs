namespace VideoArchiveFinder.Application.ExternalTools;

public enum FfprobeRunStatus
{
    Succeeded,
    ToolUnavailable,
    InputUnavailable,
    Failed,
    TimedOut
}
