using CommunityToolkit.Mvvm.ComponentModel;
using VideoArchiveFinder.Application.ArchiveSources;
using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Desktop.ViewModels;

public sealed partial class ArchiveSourceItemViewModel :
    ObservableObject
{
    public ArchiveSourceItemViewModel(ArchiveSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Id = source.Id;
        DisplayName = source.DisplayName;
        FullPath = source.FullPath;
        SourceType = source.SourceType;
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public string FullPath { get; }

    public ArchiveSourceType SourceType { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailabilityText))]
    [NotifyPropertyChangedFor(nameof(AvailabilityColor))]
    [NotifyPropertyChangedFor(nameof(AvailabilityToolTip))]
    private ArchiveSourceAvailability _availability =
        ArchiveSourceAvailability.Unknown;

    public string SourceTypeText => SourceType switch
    {
        ArchiveSourceType.LocalFolder => "Локальная папка",
        ArchiveSourceType.UncPath => "Сетевой UNC-путь",
        _ => "Неизвестный источник"
    };
    public string SourceTypeShortText => SourceType switch
    {
        ArchiveSourceType.LocalFolder => "Локальный",
        ArchiveSourceType.UncPath => "UNC",
        _ => "Неизвестный"
    };

    public string AvailabilityText => Availability switch
    {
        ArchiveSourceAvailability.Checking => "Проверка...",
        ArchiveSourceAvailability.Available => "Доступен",
        ArchiveSourceAvailability.Unavailable => "Недоступен",
        ArchiveSourceAvailability.TimedOut => "Не отвечает",
        _ => "Не проверено"
    };

    public string AvailabilityColor => Availability switch
    {
        ArchiveSourceAvailability.Checking => "#2563EB",
        ArchiveSourceAvailability.Available => "#15803D",
        ArchiveSourceAvailability.Unavailable => "#B91C1C",
        ArchiveSourceAvailability.TimedOut => "#B45309",
        _ => "#6B7280"
    };

    public string AvailabilityToolTip => Availability switch
    {
        ArchiveSourceAvailability.Checking =>
            "Выполняется проверка доступности источника",
        ArchiveSourceAvailability.Available =>
            "Путь доступен",
        ArchiveSourceAvailability.Unavailable =>
            "Папка не найдена или доступ к ней невозможен",
        ArchiveSourceAvailability.TimedOut =>
            "Источник не ответил за отведённое время",
        _ =>
            "Доступность источника ещё не проверялась"
    };
}
