namespace VideoArchiveFinder.Application.VideoFiles;

public sealed class VideoFileAnalysisStateChangedEventArgs
    : EventArgs
{
    public VideoFileAnalysisStateChangedEventArgs(
        VideoFileAnalysisRequest request,
        VideoFileAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        Request = request;
        Result = result;
    }

    public VideoFileAnalysisRequest Request { get; }

    public VideoFileAnalysisResult Result { get; }
}
