namespace VideoArchiveFinder.Application.Settings;

public sealed record UserSettings
{
    public const double DefaultGridCardWidth = 240;
    public const double MinimumGridCardWidth = 160;
    public const double MaximumGridCardWidth = 360;
    public const double DefaultWindowWidth = 1280;
    public const double DefaultWindowHeight = 800;
    public const double MinimumWindowWidth = 900;
    public const double MinimumWindowHeight = 600;
    public const double DefaultSearchResultsPanelFraction = 0.28;
    public const double MinimumSearchResultsPanelFraction = 0.1;
    public const double MaximumSearchResultsPanelFraction = 0.9;

    public VideoFilesViewMode VideoFilesViewMode
    {
        get;
        init;
    } = VideoFilesViewMode.Grid;

    public double GridCardWidth
    {
        get;
        init;
    } = DefaultGridCardWidth;

    public AppThemeMode ThemeMode
    {
        get;
        init;
    } = AppThemeMode.System;

    public double? WindowLeft
    {
        get;
        init;
    }

    public double? WindowTop
    {
        get;
        init;
    }

    public double WindowWidth
    {
        get;
        init;
    } = DefaultWindowWidth;

    public double WindowHeight
    {
        get;
        init;
    } = DefaultWindowHeight;

    public bool IsWindowMaximized
    {
        get;
        init;
    }

    public double SearchResultsPanelFraction
    {
        get;
        init;
    } = DefaultSearchResultsPanelFraction;

    public UserSettings Normalize()
    {
        var normalizedWidth =
            double.IsFinite(GridCardWidth)
                ? Math.Clamp(
                    GridCardWidth,
                    MinimumGridCardWidth,
                    MaximumGridCardWidth)
                : DefaultGridCardWidth;

        var normalizedViewMode =
            Enum.IsDefined(VideoFilesViewMode)
                ? VideoFilesViewMode
                : VideoFilesViewMode.Grid;

        var normalizedThemeMode =
            Enum.IsDefined(ThemeMode)
                ? ThemeMode
                : AppThemeMode.System;

        var normalizedWindowWidth =
            double.IsFinite(WindowWidth)
                ? Math.Max(WindowWidth, MinimumWindowWidth)
                : DefaultWindowWidth;

        var normalizedWindowHeight =
            double.IsFinite(WindowHeight)
                ? Math.Max(WindowHeight, MinimumWindowHeight)
                : DefaultWindowHeight;

        double? normalizedWindowLeft =
            WindowLeft is { } windowLeft &&
            double.IsFinite(windowLeft)
                ? windowLeft
                : null;

        double? normalizedWindowTop =
            WindowTop is { } windowTop &&
            double.IsFinite(windowTop)
                ? windowTop
                : null;

        var normalizedSearchResultsPanelFraction =
            double.IsFinite(SearchResultsPanelFraction)
                ? Math.Clamp(
                    SearchResultsPanelFraction,
                    MinimumSearchResultsPanelFraction,
                    MaximumSearchResultsPanelFraction)
                : DefaultSearchResultsPanelFraction;

        return this with
        {
            VideoFilesViewMode = normalizedViewMode,
            GridCardWidth = normalizedWidth,
            ThemeMode = normalizedThemeMode,
            WindowLeft = normalizedWindowLeft,
            WindowTop = normalizedWindowTop,
            WindowWidth = normalizedWindowWidth,
            WindowHeight = normalizedWindowHeight,
            SearchResultsPanelFraction =
                normalizedSearchResultsPanelFraction
        };
    }
}

public enum VideoFilesViewMode
{
    Grid,
    List
}

public enum AppThemeMode
{
    System,
    Light,
    Dark
}
