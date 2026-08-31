namespace VideoArchiveFinder.Application.ExternalTools;

public interface ILibVlcRuntimeLocator
{
    LibVlcRuntimeStatus Locate();
}
