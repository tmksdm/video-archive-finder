using VideoArchiveFinder.Application.Search;

namespace VideoArchiveFinder.Tests.Search;

public sealed class FolderSearchTreeBuilderTests
{
    private static readonly Guid DefaultSourceId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly FolderSearchTreeBuilder _builder = new();

    [Fact]
    public void Build_CreatesParentChainForMatch()
    {
        var root = CreateFolder(
            id: 1,
            name: "Архив");

        var section = CreateFolder(
            id: 2,
            name: "Транспорт",
            parentFolderId: root.Id);

        var match = CreateFolder(
            id: 3,
            name: "Железная дорога",
            parentFolderId: section.Id);

        var result = _builder.Build(
            matches: [match],
            availableFolders: [root, section, match]);

        var rootNode = Assert.Single(result);
        Assert.Equal("Архив", rootNode.Name);
        Assert.False(rootNode.IsMatch);

        var sectionNode = Assert.Single(rootNode.Children);
        Assert.Equal("Транспорт", sectionNode.Name);
        Assert.False(sectionNode.IsMatch);

        var matchNode = Assert.Single(sectionNode.Children);
        Assert.Equal("Железная дорога", matchNode.Name);
        Assert.True(matchNode.IsMatch);
        Assert.Empty(matchNode.Children);
    }

    [Fact]
    public void Build_MergesMatchesWithSharedParent()
    {
        var root = CreateFolder(
            id: 1,
            name: "Архив");

        var firstMatch = CreateFolder(
            id: 2,
            name: "Автомобильная дорога",
            parentFolderId: root.Id);

        var secondMatch = CreateFolder(
            id: 3,
            name: "Железная дорога",
            parentFolderId: root.Id);

        var result = _builder.Build(
            matches: [firstMatch, secondMatch],
            availableFolders:
            [
                root,
                firstMatch,
                secondMatch
            ]);

        var rootNode = Assert.Single(result);

        Assert.False(rootNode.IsMatch);
        Assert.Equal(2, rootNode.Children.Count);

        Assert.Collection(
            rootNode.Children,
            node =>
            {
                Assert.Equal(
                    "Автомобильная дорога",
                    node.Name);

                Assert.True(node.IsMatch);
            },
            node =>
            {
                Assert.Equal(
                    "Железная дорога",
                    node.Name);

                Assert.True(node.IsMatch);
            });
    }

    [Fact]
    public void Build_MarksParentAndChildWhenBothMatch()
    {
        var parentMatch = CreateFolder(
            id: 1,
            name: "Дорога");

        var childMatch = CreateFolder(
            id: 2,
            name: "Дорожные работы",
            parentFolderId: parentMatch.Id);

        var result = _builder.Build(
            matches: [parentMatch, childMatch],
            availableFolders: [parentMatch, childMatch]);

        var parentNode = Assert.Single(result);
        Assert.True(parentNode.IsMatch);

        var childNode = Assert.Single(parentNode.Children);
        Assert.True(childNode.IsMatch);
    }

    [Fact]
    public void Build_DoesNotIncludeUnrelatedChildFolder()
    {
        var root = CreateFolder(
            id: 1,
            name: "Архив");

        var match = CreateFolder(
            id: 2,
            name: "Железная дорога",
            parentFolderId: root.Id);

        var unrelatedChild = CreateFolder(
            id: 3,
            name: "Вторая речка",
            parentFolderId: root.Id);

        var result = _builder.Build(
            matches: [match],
            availableFolders:
            [
                root,
                match,
                unrelatedChild
            ]);

        var rootNode = Assert.Single(result);
        var childNode = Assert.Single(rootNode.Children);

        Assert.Equal("Железная дорога", childNode.Name);
        Assert.DoesNotContain(
            rootNode.Children,
            node => node.Id == unrelatedChild.Id);
    }

    [Fact]
    public void Build_KeepsDifferentSourcesInSeparateTrees()
    {
        var firstSourceId =
            Guid.Parse(
                "11111111-1111-1111-1111-111111111111");

        var secondSourceId =
            Guid.Parse(
                "22222222-2222-2222-2222-222222222222");

        var firstRoot = CreateFolder(
            id: 1,
            name: "Первый архив",
            rootSourceId: firstSourceId);

        var firstMatch = CreateFolder(
            id: 2,
            name: "Первая дорога",
            parentFolderId: firstRoot.Id,
            rootSourceId: firstSourceId);

        var secondRoot = CreateFolder(
            id: 3,
            name: "Второй архив",
            rootSourceId: secondSourceId);

        var secondMatch = CreateFolder(
            id: 4,
            name: "Вторая дорога",
            parentFolderId: secondRoot.Id,
            rootSourceId: secondSourceId);

        var result = _builder.Build(
            matches: [firstMatch, secondMatch],
            availableFolders:
            [
                firstRoot,
                firstMatch,
                secondRoot,
                secondMatch
            ]);

        Assert.Equal(2, result.Count);

        Assert.Contains(
            result,
            node =>
                node.RootSourceId == firstSourceId &&
                Assert.Single(node.Children).Id ==
                firstMatch.Id);

        Assert.Contains(
            result,
            node =>
                node.RootSourceId == secondSourceId &&
                Assert.Single(node.Children).Id ==
                secondMatch.Id);
    }

    private static FolderSearchResult CreateFolder(
        long id,
        string name,
        long? parentFolderId = null,
        Guid? rootSourceId = null)
    {
        return new FolderSearchResult(
            Id: id,
            FullPath: $@"C:\Archive\{name}",
            Name: name,
            NormalizedName: name.ToLowerInvariant(),
            ParentFolderId: parentFolderId,
            RootSourceId: rootSourceId ?? DefaultSourceId,
            IsAvailable: true,
            DirectSubfolderCount: 0,
            DirectVideoFileCount: 0);
    }
}
