namespace VideoArchiveFinder.Application.Search;

public sealed class RussianSearchStemService
    : ISearchStemService
{
    private const int MinimumStemLength = 3;

    private static readonly RelatedStemRule[]
        RelatedStemRules =
        [
            new("почт", "почт"),
            new("дорож", "дорог"),
            new("дорог", "дорог"),
            new("поезд", "поезд"),
            new("машин", "машин"),
            new("морск", "мор")
        ];

    private static readonly string[]
        RussianEndings =
        [
            "иями",
            "ьями",
            "ями",
            "ами",
            "ией",
            "иям",
            "ием",
            "иях",
            "ого",
            "ему",
            "ому",
            "ыми",
            "ими",
            "ая",
            "яя",
            "ое",
            "ее",
            "ые",
            "ие",
            "ий",
            "ый",
            "ой",
            "ей",
            "ам",
            "ям",
            "ах",
            "ях",
            "ом",
            "ем",
            "ов",
            "ев",
            "а",
            "я",
            "ы",
            "и",
            "у",
            "ю",
            "е",
            "о"
        ];

    public string GetStem(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var preparedToken = token
            .Trim()
            .ToLowerInvariant()
            .Replace('ё', 'е');

        if (preparedToken.Length == 0)
        {
            return string.Empty;
        }

        foreach (var rule in RelatedStemRules)
        {
            if (preparedToken.Contains(
                    rule.Fragment,
                    StringComparison.Ordinal))
            {
                return rule.Stem;
            }
        }

        if (!ContainsCyrillicLetter(preparedToken))
        {
            return preparedToken;
        }

        foreach (var ending in RussianEndings)
        {
            if (!preparedToken.EndsWith(
                    ending,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var stemLength =
                preparedToken.Length -
                ending.Length;

            if (stemLength >= MinimumStemLength)
            {
                return preparedToken[..stemLength];
            }
        }

        return preparedToken;
    }

    public IReadOnlyList<string> GetStems(
        IEnumerable<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var result = new List<string>();
        var uniqueStems = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var token in tokens)
        {
            var stem = GetStem(token);

            if (stem.Length > 0 &&
                uniqueStems.Add(stem))
            {
                result.Add(stem);
            }
        }

        return result;
    }

    public string CreateStemText(
        IEnumerable<string> tokens)
    {
        return string.Join(
            ' ',
            GetStems(tokens));
    }

    private static bool ContainsCyrillicLetter(
        string token)
    {
        foreach (var character in token)
        {
            if (character is >= 'а' and <= 'я')
            {
                return true;
            }
        }

        return false;
    }

    private sealed record RelatedStemRule(
        string Fragment,
        string Stem);
}
