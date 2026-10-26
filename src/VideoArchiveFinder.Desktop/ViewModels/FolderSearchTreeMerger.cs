using System.Collections.ObjectModel;
using VideoArchiveFinder.Application.Search;

namespace VideoArchiveFinder.Desktop.ViewModels;

public sealed class FolderSearchTreeMerger
{
    private static readonly StringComparer NameComparer =
        StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<long, FolderSearchTreeItem>
        _itemsById = [];

    private readonly Dictionary<
        long,
        FolderSearchTreeItem?> _parentById = [];

    public ObservableCollection<FolderSearchTreeItem> Roots
    {
        get;
    } = [];

    public void Append(
        IReadOnlyList<FolderSearchTreeNode> pageTree)
    {
        ArgumentNullException.ThrowIfNull(pageTree);

        var ancestorIds = new HashSet<long>();

        foreach (var node in pageTree)
        {
            MergeNode(
                node,
                parent: null,
                ancestorIds);
        }
    }

    public void Clear()
    {
        _itemsById.Clear();
        _parentById.Clear();
        Roots.Clear();
    }

    private void MergeNode(
        FolderSearchTreeNode node,
        FolderSearchTreeItem? parent,
        HashSet<long> ancestorIds)
    {
        if (!ancestorIds.Add(node.Id))
        {
            return;
        }

        try
        {
            if (!_itemsById.TryGetValue(
                    node.Id,
                    out var item))
            {
                item = new FolderSearchTreeItem(node);
                _itemsById.Add(node.Id, item);
                _parentById.Add(node.Id, parent);

                InsertSorted(
                    GetChildren(parent),
                    item);
            }
            else
            {
                item.Apply(node);

                if (!TryReparent(item, parent))
                {
                    return;
                }
            }

            foreach (var child in node.Children)
            {
                MergeNode(
                    child,
                    item,
                    ancestorIds);
            }
        }
        finally
        {
            ancestorIds.Remove(node.Id);
        }
    }

    private bool TryReparent(
        FolderSearchTreeItem item,
        FolderSearchTreeItem? parent)
    {
        if (!_parentById.TryGetValue(
                item.Id,
                out var currentParent))
        {
            return false;
        }

        if (ReferenceEquals(currentParent, parent))
        {
            return true;
        }

        if (WouldCreateCycle(item, parent))
        {
            return false;
        }

        if (currentParent is null)
        {
            Roots.Remove(item);
        }
        else
        {
            currentParent.Children.Remove(item);
        }

        _parentById[item.Id] = parent;

        InsertSorted(
            GetChildren(parent),
            item);

        return true;
    }

    private bool WouldCreateCycle(
        FolderSearchTreeItem item,
        FolderSearchTreeItem? parent)
    {
        var visitedIds = new HashSet<long>();
        var current = parent;

        while (current is not null &&
               visitedIds.Add(current.Id))
        {
            if (ReferenceEquals(current, item))
            {
                return true;
            }

            current = _parentById.TryGetValue(
                    current.Id,
                    out var next)
                ? next
                : null;
        }

        return false;
    }

    private ObservableCollection<FolderSearchTreeItem>
        GetChildren(FolderSearchTreeItem? parent)
    {
        return parent?.Children ?? Roots;
    }

    private static void InsertSorted(
        ObservableCollection<FolderSearchTreeItem> children,
        FolderSearchTreeItem item)
    {
        var low = 0;
        var high = children.Count;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);

            if (Compare(children[middle], item) <= 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        children.Insert(low, item);
    }

    private static int Compare(
        FolderSearchTreeItem left,
        FolderSearchTreeItem right)
    {
        var nameComparison = NameComparer.Compare(
            left.Name,
            right.Name);

        return nameComparison != 0
            ? nameComparison
            : NameComparer.Compare(
                left.FullPath,
                right.FullPath);
    }
}
