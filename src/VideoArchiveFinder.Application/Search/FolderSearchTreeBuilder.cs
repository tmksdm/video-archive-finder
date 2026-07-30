namespace VideoArchiveFinder.Application.Search;

public sealed class FolderSearchTreeBuilder
{
    private readonly IFolderNameHighlightService
        _folderNameHighlightService;

    public FolderSearchTreeBuilder(
        IFolderNameHighlightService folderNameHighlightService)
    {
        _folderNameHighlightService =
            folderNameHighlightService;
    }

    public IReadOnlyList<FolderSearchTreeNode> Build(
        IReadOnlyCollection<FolderSearchResult> matches,
        IReadOnlyCollection<FolderSearchResult> availableFolders,
        string queryText = "",
        FolderSearchMode searchMode = FolderSearchMode.Smart)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(availableFolders);
        ArgumentNullException.ThrowIfNull(queryText);

        if (matches.Count == 0)
        {
            return [];
        }

        var foldersById = availableFolders
            .GroupBy(folder => folder.Id)
            .ToDictionary(
                group => group.Key,
                group => group.First());

        foreach (var match in matches)
        {
            foldersById[match.Id] = match;
        }

        var matchIds = matches
            .Select(match => match.Id)
            .ToHashSet();

        var includedIds = FindIncludedFolderIds(
            matches,
            foldersById);

        var childrenByParentId =
            new Dictionary<long, List<FolderSearchResult>>();

        var roots = new List<FolderSearchResult>();

        foreach (var folderId in includedIds)
        {
            var folder = foldersById[folderId];

            if (folder.ParentFolderId is long parentId &&
                parentId != folder.Id &&
                includedIds.Contains(parentId))
            {
                if (!childrenByParentId.TryGetValue(
                        parentId,
                        out var children))
                {
                    children = [];
                    childrenByParentId[parentId] = children;
                }

                children.Add(folder);
            }
            else
            {
                roots.Add(folder);
            }
        }

        return roots
            .OrderBy(
                folder => folder.Name,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                folder => folder.FullPath,
                StringComparer.OrdinalIgnoreCase)
            .Select(folder => CreateNode(
                folder,
                matchIds,
                childrenByParentId,
                [],
                queryText,
                searchMode))
            .ToArray();
    }

    private static HashSet<long> FindIncludedFolderIds(
        IReadOnlyCollection<FolderSearchResult> matches,
        IReadOnlyDictionary<long, FolderSearchResult> foldersById)
    {
        var includedIds = new HashSet<long>();

        foreach (var match in matches)
        {
            var current = match;
            var visitedIds = new HashSet<long>();

            while (visitedIds.Add(current.Id))
            {
                includedIds.Add(current.Id);

                if (current.ParentFolderId is not long parentId ||
                    !foldersById.TryGetValue(
                        parentId,
                        out var parent))
                {
                    break;
                }

                current = parent;
            }
        }

        return includedIds;
    }

    private FolderSearchTreeNode CreateNode(
        FolderSearchResult folder,
        IReadOnlySet<long> matchIds,
        IReadOnlyDictionary<long, List<FolderSearchResult>>
            childrenByParentId,
        HashSet<long> ancestorIds,
        string queryText,
        FolderSearchMode searchMode)
    {
        var currentAncestorIds =
            new HashSet<long>(ancestorIds)
            {
                folder.Id
            };

        var children = childrenByParentId.TryGetValue(
                folder.Id,
                out var childFolders)
            ? childFolders
                .Where(child =>
                    !currentAncestorIds.Contains(child.Id))
                .OrderBy(
                    child => child.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    child => child.FullPath,
                    StringComparer.OrdinalIgnoreCase)
                .Select(child => CreateNode(
                    child,
                    matchIds,
                    childrenByParentId,
                    currentAncestorIds,
                    queryText,
                    searchMode))
                .ToArray()
            : [];

        var isMatch = matchIds.Contains(folder.Id);

        var nameSegments = isMatch
            ? _folderNameHighlightService.CreateSegments(
                folder.Name,
                queryText,
                searchMode)
            :
            [
                new FolderNameTextSegment(
                    folder.Name,
                    IsHighlighted: false)
            ];

        return new FolderSearchTreeNode(
            Id: folder.Id,
            FullPath: folder.FullPath,
            Name: folder.Name,
            RootSourceId: folder.RootSourceId,
            IsAvailable: folder.IsAvailable,
            IsMatch: isMatch,
            NameSegments: nameSegments,
            Children: children);
    }
}
