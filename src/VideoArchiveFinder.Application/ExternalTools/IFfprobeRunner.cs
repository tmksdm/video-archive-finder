namespace VideoArchiveFinder.Application.ExternalTools;

public interface IFfprobeRunner
{
    Task<FfprobeRunResult> RunAsync(
        string videoPath,
        CancellationToken cancellationToken = default);
}
