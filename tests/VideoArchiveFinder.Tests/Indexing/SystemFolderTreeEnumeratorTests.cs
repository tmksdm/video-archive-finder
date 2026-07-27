using VideoArchiveFinder.Application.Indexing;
using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Tests.Indexing;

public sealed class SystemFolderTreeEnumeratorTests
{
    [Fact]
    public async Task EnumerateAsync_ReturnsFoldersWithParentPaths()
    {
        const string rootPath = @"C:\Archive";
        const string firstChild =
            @"C:\Archive\First";
        const string secondChild =
            @"C:\Archive\Second";

        var fileSystem =
            new TestFolderFileSystem();

        fileSystem.SetDirectories(
            rootPath,
            firstChild,
            secondChild);

        fileSystem.SetDirectories(firstChild);
        fileSystem.SetDirectories(secondChild);

        var enumerator =
            new SystemFolderTreeEnumerator(
                fileSystem);

        var entries =
            await ReadAllAsync(
                enumerator,
                rootPath);

        var folders =
            entries
                .OfType<DiscoveredFolder>()
                .ToArray();

        Assert.Equal(3, folders.Length);

        var rootFolder =
            Assert.Single(
                folders,
                folder =>
                    folder.FullPath == rootPath);

        Assert.Null(rootFolder.ParentFullPath);
        Assert.Equal(
            2,
            rootFolder.DirectSubfolderCount);
        Assert.True(rootFolder.IsAvailable);

        var firstFolder =
            Assert.Single(
                folders,
                folder =>
                    folder.FullPath == firstChild);

        Assert.Equal(
            rootPath,
            firstFolder.ParentFullPath);
    }

    [Fact]
    public async Task EnumerateAsync_AfterAccessError_ContinuesScanning()
    {
        const string rootPath = @"C:\Archive";
        const string deniedPath =
            @"C:\Archive\Denied";
        const string availablePath =
            @"C:\Archive\Available";

        var fileSystem =
            new TestFolderFileSystem();

        fileSystem.SetDirectories(
            rootPath,
            deniedPath,
            availablePath);

        fileSystem.SetAttributesException(
            deniedPath,
            new UnauthorizedAccessException(
                "Access denied."));

        fileSystem.SetDirectories(
            availablePath);

        var enumerator =
            new SystemFolderTreeEnumerator(
                fileSystem);

        var entries =
            await ReadAllAsync(
                enumerator,
                rootPath);

        var error =
            Assert.Single(
                entries.OfType<
                    FolderEnumerationError>());

        Assert.Equal(
            deniedPath,
            error.DirectoryPath);

        var folders =
            entries
                .OfType<DiscoveredFolder>()
                .ToArray();

        Assert.Contains(
            folders,
            folder =>
                folder.FullPath == availablePath &&
                folder.IsAvailable);

        Assert.Contains(
            folders,
            folder =>
                folder.FullPath == deniedPath &&
                !folder.IsAvailable);
    }

    [Fact]
    public async Task EnumerateAsync_ReparsePoint_IsNotTraversed()
    {
        const string rootPath = @"C:\Archive";
        const string linkPath =
            @"C:\Archive\Link";

        var fileSystem =
            new TestFolderFileSystem();

        fileSystem.SetDirectories(
            rootPath,
            linkPath);

        fileSystem.SetAttributes(
            linkPath,
            FileAttributes.Directory |
            FileAttributes.ReparsePoint);

        var enumerator =
            new SystemFolderTreeEnumerator(
                fileSystem);

        var entries =
            await ReadAllAsync(
                enumerator,
                rootPath);

        var linkFolder =
            Assert.Single(
                entries
                    .OfType<DiscoveredFolder>(),
                folder =>
                    folder.FullPath == linkPath);

        Assert.True(linkFolder.IsReparsePoint);

        Assert.DoesNotContain(
            linkPath,
            fileSystem.DirectoryEnumerationCalls);
    }

    [Fact]
    public async Task EnumerateAsync_RepeatedPath_IsVisitedOnlyOnce()
    {
        const string rootPath = @"C:\Archive";
        const string childPath =
            @"C:\Archive\Child";

        var fileSystem =
            new TestFolderFileSystem();

        fileSystem.SetDirectories(
            rootPath,
            childPath);

        fileSystem.SetDirectories(
            childPath,
            rootPath);

        var enumerator =
            new SystemFolderTreeEnumerator(
                fileSystem);

        var entries =
            await ReadAllAsync(
                enumerator,
                rootPath);

        var folders =
            entries
                .OfType<DiscoveredFolder>()
                .ToArray();

        Assert.Equal(2, folders.Length);

        Assert.Single(
            folders,
            folder =>
                folder.FullPath == rootPath);

        Assert.Single(
            folders,
            folder =>
                folder.FullPath == childPath);
    }

    private static async Task<
        IReadOnlyList<FolderEnumerationEntry>>
        ReadAllAsync(
            IFolderTreeEnumerator enumerator,
            string rootPath)
    {
        var entries =
            new List<FolderEnumerationEntry>();

        await foreach (var entry in
            enumerator.EnumerateAsync(rootPath))
        {
            entries.Add(entry);
        }

        return entries;
    }

    private sealed class TestFolderFileSystem
        : IFolderFileSystem
    {
        private readonly
            Dictionary<string, FileAttributes>
            _attributes =
                new(
                    StringComparer.OrdinalIgnoreCase);

        private readonly
            Dictionary<string, IReadOnlyList<string>>
            _directories =
                new(
                    StringComparer.OrdinalIgnoreCase);

        private readonly
            Dictionary<string, Exception>
            _attributeExceptions =
                new(
                    StringComparer.OrdinalIgnoreCase);

        public List<string>
            DirectoryEnumerationCalls
        { get; } = [];

        public FileAttributes GetAttributes(
            string directoryPath)
        {
            if (_attributeExceptions.TryGetValue(
                directoryPath,
                out var exception))
            {
                throw exception;
            }

            return _attributes.TryGetValue(
                directoryPath,
                out var attributes)
                    ? attributes
                    : FileAttributes.Directory;
        }

        public IReadOnlyList<string> GetDirectories(
            string directoryPath)
        {
            DirectoryEnumerationCalls.Add(
                directoryPath);

            return _directories.TryGetValue(
                directoryPath,
                out var directories)
                    ? directories
                    : [];
        }

        public void SetAttributes(
            string directoryPath,
            FileAttributes attributes)
        {
            _attributes[directoryPath] =
                attributes;
        }

        public void SetAttributesException(
            string directoryPath,
            Exception exception)
        {
            _attributeExceptions[directoryPath] =
                exception;
        }

        public void SetDirectories(
            string directoryPath,
            params string[] childDirectories)
        {
            _directories[directoryPath] =
                childDirectories;
        }
    }
}
