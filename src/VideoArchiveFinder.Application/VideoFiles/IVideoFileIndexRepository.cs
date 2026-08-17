namespace VideoArchiveFinder.Application.VideoFiles;

public interface IVideoFileIndexRepository
{
    Task UpsertBatchAsync(
        IReadOnlyCollection<VideoFileIndexUpsertItem> files,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAnalysisAsync(
        VideoFileAnalysisUpdate update,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateThumbnailAsync(
        VideoFileThumbnailUpdate update,
        CancellationToken cancellationToken = default);

    Task<int> CompleteFolderScanAsync(
        Guid rootSourceId,
        string folderFullPath,
        DateTimeOffset scanStartedAtUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexedVideoFile>>
        GetByFolderPathAsync(
            Guid rootSourceId,
            string folderFullPath,
            CancellationToken cancellationToken = default);
}
