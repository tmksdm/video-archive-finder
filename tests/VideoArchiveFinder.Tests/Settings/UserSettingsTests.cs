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

            GridCardWidth = 275,

            ThemeMode =
                AppThemeMode.Dark,

            WindowLeft = -1200,
            WindowTop = 80,
            WindowWidth = 1440,
            WindowHeight = 900,
            IsWindowMaximized = true,
            SearchResultsPanelFraction = 0.35
        };

        var normalized = settings.Normalize();

        Assert.Equal(
            VideoFilesViewMode.List,
            normalized.VideoFilesViewMode);

        Assert.Equal(
            275,
            normalized.GridCardWidth);

        Assert.Equal(
            AppThemeMode.Dark,
            normalized.ThemeMode);

        Assert.Equal(-1200, normalized.WindowLeft);
        Assert.Equal(80, normalized.WindowTop);
        Assert.Equal(1440, normalized.WindowWidth);
        Assert.Equal(900, normalized.WindowHeight);
        Assert.True(normalized.IsWindowMaximized);
        Assert.Equal(
            0.35,
            normalized.SearchResultsPanelFraction);
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

    [Fact]
    public void Normalize_ReplacesUnknownThemeMode()
    {
        var settings = new UserSettings
        {
            ThemeMode =
                (AppThemeMode)999
        };

        var normalized = settings.Normalize();

        Assert.Equal(
            AppThemeMode.System,
            normalized.ThemeMode);
    }

    [Fact]
    public void Normalize_ReplacesInvalidWindowBounds()
    {
        var settings = new UserSettings
        {
            WindowLeft = double.NaN,
            WindowTop = double.PositiveInfinity,
            WindowWidth = 100,
            WindowHeight = double.NaN,
            SearchResultsPanelFraction = 2
        };

        var normalized = settings.Normalize();

        Assert.Null(normalized.WindowLeft);
        Assert.Null(normalized.WindowTop);
        Assert.Equal(
            UserSettings.MinimumWindowWidth,
            normalized.WindowWidth);
        Assert.Equal(
            UserSettings.DefaultWindowHeight,
            normalized.WindowHeight);
        Assert.Equal(
            UserSettings.MaximumSearchResultsPanelFraction,
            normalized.SearchResultsPanelFraction);
    }
}
