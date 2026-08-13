namespace VideoArchiveFinder.Infrastructure.ExternalTools;

public sealed record ExternalProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout);
