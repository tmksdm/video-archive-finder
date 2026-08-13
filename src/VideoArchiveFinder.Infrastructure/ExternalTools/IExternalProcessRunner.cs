namespace VideoArchiveFinder.Infrastructure.ExternalTools;

public interface IExternalProcessRunner
{
    Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        CancellationToken cancellationToken = default);
}
