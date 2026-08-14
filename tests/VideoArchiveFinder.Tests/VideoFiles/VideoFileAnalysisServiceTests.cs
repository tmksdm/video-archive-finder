using Microsoft.Extensions.Logging.Abstractions;
using VideoArchiveFinder.Application.ExternalTools;
using VideoArchiveFinder.Application.VideoFiles;
using VideoArchiveFinder.Infrastructure.ExternalTools;

namespace VideoArchiveFinder.Tests.VideoFiles;

public sealed class VideoFileAnalysisServiceTests
{
    private static readonly Guid RootSourceId =
        Guid.Parse(
            "42b19496-b48c-4af2-b6fb-176b36db881b");

    private const string VideoPath =
        @"C:\Archive\Folder\Video.mp4";

    [Fact]
    public async Task AnalyzeAsync_SuccessfulVideo_StoresMetadata()
    {
        const string json =
            """
            {
              "streams": [
                {
                  "codec_type": "video",
                  "codec_name": "h264",
                  "width": 1920,
                  "height": 1080
                }
              ],
              "format": {
                "duration": "12.5"
              }
            }
            """;

        var repository =
            new TestVideoFileIndexRepository();

        var service = CreateService(
            CreateSuccessfulRunner(json),
            repository);

        var result =
            await service.AnalyzeAsync(
                RootSourceId,
                VideoPath);

        Assert.True(result.WasStored);
        Assert.Equal(
            VideoFileAnalysisState.Succeeded,
            result.State);

        var update =
            Assert.IsType<VideoFileAnalysisUpdate>(
                repository.LastUpdate);

        Assert.Equal(RootSourceId, update.RootSourceId);
        Assert.Equal(VideoPath, update.FullPath);
        Assert.Equal(
            VideoFileAnalysisState.Succeeded,
            update.State);

        Assert.True(update.HasVideoStream);
        Assert.Equal(
            TimeSpan.FromSeconds(12.5),
            update.Duration);

        Assert.Equal(1920, update.Width);
        Assert.Equal(1080, update.Height);
        Assert.Equal("h264", update.Codec);
    }

    [Fact]
    public async Task AnalyzeAsync_NoVideoStream_StoresSuccessfulResult()
    {
        const string json =
            """
            {
              "streams": [
                {
                  "codec_type": "audio",
                  "codec_name": "aac"
                }
              ],
              "format": {
                "duration": "3.25"
              }
            }
            """;

        var repository =
            new TestVideoFileIndexRepository();

        var service = CreateService(
            CreateSuccessfulRunner(json),
            repository);

        var result =
            await service.AnalyzeAsync(
                RootSourceId,
                VideoPath);

        Assert.True(result.WasStored);
        Assert.Equal(
            VideoFileAnalysisState.Succeeded,
            result.State);
        Assert.False(result.HasVideoStream);

        var update =
            Assert.IsType<VideoFileAnalysisUpdate>(
                repository.LastUpdate);

        Assert.Equal(
            VideoFileAnalysisState.Succeeded,
            update.State);

        Assert.False(update.HasVideoStream);
        Assert.Equal(
            TimeSpan.FromSeconds(3.25),
            update.Duration);

        Assert.Null(update.Width);
        Assert.Null(update.Height);
        Assert.Null(update.Codec);
    }

    [Fact]
    public async Task AnalyzeAsync_FfprobeFailure_StoresFailedState()
    {
        var runner =
            new TestFfprobeRunner(
                new FfprobeRunResult(
                    FfprobeRunStatus.Failed,
                    string.Empty,
                    1,
                    "FFprobe failed."));

        var repository =
            new TestVideoFileIndexRepository();

        var service = CreateService(
            runner,
            repository);

        var result =
            await service.AnalyzeAsync(
                RootSourceId,
                VideoPath);

        Assert.True(result.WasStored);
        Assert.Equal(
            VideoFileAnalysisState.Failed,
            result.State);
        Assert.Null(result.HasVideoStream);

        Assert.Equal(
            "FFprobe failed.",
            result.DiagnosticMessage);

        AssertFailedUpdate(repository.LastUpdate);
    }

