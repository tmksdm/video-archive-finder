using VideoArchiveFinder.Infrastructure.Indexing;

namespace VideoArchiveFinder.Tests.VideoFiles;

public sealed class SystemVideoFileSystemTests
    : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "VideoArchiveFinder.Tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetFiles_ReturnsOnlyImmediateFiles()
    {
        Directory.CreateDirectory(
            _temporaryDirectory);

        var immediateFilePath =
            Path.Combine(
                _temporaryDirectory,
                "immediate.mp4");

        File.WriteAllText(
            immediateFilePath,
            "video");

        var nestedDirectory =
            Path.Combine(
                _temporaryDirectory,
                "Nested");

        Directory.CreateDirectory(
            nestedDirectory);

        var nestedFilePath =
            Path.Combine(
                nestedDirectory,
                "nested.mp4");

        File.WriteAllText(
            nestedFilePath,
            "nested video");

        var fileSystem =
            new SystemVideoFileSystem();

        var files =
            fileSystem.GetFiles(
                _temporaryDirectory);

        var file =
            Assert.Single(files);

        Assert.Equal(
            immediateFilePath,
            file);

        Assert.DoesNotContain(
            nestedFilePath,
            files);
    }

    [Fact]
    public void GetMetadata_ReturnsFileSizeAndUtcTimestamp()
    {
        Directory.CreateDirectory(
            _temporaryDirectory);

        var filePath =
            Path.Combine(
                _temporaryDirectory,
                "video.mp4");

        var content =
            new byte[]
            {
                1,
                2,
                3,
                4,
                5
            };

        File.WriteAllBytes(
            filePath,
            content);

        var fileSystem =
            new SystemVideoFileSystem();

        var metadata =
            fileSystem.GetMetadata(
                filePath);

        Assert.Equal(
            content.LongLength,
            metadata.SizeBytes);

        Assert.Equal(
            TimeSpan.Zero,
            metadata.LastWriteTimeUtc.Offset);

        Assert.True(
            metadata.LastWriteTimeUtc <=
            DateTimeOffset.UtcNow);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(
                _temporaryDirectory,
                recursive: true);
        }
    }
}
