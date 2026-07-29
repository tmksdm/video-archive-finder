namespace VideoArchiveFinder.Application.Search;

public interface ITextNormalizationService
{
    string Normalize(string text);

    IReadOnlyList<string> Tokenize(string text);

    string CreateTokenText(string text);
}
