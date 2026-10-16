namespace VideoArchiveFinder.Application.Search;

/// <summary>
/// Позиция в эталонном порядке выдачи одного потока (папки или видеофайлы).
///
/// Порядок задаётся ключом
/// <c>Priority, length(Name), Name COLLATE NOCASE, Id</c>; <see cref="Id"/> —
/// идентификатор строки в своей таблице (<c>Folders.Id</c> для папок,
/// <c>VideoFiles.Id</c> для файлов), поэтому ключ является тотальным порядком и
/// однозначно определяет, с какого места продолжать выборку.
/// </summary>
public sealed record FolderSearchCursor(
    int Priority,
    int Length,
    string Name,
    long Id);
