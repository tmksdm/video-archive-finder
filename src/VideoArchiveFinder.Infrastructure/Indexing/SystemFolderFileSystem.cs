using VideoArchiveFinder.Application.Indexing;

namespace VideoArchiveFinder.Infrastructure.Indexing;

public sealed class SystemFolderFileSystem
    : IFolderFileSystem
{
    public FileAttributes GetAttributes(
        string directoryPath)
    {
        return File.GetAttributes(directoryPath);
    }

    public IReadOnlyList<string> GetDirectories(
        string directoryPath)
    {
        return Directory.GetDirectories(
            directoryPath,
            "*",
            new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = 0
            });
    }
}
