namespace VideoArchiveFinder.Application.Search;

public interface ISearchStemService
{
    string GetStem(string token);

    IReadOnlyList<string> GetStems(
        IEnumerable<string> tokens);

    string CreateStemText(
        IEnumerable<string> tokens);
}
