using VideoArchiveFinder.Application.VideoFiles;

namespace VideoArchiveFinder.Infrastructure.Indexing;

public sealed class SystemVideoFileSystem
    : IVideoFileSystem
{
    public IReadOnlyList<string> GetFiles(
        string folderPath)
    {
        return Directory.GetFiles(
            folderPath,
            "*",
            new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = 0
            });
    }

    public VideoFileMetadata GetMetadata(
        string filePath)
    {
        var fileInfo = new FileInfo(filePath);

        return new VideoFileMetadata(
            SizeBytes: fileInfo.Length,
            LastWriteTimeUtc:
                new DateTimeOffset(
                    fileInfo.LastWriteTimeUtc));
    }
}
