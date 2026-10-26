using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoArchiveFinder.Application.Search;

namespace VideoArchiveFinder.Desktop.ViewModels;

public sealed partial class FolderSearchTreeItem
    : ObservableObject
{
    private bool _isMatch;

    private IReadOnlyList<FolderNameTextSegment>
        _nameSegments;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int _directSubfolderCount;

    [ObservableProperty]
    private int _directVideoFileCount;

    public FolderSearchTreeItem(
        FolderSearchTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        Id = node.Id;
        FullPath = node.FullPath;
        Name = node.Name;
        RootSourceId = node.RootSourceId;
        IsAvailable = node.IsAvailable;
        IsVideoFile = node.IsVideoFile;
        NavigationFolderId = node.NavigationFolderId;

        NavigationFolderFullPath =
            node.NavigationFolderFullPath;

        _isMatch = node.IsMatch;
        _nameSegments = node.NameSegments;
        _directSubfolderCount =
            node.DirectSubfolderCount;

        _directVideoFileCount =
            node.DirectVideoFileCount;
    }

    public long Id { get; }

    public string FullPath { get; }

    public string Name { get; }

    public Guid RootSourceId { get; }

    public bool IsAvailable { get; }

    public bool IsVideoFile { get; }

    public long? NavigationFolderId { get; }

    public string? NavigationFolderFullPath { get; }

    public bool IsMatch
    {
        get => _isMatch;
        set
        {
            if (_isMatch || !value)
            {
                return;
            }

            _isMatch = true;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<FolderNameTextSegment>
        NameSegments
    {
        get => _nameSegments;
        private set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetProperty(ref _nameSegments, value);
        }
    }

    public ObservableCollection<FolderSearchTreeItem>
        Children
    {
        get;
    } = [];

    public void PromoteToMatch(
        IReadOnlyList<FolderNameTextSegment>
            nameSegments)
    {
        ArgumentNullException.ThrowIfNull(nameSegments);

        if (IsMatch)
        {
            return;
        }

        IsMatch = true;
        NameSegments = nameSegments;
    }

    internal void Apply(FolderSearchTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.IsMatch)
        {
            PromoteToMatch(node.NameSegments);

            DirectSubfolderCount =
                node.DirectSubfolderCount;

            DirectVideoFileCount =
                node.DirectVideoFileCount;

            return;
        }

        if (IsMatch)
        {
            return;
        }

        DirectSubfolderCount =
            node.DirectSubfolderCount;

        DirectVideoFileCount =
            node.DirectVideoFileCount;
    }

    public FolderSearchTreeNode ToNode()
    {
        return new FolderSearchTreeNode(
            Id,
            FullPath,
            Name,
            RootSourceId,
            IsAvailable,
            IsMatch,
            NameSegments,
            [],
            DirectSubfolderCount,
            DirectVideoFileCount,
            IsVideoFile,
            NavigationFolderId,
            NavigationFolderFullPath);
    }
}
