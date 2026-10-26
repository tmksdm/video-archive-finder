namespace VideoArchiveFinder.Application.Search;

/// <summary>
/// Индекс изменился между порциями поиска. Политика CHANGE-STOP требует
/// прекратить подгрузку, сохранить уже показанные результаты и начать новый
/// поиск, чтобы получить актуальную выдачу.
/// </summary>
public sealed class FolderSearchIndexChangedException : Exception
{
    public FolderSearchIndexChangedException(Guid sessionId)
        : base(
            "Индекс изменился после начала поиска. " +
            "Уже найденные результаты сохранены; выполните поиск заново.")
    {
        SessionId = sessionId;
    }

    /// <summary>Сессия, для которой обнаружено изменение индекса.</summary>
    public Guid SessionId { get; }
}
