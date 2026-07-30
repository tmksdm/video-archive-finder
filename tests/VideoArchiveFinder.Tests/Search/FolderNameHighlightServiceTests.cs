using VideoArchiveFinder.Application.Search;

namespace VideoArchiveFinder.Tests.Search;

public sealed class FolderNameHighlightServiceTests
{
    private readonly FolderNameHighlightService _service =
        new(
            new TextNormalizationService(),
            new RussianSearchStemService());

    [Fact]
    public void CreateSegments_Exact_HighlightsAllOccurrences()
    {
        var segments = _service.CreateSegments(
            "Почта и ПОЧТА",
            "почта",
            FolderSearchMode.Exact);

        Assert.Equal(
            "ПочтаПОЧТА",
            GetHighlightedText(segments));

        Assert.Equal(
            "Почта и ПОЧТА",
            GetFullText(segments));
    }

    [Fact]
    public void CreateSegments_Exact_TreatsYoAsYe()
    {
        var segments = _service.CreateSegments(
            "Ёлка",
            "елка",
            FolderSearchMode.Exact);

        var segment = Assert.Single(segments);

        Assert.Equal("Ёлка", segment.Text);
        Assert.True(segment.IsHighlighted);
    }

    [Fact]
    public void CreateSegments_Exact_MapsCollapsedWhitespace()
    {
        var segments = _service.CreateSegments(
            "Железная   дорога",
            "железная дорога",
            FolderSearchMode.Exact);

        var segment = Assert.Single(segments);

        Assert.Equal(
            "Железная   дорога",
            segment.Text);

        Assert.True(segment.IsHighlighted);
    }

    [Fact]
    public void CreateSegments_Smart_HighlightsPartialWord()
    {
        var segments = _service.CreateSegments(
            "Железная_дорога",
            "дорог",
            FolderSearchMode.Smart);

        Assert.Equal(
            "дорог",
            GetHighlightedText(segments));

        Assert.Equal(
            "Железная_дорога",
            GetFullText(segments));
    }

    [Fact]
    public void CreateSegments_Smart_HighlightsRelatedStemWord()
    {
        var segments = _service.CreateSegments(
            "Дорожные работы",
            "дорога",
            FolderSearchMode.Smart);

        Assert.Equal(
            "Дорожные",
            GetHighlightedText(segments));
    }

    [Fact]
    public void CreateSegments_Smart_HighlightsSeveralMatches()
    {
        var segments = _service.CreateSegments(
            "Почтовая дорога",
            "почта дорога",
            FolderSearchMode.Smart);

        Assert.Equal(
            "Почтдорога",
            GetHighlightedText(segments));

        Assert.Equal(
            "Почтовая дорога",
            GetFullText(segments));
    }

    [Fact]
    public void CreateSegments_Smart_DoesNotExpandShortQuery()
    {
        var segments = _service.CreateSegments(
            "Поезд",
            "по",
            FolderSearchMode.Smart);

        Assert.DoesNotContain(
            segments,
            segment => segment.IsHighlighted);
    }

    [Fact]
    public void CreateSegments_ReturnsPlainSegmentWhenNoMatch()
    {
        var segments = _service.CreateSegments(
            "Морской вокзал",
            "почта",
            FolderSearchMode.Smart);

        var segment = Assert.Single(segments);

        Assert.Equal(
            "Морской вокзал",
            segment.Text);

        Assert.False(segment.IsHighlighted);
    }

    private static string GetHighlightedText(
        IEnumerable<FolderNameTextSegment> segments)
    {
        return string.Concat(
            segments
                .Where(segment =>
                    segment.IsHighlighted)
                .Select(segment =>
                    segment.Text));
    }

    private static string GetFullText(
        IEnumerable<FolderNameTextSegment> segments)
    {
        return string.Concat(
            segments.Select(segment =>
                segment.Text));
    }
}
