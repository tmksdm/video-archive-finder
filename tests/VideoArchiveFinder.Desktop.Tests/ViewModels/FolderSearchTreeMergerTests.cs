using VideoArchiveFinder.Application.Search;
using VideoArchiveFinder.Desktop.ViewModels;

namespace VideoArchiveFinder.Desktop.Tests.ViewModels;

public sealed class FolderSearchTreeMergerTests
{
    private const string QueryText = "фестиваль";

    private static readonly Guid RootSourceId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly FolderSearchResult Archive =
        CreateFolder(1, "Фестиваль архив");

    private static readonly FolderSearchResult Year =
        CreateFolder(2, "Фестиваль 2024", 1);

    private static readonly FolderSearchResult January =
        CreateFolder(3, "Фестиваль январь", 2);

    private static readonly FolderSearchResult Opening =
        CreateFolder(4, "Фестиваль открытие", 3);

    private static readonly FolderSearchResult Final =
        CreateFolder(5, "Фестиваль финал", 3);

    private static readonly FolderSearchResult VideoFolder =
        CreateFolder(6, "Видео", 3);

    private static readonly FolderSearchResult February =
        CreateFolder(7, "Фестиваль февраль", 2);

    private static readonly FolderSearchResult Closing =
        CreateFolder(8, "Фестиваль закрытие", 7);

    private static readonly FolderSearchResult OtherArchive =
        CreateFolder(9, "Фестиваль другой");

    private static readonly FolderSearchResult OpeningVideo =
        CreateVideo(
            -100,
            "Фестиваль запись.mkv",
            VideoFolder);

    private static readonly FolderSearchResult ClosingVideo =
        CreateVideo(
            -101,
            "Фестиваль финал.mkv",
            Closing);

    private static readonly IReadOnlyList<FolderSearchResult>
        AllFolders =
        [
            Archive,
            Year,
            January,
            Opening,
            Final,
            VideoFolder,
            February,
            Closing,
            OtherArchive
        ];

