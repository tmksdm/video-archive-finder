namespace VideoArchiveFinder.Application.ExternalTools;

public sealed record FfprobeJsonParseResult(
    FfprobeVideoMetadata? Metadata,
    string DiagnosticMessage)
{
    public bool IsSuccess => Metadata is not null;
}
