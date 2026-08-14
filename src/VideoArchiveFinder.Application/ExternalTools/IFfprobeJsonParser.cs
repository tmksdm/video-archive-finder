namespace VideoArchiveFinder.Application.ExternalTools;

public interface IFfprobeJsonParser
{
    FfprobeJsonParseResult Parse(string json);
}
