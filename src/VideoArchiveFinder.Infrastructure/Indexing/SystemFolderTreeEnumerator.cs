using System.Runtime.CompilerServices;
using System.Security;
using VideoArchiveFinder.Application.Indexing;

namespace VideoArchiveFinder.Infrastructure.Indexing;

public sealed class SystemFolderTreeEnumerator
    : IFolderTreeEnumerator
{

    private readonly IFolderFileSystem
        _folderFileSystem;

    public SystemFolderTreeEnumerator(
        IFolderFileSystem folderFileSystem)
    {
        _folderFileSystem =
            folderFileSystem;
    }

    public async IAsyncEnumerable<FolderEnumerationEntry>
        EnumerateAsync(
            string rootPath,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException(
                "Root path cannot be empty.",
                nameof(rootPath));
        }

        await Task.CompletedTask.ConfigureAwait(false);

        var pendingDirectories =
            new Stack<PendingDirectory>();

        var visitedPaths =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        pendingDirectories.Push(
            new PendingDirectory(
                rootPath,
                ParentFullPath: null));

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentDirectory =
                pendingDirectories.Pop();

            var pathKey =
                CreatePathComparisonKey(
                    currentDirectory.FullPath);

            if (!visitedPaths.Add(pathKey))
            {
                continue;
            }

            var inspection =
                InspectDirectory(
                    currentDirectory.FullPath);

            if (inspection.Exception is not null)
            {
                yield return new FolderEnumerationError(
                    currentDirectory.FullPath,
                    inspection.Exception);
            }

            var childDirectories =
                inspection.ChildDirectories;

            yield return new DiscoveredFolder(
                FullPath: currentDirectory.FullPath,
                Name: GetDirectoryName(
                    currentDirectory.FullPath),
                ParentFullPath:
                    currentDirectory.ParentFullPath,
                DirectSubfolderCount:
                    childDirectories.Count,
                IsAvailable:
                    inspection.Exception is null,
                IsReparsePoint:
                    inspection.IsReparsePoint);

            if (inspection.IsReparsePoint ||
                inspection.Exception is not null)
            {
                continue;
            }

            for (var index =
                 childDirectories.Count - 1;
                 index >= 0;
                 index--)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                pendingDirectories.Push(
                    new PendingDirectory(
                        childDirectories[index],
                        currentDirectory.FullPath));
            }
        }
    }

    private DirectoryInspection InspectDirectory(
        string directoryPath)
    {
        try
        {
            var attributes =
                _folderFileSystem.GetAttributes(
                    directoryPath);

            var isReparsePoint =
                attributes.HasFlag(
                    FileAttributes.ReparsePoint);

            if (isReparsePoint)
            {
                return new DirectoryInspection(
                    IsReparsePoint: true,
                    ChildDirectories: [],
                    Exception: null);
            }

            var childDirectories =
                _folderFileSystem.GetDirectories(
                    directoryPath);

            return new DirectoryInspection(
                IsReparsePoint: false,
                ChildDirectories:
                    childDirectories,
                Exception: null);
        }
        catch (Exception exception)
            when (IsRecoverable(exception))
        {
            return new DirectoryInspection(
                IsReparsePoint: false,
                ChildDirectories: [],
                Exception: exception);
        }
    }

    private static bool IsRecoverable(
        Exception exception)
    {
        return exception is
            UnauthorizedAccessException or
            IOException or
            SecurityException or
            NotSupportedException;
    }

    private static string GetDirectoryName(
        string directoryPath)
    {
        var trimmedPath =
            directoryPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        var directoryName =
            Path.GetFileName(trimmedPath);

        return string.IsNullOrWhiteSpace(directoryName)
            ? directoryPath
            : directoryName;
    }

    private static string CreatePathComparisonKey(
        string directoryPath)
    {
        var normalizedPath =
            directoryPath
                .Replace(
                    Path.AltDirectorySeparatorChar,
                    Path.DirectorySeparatorChar)
                .TrimEnd(
                    Path.DirectorySeparatorChar);

        return string.IsNullOrEmpty(normalizedPath)
            ? directoryPath
            : normalizedPath;
    }

    private sealed record PendingDirectory(
        string FullPath,
        string? ParentFullPath);

    private sealed record DirectoryInspection(
        bool IsReparsePoint,
        IReadOnlyList<string> ChildDirectories,
        Exception? Exception);
}