    [Fact]
    public async Task AnalyzeAsync_InvalidJson_StoresFailedState()
    {
        const string damagedJson =
            """
            {
              "streams": [
            """;

        var repository =
            new TestVideoFileIndexRepository();

        var service = CreateService(
            CreateSuccessfulRunner(damagedJson),
            repository);

        var result =
            await service.AnalyzeAsync(
                RootSourceId,
                VideoPath);

        Assert.True(result.WasStored);
        Assert.Equal(
            VideoFileAnalysisState.Failed,
            result.State);
        Assert.Null(result.HasVideoStream);

        Assert.Contains(
            "Не удалось разобрать JSON FFprobe",
            result.DiagnosticMessage);

        AssertFailedUpdate(repository.LastUpdate);
    }

    [Fact]
    public async Task AnalyzeAsync_FileNotInIndex_ReturnsNotStored()
    {
        const string json =
            """
            {
              "streams": [
                {
                  "codec_type": "video"
                }
              ],
              "format": {}
            }
            """;

        var repository =
            new TestVideoFileIndexRepository
            {
                UpdateResult = false
            };

        var service = CreateService(
            CreateSuccessfulRunner(json),
            repository);

        var result =
            await service.AnalyzeAsync(
                RootSourceId,
                VideoPath);

        Assert.False(result.WasStored);
        Assert.Equal(
            VideoFileAnalysisState.Succeeded,
            result.State);

        Assert.NotNull(repository.LastUpdate);
    }

    private static VideoFileAnalysisService CreateService(
        IFfprobeRunner runner,
        IVideoFileIndexRepository repository)
    {
        return new VideoFileAnalysisService(
            runner,
            new FfprobeJsonParser(),
            repository,
            NullLogger<
                VideoFileAnalysisService>.Instance);
    }

    private static TestFfprobeRunner
        CreateSuccessfulRunner(string json)
    {
        return new TestFfprobeRunner(
            new FfprobeRunResult(
                FfprobeRunStatus.Succeeded,
                json,
                0,
                "FFprobe succeeded."));
    }

    private static void AssertFailedUpdate(
        VideoFileAnalysisUpdate? update)
    {
        var failedUpdate =
            Assert.IsType<VideoFileAnalysisUpdate>(
                update);

        Assert.Equal(
            VideoFileAnalysisState.Failed,
            failedUpdate.State);

        Assert.Null(failedUpdate.HasVideoStream);
        Assert.Null(failedUpdate.Duration);
        Assert.Null(failedUpdate.Width);
        Assert.Null(failedUpdate.Height);
        Assert.Null(failedUpdate.Codec);
    }

    private sealed class TestFfprobeRunner
        : IFfprobeRunner
    {
        private readonly FfprobeRunResult _result;

        public TestFfprobeRunner(
            FfprobeRunResult result)
        {
            _result = result;
        }

        public Task<FfprobeRunResult> RunAsync(
            string videoPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            return Task.FromResult(_result);
        }
    }

    private sealed class TestVideoFileIndexRepository
        : IVideoFileIndexRepository
    {
        public bool UpdateResult { get; init; } = true;

        public VideoFileAnalysisUpdate? LastUpdate
        {
            get;
            private set;
        }

        public Task UpsertBatchAsync(
            IReadOnlyCollection<VideoFileIndexUpsertItem> files,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> UpdateAnalysisAsync(
            VideoFileAnalysisUpdate update,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            LastUpdate = update;

            return Task.FromResult(UpdateResult);
        }

        public Task<int> CompleteFolderScanAsync(
            Guid rootSourceId,
            string folderFullPath,
            DateTimeOffset scanStartedAtUtc,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<IndexedVideoFile>>
            GetByFolderPathAsync(
                Guid rootSourceId,
                string folderFullPath,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
