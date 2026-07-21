using System.Text.Json.Serialization;

namespace VideoArchiveFinder.Domain.ArchiveSources;

public sealed record ArchiveSource
{
    [JsonConstructor]
    public ArchiveSource(
        Guid id,
        string displayName,
        string fullPath,
        ArchiveSourceType sourceType,
        DateTimeOffset addedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Source identifier cannot be empty.",
                nameof(id));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(
                "Source display name cannot be empty.",
                nameof(displayName));
        }

        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException(
                "Source path cannot be empty.",
                nameof(fullPath));
        }

        var normalizedPath = NormalizePath(fullPath);
        var detectedType = DetectSourceType(normalizedPath);

        if (detectedType != sourceType)
        {
            throw new ArgumentException(
                "Source type does not match its path.",
                nameof(sourceType));
        }

        Id = id;
        DisplayName = displayName.Trim();
        FullPath = normalizedPath;
        SourceType = sourceType;
        AddedAtUtc = addedAtUtc;
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public string FullPath { get; }

    public ArchiveSourceType SourceType { get; }

    public DateTimeOffset AddedAtUtc { get; }

    public static ArchiveSource Create(
        string fullPath,
        string? displayName = null)
    {
        var normalizedPath = NormalizePath(fullPath);
        var sourceType = DetectSourceType(normalizedPath);
        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? CreateDefaultDisplayName(normalizedPath)
            : displayName.Trim();

        return new ArchiveSource(
            Guid.NewGuid(),
            resolvedDisplayName,
            normalizedPath,
            sourceType,
            DateTimeOffset.UtcNow);
    }

    private static string NormalizePath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException(
                "Source path cannot be empty.",
                nameof(fullPath));
        }

        var path = fullPath.Trim();

        if (IsLocalPath(path) && path.Length == 3)
        {
            return path;
        }

        var trimmedPath = path.TrimEnd('\\', '/');

        return string.IsNullOrEmpty(trimmedPath)
            ? path
            : trimmedPath;
    }

    private static ArchiveSourceType DetectSourceType(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = path[2..].Split(
                '\\',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

            if (parts.Length < 2)
            {
                throw new ArgumentException(
                    "UNC path must contain a server and share name.",
                    nameof(path));
            }

            return ArchiveSourceType.UncPath;
        }

        if (IsLocalPath(path))
        {
            return ArchiveSourceType.LocalFolder;
        }

        throw new ArgumentException(
            "Source path must be an absolute local path or UNC path.",
            nameof(path));
    }

    private static bool IsLocalPath(string path)
    {
        return path.Length >= 3
            && char.IsAsciiLetter(path[0])
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/');
    }

    private static string CreateDefaultDisplayName(string path)
    {
        if (IsLocalPath(path) && path.Length == 3)
        {
            return path;
        }

        var lastSeparatorIndex = path.LastIndexOfAny(['\\', '/']);

        if (lastSeparatorIndex >= 0 &&
            lastSeparatorIndex < path.Length - 1)
        {
            return path[(lastSeparatorIndex + 1)..];
        }

        return path;
    }
}
