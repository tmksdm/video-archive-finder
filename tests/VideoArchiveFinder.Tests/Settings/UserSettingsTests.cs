using VideoArchiveFinder.Application.Settings;

namespace VideoArchiveFinder.Tests.Settings;

public sealed class UserSettingsTests
{
    [Fact]
    public void Normalize_PreservesValidValues()
    {
        var settings = new UserSettings
        {
            VideoFilesViewMode =
                VideoFilesViewMode.List,

            GridCardWidth = 275
        };

        var normalized = settings.Normalize();

        Assert.Equal(
            VideoFilesViewMode.List,
            normalized.VideoFilesViewMode);

        Assert.Equal(
            275,
            normalized.GridCardWidth);
    }

    [Theory]
    [InlineData(100, UserSettings.MinimumGridCardWidth)]
    [InlineData(500, UserSettings.MaximumGridCardWidth)]
    public void Normalize_ClampsGridCardWidth(
        double value,
        double expected)
    {
        var settings = new UserSettings
        {
            GridCardWidth = value
        };

        var normalized = settings.Normalize();

        Assert.Equal(
            expected,
            normalized.GridCardWidth);
    }

    [Fact]
    public void Normalize_ReplacesNonFiniteGridCardWidth()
    {
        var settings = new UserSettings
        {
            GridCardWidth = double.NaN
        };

        var normalized = settings.Normalize();

        Assert.Equal(
            UserSettings.DefaultGridCardWidth,
            normalized.GridCardWidth);
    }

    [Fact]
    public void Normalize_ReplacesUnknownViewMode()
    {
        var settings = new UserSettings
        {
            VideoFilesViewMode =
                (VideoFilesViewMode)999
        };

        var normalized = settings.Normalize();

        Assert.Equal(
            VideoFilesViewMode.Grid,
            normalized.VideoFilesViewMode);
    }
}
