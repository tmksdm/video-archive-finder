namespace VideoArchiveFinder.Application.Settings;

public sealed record UserSettings
{
    public const double DefaultGridCardWidth = 240;
    public const double MinimumGridCardWidth = 160;
    public const double MaximumGridCardWidth = 360;

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

        return this with
        {
            VideoFilesViewMode = normalizedViewMode,
            GridCardWidth = normalizedWidth,
            ThemeMode = normalizedThemeMode
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
