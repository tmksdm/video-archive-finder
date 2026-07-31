namespace VideoArchiveFinder.Application.VideoFiles;

public interface IVideoFileCandidatePolicy
{
    bool IsCandidate(string filePath);
}
