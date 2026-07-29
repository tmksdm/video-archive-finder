using System.Text;

namespace VideoArchiveFinder.Application.Search;

public sealed class TextNormalizationService
    : ITextNormalizationService
{
    public string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var preparedText = text
            .Normalize(NormalizationForm.FormC)
            .Trim()
            .ToLowerInvariant()
            .Replace('ё', 'е');

        var result = new StringBuilder(
            preparedText.Length);

        var previousCharacterWasWhitespace = false;

        foreach (var character in preparedText)
        {
            if (char.IsWhiteSpace(character))
            {
                if (result.Length > 0 &&
                    !previousCharacterWasWhitespace)
                {
                    result.Append(' ');
                }

                previousCharacterWasWhitespace = true;
                continue;
            }

            result.Append(character);
            previousCharacterWasWhitespace = false;
        }

        return result
            .ToString()
            .TrimEnd();
    }

    public IReadOnlyList<string> Tokenize(
        string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalizedText = Normalize(text);

        if (normalizedText.Length == 0)
        {
            return [];
        }

        var tokens = new List<string>();
        var currentToken = new StringBuilder();

        foreach (var character in normalizedText)
        {
            if (char.IsLetterOrDigit(character))
            {
                currentToken.Append(character);
                continue;
            }

            AddCurrentToken(tokens, currentToken);
        }

        AddCurrentToken(tokens, currentToken);

        return tokens;
    }

    public string CreateTokenText(
        string text)
    {
        return string.Join(
            ' ',
            Tokenize(text));
    }

    private static void AddCurrentToken(
        ICollection<string> tokens,
        StringBuilder currentToken)
    {
        if (currentToken.Length == 0)
        {
            return;
        }

        tokens.Add(currentToken.ToString());
        currentToken.Clear();
    }
}