    private static readonly IReadOnlyList<FolderSearchResult>
        AllMatches =
        [
            Opening,
            Final,
            Closing,
            OtherArchive,
            OpeningVideo,
            ClosingVideo
        ];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(20)]
    public void Append_DifferentBatching_MatchesFullBuild(
        int batchSize)
    {
        var builder = CreateTreeBuilder();

        var expected = builder.Build(
            AllMatches,
            AllFolders,
            QueryText,
            FolderSearchMode.Smart);

        var merger = new FolderSearchTreeMerger();

        for (var offset = 0;
             offset < AllMatches.Count;
             offset += batchSize)
        {
            var pageMatches = AllMatches
                .Skip(offset)
                .Take(batchSize)
                .ToArray();

            var pageTree = builder.Build(
                pageMatches,
                AllFolders,
                QueryText,
                FolderSearchMode.Smart);

            merger.Append(pageTree);
        }

        AssertTreesEqual(expected, merger.Roots);

        var items = Flatten(merger.Roots).ToArray();

        Assert.Equal(
            items.Length,
            items.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public void ToNode_ReturnsCurrentItemWithoutLoadedChildren()
    {
        var builder = CreateTreeBuilder();

        var merger = new FolderSearchTreeMerger();

        foreach (var match in AllMatches)
        {
            merger.Append(builder.Build(
                [match],
                AllFolders,
                QueryText,
                FolderSearchMode.Smart));
        }

        var item = Find(merger, January.Id);
        var node = item.ToNode();

        Assert.Equal(item.Id, node.Id);
        Assert.Equal(item.FullPath, node.FullPath);
        Assert.Equal(item.Name, node.Name);

        Assert.Equal(
            item.RootSourceId,
            node.RootSourceId);

        Assert.Equal(item.IsAvailable, node.IsAvailable);
        Assert.Equal(item.IsMatch, node.IsMatch);

        Assert.Same(item.NameSegments, node.NameSegments);

        Assert.Equal(
            item.DirectSubfolderCount,
            node.DirectSubfolderCount);

        Assert.Equal(
            item.DirectVideoFileCount,
            node.DirectVideoFileCount);

        Assert.Equal(item.IsVideoFile, node.IsVideoFile);

        Assert.Equal(
            item.NavigationFolderId,
            node.NavigationFolderId);

        Assert.Equal(
            item.NavigationFolderFullPath,
            node.NavigationFolderFullPath);

        Assert.Empty(node.Children);
    }

    [Fact]
    public void Append_SamePageTwice_DoesNotDuplicateItems()
    {
        var builder = CreateTreeBuilder();

        var pageTree = builder.Build(
            [Opening, Final, OpeningVideo],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart);

        var merger = new FolderSearchTreeMerger();

        merger.Append(pageTree);

        var rootsBefore = merger.Roots.ToArray();

        var itemsBefore = Flatten(merger.Roots)
            .ToArray();

        var childrenBefore = itemsBefore.ToDictionary(
            item => item.Id,
            item => item.Children);

        merger.Append(pageTree);

        Assert.Equal(rootsBefore, merger.Roots.ToArray());

        var itemsAfter = Flatten(merger.Roots)
            .ToArray();

        Assert.Equal(itemsBefore, itemsAfter);

        Assert.Equal(
            itemsAfter.Length,
            itemsAfter
                .Select(item => item.Id)
                .Distinct()
                .Count());

        foreach (var item in itemsAfter)
        {
            Assert.Same(
                childrenBefore[item.Id],
                item.Children);
        }
    }

    [Fact]
    public void Clear_ResetsTreeAndAllowsReappending()
    {
        var builder = CreateTreeBuilder();

        var pageTree = builder.Build(
            [Opening, OpeningVideo],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart);

        var merger = new FolderSearchTreeMerger();

        merger.Append(pageTree);

        var itemBefore = Find(merger, Opening.Id);

        merger.Clear();

        Assert.Empty(merger.Roots);

        merger.Append(pageTree);

        var itemAfter = Find(merger, Opening.Id);

        Assert.NotSame(itemBefore, itemAfter);
        Assert.True(itemAfter.IsMatch);
    }

    [Fact]
    public void Append_ContextNodePromotedToMatch_UpdatesInPlace()
    {
        var builder = CreateTreeBuilder();

        var contextPage = builder.Build(
            [Opening],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart);

        var matchPage = builder.Build(
            [January],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart);

        var merger = new FolderSearchTreeMerger();

        merger.Append(contextPage);

        var january = Find(merger, January.Id);
        var opening = Find(merger, Opening.Id);
        var childrenBefore = january.Children;

        Assert.False(january.IsMatch);
        Assert.DoesNotContain(
            january.NameSegments,
            segment => segment.IsHighlighted);

        var changedProperties =
            new List<string?>();

        january.PropertyChanged +=
            (_, eventArgs) =>
                changedProperties.Add(
                    eventArgs.PropertyName);

        merger.Append(matchPage);

        var promoted = Find(merger, January.Id);

        Assert.Same(january, promoted);
        Assert.Same(childrenBefore, promoted.Children);
        Assert.Same(opening, Find(merger, Opening.Id));
        Assert.True(promoted.IsMatch);

        Assert.Contains(
            promoted.NameSegments,
            segment => segment.IsHighlighted);

        Assert.Contains(
            nameof(FolderSearchTreeItem.IsMatch),
            changedProperties);

        Assert.Contains(
            nameof(FolderSearchTreeItem.NameSegments),
            changedProperties);
    }

    [Fact]
    public void Append_Promotion_UpdatesCountersAndHighlight()
    {
        var builder = CreateTreeBuilder();

        var contextParent = CreateFolder(
            20,
            "Фестиваль папка");

        var matchParent = contextParent with
        {
            DirectSubfolderCount = 2,
            DirectVideoFileCount = 3
        };

        var child = CreateFolder(
            21,
            "Фестиваль ребёнок",
            contextParent.Id);

        var contextPage = builder.Build(
            [child],
            [contextParent, child],
            QueryText,
            FolderSearchMode.Smart);

        var matchPage = builder.Build(
            [matchParent],
            [matchParent],
            QueryText,
            FolderSearchMode.Smart);

        var merger = new FolderSearchTreeMerger();

        merger.Append(contextPage);

        var parent = Find(merger, contextParent.Id);

        Assert.False(parent.IsMatch);
        Assert.Equal(0, parent.DirectSubfolderCount);
        Assert.Equal(0, parent.DirectVideoFileCount);

        merger.Append(matchPage);

        Assert.True(parent.IsMatch);
        Assert.Equal(2, parent.DirectSubfolderCount);
        Assert.Equal(3, parent.DirectVideoFileCount);

        Assert.Contains(
            parent.NameSegments,
            segment => segment.IsHighlighted);
    }

    [Fact]
    public void Append_ContextAfterMatch_DoesNotDowngradeOrOverwrite()
    {
        var builder = CreateTreeBuilder();

        var matchPage = builder.Build(
            [January],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart);

        var contextPage = builder.Build(
            [Opening],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart);

        var merger = new FolderSearchTreeMerger();

        merger.Append(matchPage);

        var january = Find(merger, January.Id);
        var segmentsAfterMatch = january.NameSegments;

        var changedProperties =
            new List<string?>();

        january.PropertyChanged +=
            (_, eventArgs) =>
                changedProperties.Add(
                    eventArgs.PropertyName);

        merger.Append(contextPage);

        Assert.True(january.IsMatch);
        Assert.Same(
            segmentsAfterMatch,
            january.NameSegments);

        Assert.DoesNotContain(
            nameof(FolderSearchTreeItem.IsMatch),
            changedProperties);

        Assert.DoesNotContain(
            nameof(FolderSearchTreeItem.NameSegments),
            changedProperties);

        january.IsMatch = false;

        Assert.True(january.IsMatch);
    }

    [Fact]
    public void Append_SharedAncestors_AreCreatedOnce()
    {
        var builder = CreateTreeBuilder();

        var merger = new FolderSearchTreeMerger();

        merger.Append(builder.Build(
            [Opening, OpeningVideo],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart));

        merger.Append(builder.Build(
            [Final],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart));

        var items = Flatten(merger.Roots).ToArray();

        Assert.Single(
            items,
            item => item.Id == Archive.Id);

        Assert.Single(
            items,
            item => item.Id == Year.Id);

        Assert.Single(
            items,
            item => item.Id == January.Id);

        Assert.Equal(
            items.Length,
            items
                .Select(item => item.Id)
                .Distinct()
                .Count());
    }

    [Fact]
    public void Append_KeepsBuilderOrderWhenInsertingInMiddle()
    {
        var builder = CreateTreeBuilder();

        var merger = new FolderSearchTreeMerger();

        merger.Append(builder.Build(
            [OtherArchive],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart));

        merger.Append(builder.Build(
            [Opening],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart));

        merger.Append(builder.Build(
            [Final],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart));

        merger.Append(builder.Build(
            [OpeningVideo],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart));

        merger.Append(builder.Build(
            [ClosingVideo],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart));

        Assert.Equal(
            new long[] { Archive.Id, OtherArchive.Id },
            merger.Roots
                .Select(item => item.Id)
                .ToArray());

        Assert.Equal(
            new long[] { February.Id, January.Id },
            Find(merger, Year.Id).Children
                .Select(item => item.Id)
                .ToArray());

        Assert.Equal(
            new long[]
            {
                VideoFolder.Id,
                Opening.Id,
                Final.Id
            },
            Find(merger, January.Id).Children
                .Select(item => item.Id)
                .ToArray());

        Assert.Equal(
            new long[] { OpeningVideo.Id },
            Find(merger, VideoFolder.Id).Children
                .Select(item => item.Id)
                .ToArray());

        Assert.Equal(
            new long[] { Closing.Id },
            Find(merger, February.Id).Children
                .Select(item => item.Id)
                .ToArray());

        Assert.Equal(
            new long[] { ClosingVideo.Id },
            Find(merger, Closing.Id).Children
                .Select(item => item.Id)
                .ToArray());
    }

    [Fact]
    public void Append_PreservesExpansionSelectionAndIdentity()
    {
        var builder = CreateTreeBuilder();

        var merger = new FolderSearchTreeMerger();

        merger.Append(builder.Build(
            [Opening, OpeningVideo],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart));

        var year = Find(merger, Year.Id);
        var january = Find(merger, January.Id);
        var opening = Find(merger, Opening.Id);
        var video = Find(merger, OpeningVideo.Id);

        year.IsExpanded = false;
        january.IsExpanded = false;
        opening.IsSelected = true;
        video.IsSelected = true;

        var childrenBefore = Flatten(merger.Roots)
            .ToDictionary(
                item => item.Id,
                item => item.Children);

        merger.Append(builder.Build(
            [Final, ClosingVideo],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart));

        Assert.Same(year, Find(merger, Year.Id));
        Assert.Same(january, Find(merger, January.Id));
        Assert.Same(opening, Find(merger, Opening.Id));
        Assert.Same(video, Find(merger, OpeningVideo.Id));

        Assert.False(year.IsExpanded);
        Assert.False(january.IsExpanded);
        Assert.True(opening.IsSelected);
        Assert.True(video.IsSelected);

        foreach (var entry in childrenBefore)
        {
            Assert.Same(
                entry.Value,
                Find(merger, entry.Key).Children);
        }

        Assert.Contains(
            Flatten(merger.Roots),
            item => item.Id == Final.Id);
    }

    [Fact]
    public void Append_MissingAncestorLater_ReparentsRootInPlace()
    {
        var builder = CreateTreeBuilder();

        var parent = CreateFolder(
            10,
            "Фестиваль родитель");

        var child = CreateFolder(
            11,
            "Фестиваль ребёнок",
            parent.Id);

        var grandchild = CreateFolder(
            12,
            "Фестиваль внук",
            child.Id);

        var firstPage = builder.Build(
            [child],
            [child],
            QueryText,
            FolderSearchMode.Smart);

        var secondPage = builder.Build(
            [grandchild],
            [parent, child],
            QueryText,
            FolderSearchMode.Smart);

        var merger = new FolderSearchTreeMerger();

        merger.Append(firstPage);

        var childItem = Assert.Single(merger.Roots);
        var segmentsAfterMatch = childItem.NameSegments;

        Assert.Equal(child.Id, childItem.Id);
        Assert.True(childItem.IsMatch);

        merger.Append(secondPage);

        var parentItem = Assert.Single(merger.Roots);

        Assert.Equal(parent.Id, parentItem.Id);
        Assert.DoesNotContain(
            merger.Roots,
            item => item.Id == child.Id);

        var movedChild = Assert.Single(
            parentItem.Children);

        Assert.Same(childItem, movedChild);
        Assert.True(childItem.IsMatch);
        Assert.Same(
            segmentsAfterMatch,
            childItem.NameSegments);

        var grandchildItem = Assert.Single(
            childItem.Children);

        Assert.Equal(grandchild.Id, grandchildItem.Id);
    }

    [Fact]
    public void Append_CyclicPageTree_DoesNotLoopOrDuplicate()
    {
        var repeated = CreateNode(
            1,
            "Фестиваль A");

        var middle = CreateNode(
            2,
            "Фестиваль B",
            [repeated]);

        var root = CreateNode(
            1,
            "Фестиваль A",
            [middle]);

        var merger = new FolderSearchTreeMerger();

        merger.Append([root]);

        var rootItem = Assert.Single(merger.Roots);

        Assert.Equal(1, rootItem.Id);

        var middleItem = Assert.Single(
            rootItem.Children);

        Assert.Equal(2, middleItem.Id);
        Assert.Empty(middleItem.Children);
    }

    [Fact]
    public void MergedTree_KeepsVideoIdsNegativeAndSeparate()
    {
        var builder = CreateTreeBuilder();

        var merger = new FolderSearchTreeMerger();

        merger.Append(builder.Build(
            [OpeningVideo, ClosingVideo],
            AllFolders,
            QueryText,
            FolderSearchMode.Smart));

        var items = Flatten(merger.Roots).ToArray();
        var folders = items
            .Where(item => !item.IsVideoFile)
            .ToArray();

        var videos = items
            .Where(item => item.IsVideoFile)
            .ToArray();

        Assert.Equal(6, folders.Length);
        Assert.Equal(2, videos.Length);

        Assert.All(
            folders,
            folder => Assert.True(folder.Id > 0));

        Assert.All(
            videos,
            video => Assert.True(video.Id < 0));

        Assert.Equal(
            items.Length,
            items
                .Select(item => item.Id)
                .Distinct()
                .Count());

        Assert.Contains(
            videos,
            video => video.Id == OpeningVideo.Id);
    }

    [Fact]
    public void Item_IsExpandedByDefaultAndRaisesChanges()
    {
        var item = new FolderSearchTreeItem(
            CreateNode(1, "Фестиваль"));

        Assert.True(item.IsExpanded);
        Assert.False(item.IsSelected);

        var changedProperties =
            new List<string?>();

        item.PropertyChanged +=
            (_, eventArgs) =>
                changedProperties.Add(
                    eventArgs.PropertyName);

        item.IsExpanded = false;
        item.IsSelected = true;

        Assert.Contains(
            nameof(FolderSearchTreeItem.IsExpanded),
            changedProperties);

        Assert.Contains(
            nameof(FolderSearchTreeItem.IsSelected),
            changedProperties);
    }

    private static FolderSearchTreeBuilder CreateTreeBuilder()
    {
        return new FolderSearchTreeBuilder(
            new FolderNameHighlightService(
                new TextNormalizationService(),
                new RussianSearchStemService()));
    }

    private static FolderSearchResult CreateFolder(
        long id,
        string name,
        long? parentFolderId = null)
    {
        return new FolderSearchResult(
            Id: id,
            FullPath: $@"\\archive\{name}",
            Name: name,
            NormalizedName: name.ToLowerInvariant(),
            ParentFolderId: parentFolderId,
            RootSourceId: RootSourceId,
            IsAvailable: true,
            DirectSubfolderCount: 0,
            DirectVideoFileCount: 0);
    }

    private static FolderSearchResult CreateVideo(
        long id,
        string name,
        FolderSearchResult folder)
    {
        return new FolderSearchResult(
            Id: id,
            FullPath: $@"{folder.FullPath}\{name}",
            Name: name,
            NormalizedName: name.ToLowerInvariant(),
            ParentFolderId: folder.Id,
            RootSourceId: RootSourceId,
            IsAvailable: true,
            DirectSubfolderCount: 0,
            DirectVideoFileCount: 0,
            IsVideoFile: true,
            NavigationFolderId: folder.Id,
            NavigationFolderFullPath:
                folder.FullPath);
    }

    private static FolderSearchTreeNode CreateNode(
        long id,
        string name,
        IReadOnlyList<FolderSearchTreeNode>?
            children = null)
    {
        return new FolderSearchTreeNode(
            Id: id,
            FullPath: $@"\\archive\{name}",
            Name: name,
            RootSourceId: RootSourceId,
            IsAvailable: true,
            IsMatch: true,
            NameSegments:
            [
                new FolderNameTextSegment(
                    name,
                    IsHighlighted: true)
            ],
            Children: children ?? []);
    }

    private static FolderSearchTreeItem Find(
        FolderSearchTreeMerger merger,
        long id)
    {
        return Flatten(merger.Roots)
            .Single(item => item.Id == id);
    }

    private static IEnumerable<FolderSearchTreeItem> Flatten(
        IEnumerable<FolderSearchTreeItem> items)
    {
        foreach (var item in items)
        {
            yield return item;

            foreach (var child in Flatten(item.Children))
            {
                yield return child;
            }
        }
    }

    private static void AssertTreesEqual(
        IReadOnlyList<FolderSearchTreeNode> expected,
        IReadOnlyList<FolderSearchTreeItem> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (var index = 0;
             index < expected.Count;
             index++)
        {
            AssertItemMatchesNode(
                expected[index],
                actual[index]);
        }
    }

    private static void AssertItemMatchesNode(
        FolderSearchTreeNode expected,
        FolderSearchTreeItem actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.FullPath, actual.FullPath);
        Assert.Equal(expected.Name, actual.Name);

        Assert.Equal(
            expected.RootSourceId,
            actual.RootSourceId);

        Assert.Equal(
            expected.IsAvailable,
            actual.IsAvailable);

        Assert.Equal(expected.IsMatch, actual.IsMatch);

        Assert.Equal(
            expected.DirectSubfolderCount,
            actual.DirectSubfolderCount);

        Assert.Equal(
            expected.DirectVideoFileCount,
            actual.DirectVideoFileCount);

        Assert.Equal(
            expected.IsVideoFile,
            actual.IsVideoFile);

        Assert.Equal(
            expected.NavigationFolderId,
            actual.NavigationFolderId);

        Assert.Equal(
            expected.NavigationFolderFullPath,
            actual.NavigationFolderFullPath);

        Assert.Equal(
            expected.NameSegments.Select(
                segment => (
                    segment.Text,
                    segment.IsHighlighted)),
            actual.NameSegments.Select(
                segment => (
                    segment.Text,
                    segment.IsHighlighted)));

        Assert.Equal(
            expected.Children.Count,
            actual.Children.Count);

        for (var index = 0;
             index < expected.Children.Count;
             index++)
        {
            AssertItemMatchesNode(
                expected.Children[index],
                actual.Children[index]);
        }
    }

}
