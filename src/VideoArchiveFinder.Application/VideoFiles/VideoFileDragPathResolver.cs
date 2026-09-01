namespace VideoArchiveFinder.Application.VideoFiles;

public static class VideoFileDragPathResolver
{
    public static VideoFileDragSelection Resolve(
        IEnumerable<IndexedVideoFile> selectedFiles,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(selectedFiles);
        ArgumentNullException.ThrowIfNull(fileExists);

        var paths = new List<string>();
        var seenPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var unavailableCount = 0;

        foreach (var file in selectedFiles)
        {
            if (!file.IsAvailable ||
                !fileExists(file.FullPath))
            {
                unavailableCount++;
                continue;
            }

            if (seenPaths.Add(file.FullPath))
            {
                paths.Add(file.FullPath);
            }
        }

        return new VideoFileDragSelection(
            paths,
            unavailableCount);
    }
}

public sealed record VideoFileDragSelection(
    IReadOnlyList<string> Paths,
    int UnavailableCount);
