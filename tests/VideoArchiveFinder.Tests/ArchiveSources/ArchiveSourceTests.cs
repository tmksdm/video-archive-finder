using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Tests.ArchiveSources;

public sealed class ArchiveSourceTests
{
    [Fact]
    public void Create_LocalFolder_CreatesLocalSource()
    {
        var source = ArchiveSource.Create(
            @"C:\Video Archive\News",
            "овости");

        Assert.NotEqual(Guid.Empty, source.Id);
        Assert.Equal("овости", source.DisplayName);
        Assert.Equal(
            @"C:\Video Archive\News",
            source.FullPath);
        Assert.Equal(
            ArchiveSourceType.LocalFolder,
            source.SourceType);
    }

    [Fact]
    public void Create_UncPath_CreatesUncSource()
    {
        var source = ArchiveSource.Create(
            @"\\media-server\archive\2026");

        Assert.Equal("2026", source.DisplayName);
        Assert.Equal(
            ArchiveSourceType.UncPath,
            source.SourceType);
    }

    [Fact]
    public void Create_RelativePath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => ArchiveSource.Create(@"archive\video"));
    }

    [Fact]
    public void Create_IncompleteUncPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => ArchiveSource.Create(@"\\media-server"));
    }
}
