namespace VideoArchiveFinder.Application.ArchiveSources;

public enum ArchiveSourceAvailability
{
    Unknown = 0,
    Checking = 1,
    Available = 2,
    Unavailable = 3,
    TimedOut = 4
}
