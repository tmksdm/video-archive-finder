using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Application.ArchiveSources;

public sealed record AddArchiveSourceResult(
    ArchiveSource Source,
    bool WasAdded);
