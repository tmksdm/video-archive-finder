using VideoArchiveFinder.Domain.ArchiveSources;

namespace VideoArchiveFinder.Desktop.ViewModels;

public sealed class ArchiveSourceItemViewModel
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

    public string SourceTypeText => SourceType switch
    {
        ArchiveSourceType.LocalFolder => "Локальная папка",
        ArchiveSourceType.UncPath => "Сетевой UNC-путь",
        _ => "Неизвестный источник"
    };
}
