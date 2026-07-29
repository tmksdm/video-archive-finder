using VideoArchiveFinder.Application.Search;

namespace VideoArchiveFinder.Tests.Search;

public sealed class RussianSearchStemServiceTests
{
    private readonly RussianSearchStemService
        _service = new();

    [Theory]
    [InlineData("почта", "почт")]
    [InlineData("почты", "почт")]
    [InlineData("почтовый", "почт")]
    [InlineData("почтовая", "почт")]
    [InlineData("почтамт", "почт")]
    [InlineData("почтальон", "почт")]
    [InlineData("дорога", "дорог")]
    [InlineData("дороги", "дорог")]
    [InlineData("дорожный", "дорог")]
    [InlineData("придорожный", "дорог")]
    [InlineData("поезд", "поезд")]
    [InlineData("поезда", "поезд")]
    [InlineData("поездной", "поезд")]
    [InlineData("машина", "машин")]
    [InlineData("машины", "машин")]
    [InlineData("машинный", "машин")]
    [InlineData("море", "мор")]
    [InlineData("морской", "мор")]
    [InlineData("VIDEO", "video")]
    public void GetStem_ReturnsExpectedStem(
        string token,
        string expected)
    {
        var result =
            _service.GetStem(token);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetStems_RemovesDuplicatesAndPreservesOrder()
    {
        var result =
            _service.GetStems(
            [
                "Почта",
                "почтовый",
                "дорога",
                "дорожный",
                "VIDEO"
            ]);

        Assert.Equal(
            ["почт", "дорог", "video"],
            result);
    }

    [Fact]
    public void CreateStemText_JoinsUniqueStems()
    {
        var result =
            _service.CreateStemText(
            [
                "машина",
                "машины",
                "морской"
            ]);

        Assert.Equal(
            "машин мор",
            result);
    }
}
