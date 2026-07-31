namespace VideoArchiveFinder.Application.VideoFiles;

public interface IVideoFileSystem
{
    IReadOnlyList<string> GetFiles(
        string folderPath);

    VideoFileMetadata GetMetadata(
        string filePath);
}
