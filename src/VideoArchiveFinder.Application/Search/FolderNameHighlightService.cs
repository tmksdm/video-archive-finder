using System.Text;

namespace VideoArchiveFinder.Application.Search;

public sealed class FolderNameHighlightService
    : IFolderNameHighlightService
{
    private const int MinimumSmartPrefixLength = 3;

    private readonly ITextNormalizationService
        _textNormalizationService;

    private readonly ISearchStemService
        _searchStemService;

    public FolderNameHighlightService(
        ITextNormalizationService textNormalizationService,
        ISearchStemService searchStemService)
    {
        _textNormalizationService = textNormalizationService;
        _searchStemService = searchStemService;
    }

    public IReadOnlyList<FolderNameTextSegment> CreateSegments(
        string folderName,
        string queryText,
        FolderSearchMode searchMode)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        ArgumentNullException.ThrowIfNull(queryText);

        if (folderName.Length == 0)
        {
            return [];
        }

        var ranges = searchMode switch
        {
            FolderSearchMode.Exact =>
                FindExactRanges(folderName, queryText),

            FolderSearchMode.Smart =>
                FindSmartRanges(folderName, queryText),

            _ => throw new ArgumentOutOfRangeException(
                nameof(searchMode),
                searchMode,
                "Unsupported folder search mode.")
        };

        return CreateSegments(folderName, ranges);
    }

    private IReadOnlyList<TextRange> FindExactRanges(
        string folderName,
        string queryText)
    {
        var normalizedQuery =
            _textNormalizationService.Normalize(queryText);

        if (normalizedQuery.Length == 0)
        {
            return [];
        }

        var normalizedName =
            CreateNormalizedTextMap(folderName);

        if (normalizedName.Text.Length == 0)
        {
            return [];
        }

        var ranges = new List<TextRange>();
        var searchStart = 0;

        while (searchStart < normalizedName.Text.Length)
        {
            var matchIndex = normalizedName.Text.IndexOf(
                normalizedQuery,
                searchStart,
                StringComparison.Ordinal);

            if (matchIndex < 0)
            {
                break;
            }

            var matchEndIndex =
                matchIndex + normalizedQuery.Length - 1;

            if (matchEndIndex < normalizedName.SourceRanges.Count)
            {
                var sourceStart =
                    normalizedName.SourceRanges[matchIndex].Start;

                var sourceEnd =
                    normalizedName.SourceRanges[matchEndIndex].End;

                ranges.Add(new TextRange(
                    sourceStart,
                    sourceEnd));
            }

            searchStart =
                matchIndex + normalizedQuery.Length;
        }

        return MergeRanges(ranges);
    }

    private IReadOnlyList<TextRange> FindSmartRanges(
        string folderName,
        string queryText)
    {
        var queryTokens =
            _textNormalizationService.Tokenize(queryText);

        if (queryTokens.Count == 0)
        {
            return [];
        }

        var folderTokens =
            ReadTokensWithPositions(folderName);

        var ranges = new List<TextRange>();

        foreach (var folderToken in folderTokens)
        {
            foreach (var queryToken in queryTokens)
            {
                var range = FindSmartTokenRange(
                    folderToken,
                    queryToken);

                if (range is not null)
                {
                    ranges.Add(range.Value);
                }
            }
        }

        return MergeRanges(ranges);
    }

    private TextRange? FindSmartTokenRange(
        PositionedToken folderToken,
        string queryToken)
    {
        if (queryToken.Length < MinimumSmartPrefixLength)
        {
            if (string.Equals(
                    folderToken.NormalizedText,
                    queryToken,
                    StringComparison.Ordinal))
            {
                return new TextRange(
                    folderToken.Start,
                    folderToken.End);
            }

            return null;
        }

        var directMatchIndex =
            folderToken.NormalizedText.IndexOf(
                queryToken,
                StringComparison.Ordinal);

        if (directMatchIndex >= 0)
        {
            return CreateTokenSubrange(
                folderToken,
                directMatchIndex,
                queryToken.Length);
        }

        var queryStem =
            _searchStemService.GetStem(queryToken);

        var folderStem =
            _searchStemService.GetStem(
                folderToken.NormalizedText);

        if (queryStem.Length <
                MinimumSmartPrefixLength ||
            folderStem.Length <
                MinimumSmartPrefixLength)
        {
            return null;
        }

        var stemsAreRelated =
            folderStem.StartsWith(
                queryStem,
                StringComparison.Ordinal) ||
            queryStem.StartsWith(
                folderStem,
                StringComparison.Ordinal);

        if (!stemsAreRelated)
        {
            return null;
        }

        var queryStemIndex =
            folderToken.NormalizedText.IndexOf(
                queryStem,
                StringComparison.Ordinal);

        if (queryStemIndex >= 0)
        {
            return CreateTokenSubrange(
                folderToken,
                queryStemIndex,
                queryStem.Length);
        }

        var folderStemIndex =
            folderToken.NormalizedText.IndexOf(
                folderStem,
                StringComparison.Ordinal);

        if (folderStemIndex >= 0)
        {
            return CreateTokenSubrange(
                folderToken,
                folderStemIndex,
                folderStem.Length);
        }

        return new TextRange(
            folderToken.Start,
            folderToken.End);
    }

    private static TextRange CreateTokenSubrange(
        PositionedToken token,
        int normalizedStart,
        int normalizedLength)
    {
        var start = Math.Min(
            token.Start + normalizedStart,
            token.End);

        var end = Math.Min(
            start + normalizedLength,
            token.End);

        return new TextRange(start, end);
    }

    private IReadOnlyList<PositionedToken>
        ReadTokensWithPositions(string text)
    {
        var result = new List<PositionedToken>();
        var position = 0;

        while (position < text.Length)
        {
            while (position < text.Length &&
                   !char.IsLetterOrDigit(text[position]))
            {
                position++;
            }

            if (position >= text.Length)
            {
                break;
            }

            var start = position;

            while (position < text.Length &&
                   char.IsLetterOrDigit(text[position]))
            {
                position++;
            }

            var tokenText =
                text[start..position];

            var normalizedToken =
                _textNormalizationService.Normalize(tokenText);

            if (normalizedToken.Length > 0)
            {
                result.Add(new PositionedToken(
                    normalizedToken,
                    start,
                    position));
            }
        }

        return result;
    }

    private static NormalizedTextMap
        CreateNormalizedTextMap(string text)
    {
        var normalizedText = new StringBuilder(text.Length);
        var sourceRanges = new List<TextRange>(text.Length);
        var pendingWhitespaceStart = -1;
        var pendingWhitespaceEnd = -1;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (char.IsWhiteSpace(character))
            {
                if (normalizedText.Length > 0)
                {
                    if (pendingWhitespaceStart < 0)
                    {
                        pendingWhitespaceStart = index;
                    }

                    pendingWhitespaceEnd = index + 1;
                }

                continue;
            }

            if (pendingWhitespaceStart >= 0)
            {
                normalizedText.Append(' ');
                sourceRanges.Add(new TextRange(
                    pendingWhitespaceStart,
                    pendingWhitespaceEnd));

                pendingWhitespaceStart = -1;
                pendingWhitespaceEnd = -1;
            }

            var normalizedCharacter =
                char.ToLowerInvariant(character);

            if (normalizedCharacter == 'ё')
            {
                normalizedCharacter = 'е';
            }

            normalizedText.Append(normalizedCharacter);
            sourceRanges.Add(new TextRange(
                index,
                index + 1));
        }

        return new NormalizedTextMap(
            normalizedText.ToString(),
            sourceRanges);
    }

    private static IReadOnlyList<TextRange> MergeRanges(
        IEnumerable<TextRange> ranges)
    {
        var orderedRanges = ranges
            .Where(range => range.End > range.Start)
            .OrderBy(range => range.Start)
            .ThenBy(range => range.End)
            .ToArray();

        if (orderedRanges.Length == 0)
        {
            return [];
        }

        var result = new List<TextRange>();
        var current = orderedRanges[0];

        for (var index = 1;
             index < orderedRanges.Length;
             index++)
        {
            var next = orderedRanges[index];

            if (next.Start <= current.End)
            {
                current = new TextRange(
                    current.Start,
                    Math.Max(current.End, next.End));

                continue;
            }

            result.Add(current);
            current = next;
        }

        result.Add(current);
        return result;
    }

    private static IReadOnlyList<FolderNameTextSegment>
        CreateSegments(
            string text,
            IReadOnlyList<TextRange> highlightedRanges)
    {
        if (highlightedRanges.Count == 0)
        {
            return
            [
                new FolderNameTextSegment(
                    text,
                    IsHighlighted: false)
            ];
        }

        var result =
            new List<FolderNameTextSegment>();

        var position = 0;

        foreach (var range in highlightedRanges)
        {
            if (range.Start > position)
            {
                result.Add(new FolderNameTextSegment(
                    text[position..range.Start],
                    IsHighlighted: false));
            }

            result.Add(new FolderNameTextSegment(
                text[range.Start..range.End],
                IsHighlighted: true));

            position = range.End;
        }

        if (position < text.Length)
        {
            result.Add(new FolderNameTextSegment(
                text[position..],
                IsHighlighted: false));
        }

        return result;
    }

    private readonly record struct TextRange(
        int Start,
        int End);

    private sealed record PositionedToken(
        string NormalizedText,
        int Start,
        int End);

    private sealed record NormalizedTextMap(
        string Text,
        IReadOnlyList<TextRange> SourceRanges);
}
