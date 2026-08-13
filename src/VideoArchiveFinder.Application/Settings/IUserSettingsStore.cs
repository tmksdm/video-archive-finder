namespace VideoArchiveFinder.Application.Settings;

public interface IUserSettingsStore
{
    Task<UserSettings> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default);
}
