using VideoArchiveFinder.Application.VideoFiles;

namespace VideoArchiveFinder.Tests.VideoFiles;

public sealed class VideoFileDragPathResolverTests
{
    [Fact]
    public void Resolve_AvailableExistingFiles_PreservesSelectionOrder()
    {
        var selectedFiles = new[]
        {
            CreateVideoFile(1, @"C:\Archive\first.mp4"),
            CreateVideoFile(2, @"C:\Archive\second.mov")
        };

        var result = VideoFileDragPathResolver.Resolve(
            selectedFiles,
            _ => true);

        Assert.Equal(
            [@"C:\Archive\first.mp4", @"C:\Archive\second.mov"],
            result.Paths);
        Assert.Equal(0, result.UnavailableCount);
    }

    [Fact]
    public void Resolve_UnavailableOrMissingFiles_SkipsThem()
    {
        var selectedFiles = new[]
        {
            CreateVideoFile(
                1,
                @"C:\Archive\offline.mp4",
                isAvailable: false),
            CreateVideoFile(2, @"C:\Archive\missing.mov"),
            CreateVideoFile(3, @"C:\Archive\ready.mxf")
        };

        var result = VideoFileDragPathResolver.Resolve(
            selectedFiles,
            path => path.EndsWith(
                "ready.mxf",
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal(
            [@"C:\Archive\ready.mxf"],
            result.Paths);
        Assert.Equal(2, result.UnavailableCount);
    }

    [Fact]
    public void Resolve_DuplicatePaths_IncludesPathOnce()
    {
        var selectedFiles = new[]
        {
            CreateVideoFile(1, @"C:\Archive\video.mp4"),
            CreateVideoFile(2, @"c:\archive\VIDEO.mp4")
        };

        var result = VideoFileDragPathResolver.Resolve(
            selectedFiles,
            _ => true);

        Assert.Equal(
            [@"C:\Archive\video.mp4"],
            result.Paths);
        Assert.Equal(0, result.UnavailableCount);
    }

    private static IndexedVideoFile CreateVideoFile(
        long id,
        string fullPath,
        bool isAvailable = true) =>
        new(
            Id: id,
            FullPath: fullPath,
            Name: Path.GetFileName(fullPath),
            NormalizedName: Path.GetFileName(fullPath).ToLowerInvariant(),
            Extension: Path.GetExtension(fullPath),
            SizeBytes: 1,
            LastWriteTimeUtc: DateTimeOffset.UnixEpoch,
            FolderFullPath: Path.GetDirectoryName(fullPath)!,
            RootSourceId: Guid.Empty,
            IsAvailable: isAvailable);
}
