using VideoArchiveFinder.Application.Settings;

namespace VideoArchiveFinder.Desktop.Services;

public interface IAppThemeService
{
    AppThemeMode SelectedMode
    {
        get;
    }

    AppThemeMode EffectiveMode
    {
        get;
    }

    event EventHandler? ThemeChanged;

    Task InitializeAsync(
        CancellationToken cancellationToken = default);

    Task SetThemeAsync(
        AppThemeMode mode,
        CancellationToken cancellationToken = default);
}
