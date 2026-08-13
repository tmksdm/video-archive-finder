namespace VideoArchiveFinder.Application.ExternalTools;

public interface IFfmpegToolsLocator
{
    FfmpegToolsStatus Locate();
}
