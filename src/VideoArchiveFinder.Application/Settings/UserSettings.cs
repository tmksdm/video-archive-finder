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

        return this with
        {
            VideoFilesViewMode = normalizedViewMode,
            GridCardWidth = normalizedWidth
        };
    }
}

public enum VideoFilesViewMode
{
    Grid,
    List
}
