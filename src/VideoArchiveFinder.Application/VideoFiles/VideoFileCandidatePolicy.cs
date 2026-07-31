namespace VideoArchiveFinder.Application.VideoFiles;

public sealed class VideoFileCandidatePolicy
    : IVideoFileCandidatePolicy
{
    private static readonly HashSet<string>
        SupportedExtensions = new(
            StringComparer.OrdinalIgnoreCase)
        {
            ".mp4",
            ".mov",
            ".mts",
            ".m2ts",
            ".avi",
            ".mkv",
            ".mpeg",
            ".mpg",
            ".wmv",
            ".mxf"
        };

    public bool IsCandidate(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var extension = Path.GetExtension(filePath);

        return !string.IsNullOrWhiteSpace(extension) &&
               SupportedExtensions.Contains(extension);
    }
}
