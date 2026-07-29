using VideoArchiveFinder.Application.Search;

namespace VideoArchiveFinder.Tests.Search;

public sealed class TextNormalizationServiceTests
{
    private readonly TextNormalizationService
        _service = new();

    [Theory]
    [InlineData(
        "  ЁЖ_Дорога  ",
        "еж_дорога")]
    [InlineData(
        "Почта",
        "почта")]
    [InlineData(
        "  Первая    Вторая  ",
        "первая вторая")]
    [InlineData(
        "VIDEO 2026",
        "video 2026")]
    [InlineData(
        "   ",
        "")]
    public void Normalize_ReturnsExpectedText(
        string source,
        string expected)
    {
        var result =
            _service.Normalize(source);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Tokenize_SplitsSupportedSeparators()
    {
        var result =
            _service.Tokenize(
                "  ЁЖ_Дорога-test--2026  ");

        Assert.Equal(
            ["еж", "дорога", "test", "2026"],
            result);
    }

    [Fact]
    public void CreateTokenText_JoinsTokensWithSpaces()
    {
        var result =
            _service.CreateTokenText(
                "ЖДвокзал_Железная-дорога");

        Assert.Equal(
            "ждвокзал железная дорога",
            result);
    }

    [Fact]
    public void Tokenize_EmptyText_ReturnsEmptyCollection()
    {
        var result =
            _service.Tokenize("   ");

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("ёлка", "елка")]
    [InlineData("берёза", "береза")]
    [InlineData("трёхэтажный", "трехэтажный")]
    public void Normalize_YoAndYeVariantsAreEquivalent(
        string withYo,
        string withYe)
    {
        var normalizedWithYo =
            _service.Normalize(withYo);

        var normalizedWithYe =
            _service.Normalize(withYe);

        Assert.Equal(
            normalizedWithYe,
            normalizedWithYo);
    }

}
