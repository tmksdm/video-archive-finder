namespace VideoArchiveFinder.Application.Indexing;

public enum FolderIndexingStage
{
    Enumerating = 0,
    WritingBatch = 1,
    Completed = 2
}
