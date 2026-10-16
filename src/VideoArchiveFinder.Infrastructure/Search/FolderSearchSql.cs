using System.Text;
using Microsoft.Data.Sqlite;
using VideoArchiveFinder.Application.Search;

namespace VideoArchiveFinder.Infrastructure.Search;

/// <summary>
/// Единый источник SQL для поиска по папкам и именам видеофайлов.
///
/// Условия поиска, выражения приоритета и порядок сортировки строятся только
/// здесь, чтобы точечный поиск, постраничный поиск, счётчики и эталонный
/// запрос сравнения не могли разойтись. Расхождение выражений сортировки
/// приводит к молчаливой потере совпадений, поэтому дублирование этих
/// фрагментов недопустимо.
/// </summary>
public static class FolderSearchSql
{
    /// <summary>
    /// Токены короче этой длины ищутся как точное слово, а не по основе.
    /// </summary>
    public const int MinimumSmartPrefixLength = 3;

    /// <summary>
    /// Верхняя граница количества результатов, которую принимает поиск.
    /// </summary>
    public const int MaximumAllowedResults = 1000;

    /// <summary>
    /// Курсор первой порции: ключ меньше любого реального ключа (приоритет не
    /// бывает отрицательным), поэтому условие «строго после курсора» истинно
    /// для всех строк и первой порции не нужен отдельный запрос.
    /// </summary>
    public static readonly FolderSearchCursor InitialCursor =
        new(
            Priority: -1,
            Length: -1,
            Name: string.Empty,
            Id: -1);

    /// <summary>
    /// Колонки выдачи по папкам. Порядок обязан совпадать с
    /// <see cref="ReadResult"/>.
    /// </summary>
    public const string FolderSelectColumns = """
        Id,
        FullPath,
        Name,
        NormalizedName,
        ParentFolderId,
        RootSourceId,
        IsAvailable,
        DirectSubfolderCount,
        DirectVideoFileCount
        """;

    /// <summary>
    /// Колонки выдачи по видеофайлам. Порядок обязан совпадать с
    /// <see cref="ReadResult"/>: четвёртая колонка — папка файла (она же
    /// родительская папка в результате), десятая — путь этой папки.
    /// </summary>
    public const string VideoSelectColumns = """
        -v.Id,
        v.FullPath,
        v.Name,
        v.NormalizedName,
        f.Id,
        f.RootSourceId,
        v.IsAvailable,
        0,
        0,
        f.FullPath
        """;

    /// <summary>
    /// Те же колонки видеофайлов с псевдонимами — их требует обращение к
    /// колонкам CTE из внешнего запроса. Обязаны соответствовать
    /// <see cref="VideoSelectColumns"/> по составу и порядку.
    /// </summary>
    private const string VideoCandidateColumns = """
        -v.Id AS Id,
        v.FullPath AS FullPath,
        v.Name AS Name,
        v.NormalizedName AS NormalizedName,
        f.Id AS NavigationFolderId,
        f.RootSourceId AS RootSourceId,
        v.IsAvailable AS IsAvailable,
        f.FullPath AS NavigationFolderFullPath
        """;

    private const string VideoFromClause = """
        FROM VideoFiles v
        INNER JOIN Folders f
            ON f.RootSourceId = v.RootSourceId
           AND f.FullPath = v.FolderFullPath
        """;

    /// <summary>
    /// Порядок сортировки папок. Совпадает с порядком прежнего поиска.
    /// </summary>
    public static string BuildFolderOrderBy(
        string normalizedNameColumn,
        string nameColumn,
        string rowKeyColumn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedNameColumn);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameColumn);
        ArgumentException.ThrowIfNullOrWhiteSpace(rowKeyColumn);

        return $"""
            CASE
                WHEN {normalizedNameColumn} = $normalizedQuery
                THEN 0

                WHEN instr(
                    {normalizedNameColumn},
                    $normalizedQuery) = 1
                THEN 1

                WHEN instr(
                    {normalizedNameColumn},
                    $normalizedQuery) > 0
                THEN 2

                ELSE 3
            END,
            length({nameColumn}),
            {nameColumn} COLLATE NOCASE,
            {rowKeyColumn}
            """;
    }

    /// <summary>
    /// Порядок сортировки видеофайлов. Совпадает с порядком прежнего поиска.
    /// </summary>
    public static string BuildVideoOrderBy(
        string normalizedNameColumn,
        string nameColumn,
        string rowKeyColumn)
    {
        return BuildFolderOrderBy(
            normalizedNameColumn,
            nameColumn,
            rowKeyColumn);
    }

    /// <summary>
    /// Выражение приоритета совпадения. Для папок и для видеофайлов отличается
    /// только именем колонки с нормализованным именем.
    /// </summary>
    public static string BuildPriorityExpression(
        string normalizedNameColumn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            normalizedNameColumn);

        return $"""
            CASE
                WHEN {normalizedNameColumn} = $normalizedQuery
                THEN 0

                WHEN instr(
                    {normalizedNameColumn},
                    $normalizedQuery) = 1
                THEN 1

                WHEN instr(
                    {normalizedNameColumn},
                    $normalizedQuery) > 0
                THEN 2

                ELSE 3
            END
            """;
    }

    public static void ValidateQuery(FolderSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Text is null)
        {
            throw new ArgumentException(
                "Search text cannot be null.",
                nameof(query));
        }

        if (!Enum.IsDefined(
                typeof(FolderSearchMode),
                query.Mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                query.Mode,
                "Unsupported folder search mode.");
        }

        if (query.MaxResults < 1 ||
            query.MaxResults > MaximumAllowedResults)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                query.MaxResults,
                $"Maximum result count must be between 1 " +
                $"and {MaximumAllowedResults}.");
        }
    }

    /// <summary>
    /// Условие поиска по папкам: точное вхождение либо «умный» поиск по токенам
    /// и основам слов.
    /// </summary>
    public static string BuildFolderSearchCondition(
        SqliteCommand command,
        ITextNormalizationService textNormalizationService,
        ISearchStemService searchStemService,
        FolderSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(textNormalizationService);
        ArgumentNullException.ThrowIfNull(searchStemService);
        ArgumentNullException.ThrowIfNull(query);

        return query.Mode switch
        {
            FolderSearchMode.Exact =>
                "instr(NormalizedName, $normalizedQuery) > 0",

            FolderSearchMode.Smart =>
                BuildFolderSmartSearchCondition(
                    command,
                    textNormalizationService,
                    searchStemService,
                    query.Text),

            _ => throw new ArgumentOutOfRangeException(
                nameof(query),
                query.Mode,
                "Unsupported folder search mode.")
        };
    }

    private static string BuildFolderSmartSearchCondition(
        SqliteCommand command,
        ITextNormalizationService textNormalizationService,
        ISearchStemService searchStemService,
        string queryText)
    {
        var tokens = textNormalizationService
            .Tokenize(queryText)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (tokens.Length == 0)
        {
            return "instr(NormalizedName, $normalizedQuery) > 0";
        }

        var conditions = new List<string>();

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            var tokenParameterName = $"$token{index}";

            command.Parameters.AddWithValue(
                tokenParameterName,
                token);

            if (token.Length < MinimumSmartPrefixLength)
            {
                conditions.Add(
                    $"""
                    (
                        NormalizedName = {tokenParameterName}
                        OR instr(
                            ' ' || SearchTokens || ' ',
                            ' ' || {tokenParameterName} || ' ') > 0
                    )
                    """);

                continue;
            }

            var stem = searchStemService.GetStem(token);
            var stemParameterName = $"$stem{index}";

            command.Parameters.AddWithValue(
                stemParameterName,
                stem);

            conditions.Add(
                $"""
                (
                    instr(
                        NormalizedName,
                        {tokenParameterName}) > 0

                    OR instr(
                        ' ' || SearchTokens,
                        ' ' || {tokenParameterName}) > 0

                    OR instr(
                        ' ' || SearchStems,
                        ' ' || {stemParameterName}) > 0
                )
                """);
        }

        return string.Join(
            Environment.NewLine + " AND ",
            conditions);
    }

    /// <summary>
    /// Условие поиска по именам видеофайлов. У видеофайлов нет предвычисленных
    /// токенов и основ, поэтому «умный» режим сравнивает подстроку имени и её
    /// основу.
    /// </summary>
    public static string BuildVideoSearchCondition(
        SqliteCommand command,
        ITextNormalizationService textNormalizationService,
        ISearchStemService searchStemService,
        FolderSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(textNormalizationService);
        ArgumentNullException.ThrowIfNull(searchStemService);
        ArgumentNullException.ThrowIfNull(query);

        return query.Mode switch
        {
            FolderSearchMode.Exact =>
                "instr(v.NormalizedName, $normalizedQuery) > 0",

            FolderSearchMode.Smart =>
                BuildVideoSmartSearchCondition(
                    command,
                    textNormalizationService,
                    searchStemService,
                    query.Text),

            _ => throw new ArgumentOutOfRangeException(
                nameof(query),
                query.Mode,
                "Unsupported folder search mode.")
        };
    }

    private static string BuildVideoSmartSearchCondition(
        SqliteCommand command,
        ITextNormalizationService textNormalizationService,
        ISearchStemService searchStemService,
        string queryText)
    {
        var tokens = textNormalizationService
            .Tokenize(queryText)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (tokens.Length == 0)
        {
            return "instr(v.NormalizedName, $normalizedQuery) > 0";
        }

        var conditions = new List<string>();

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            var parameterName = $"$videoToken{index}";

            command.Parameters.AddWithValue(parameterName, token);

            if (token.Length < MinimumSmartPrefixLength)
            {
                conditions.Add(
                    $"instr(v.NormalizedName, {parameterName}) > 0");

                continue;
            }

            var stem = searchStemService.GetStem(token);
            var stemParameterName = $"$videoStem{index}";

            command.Parameters.AddWithValue(
                stemParameterName,
                stem);

            conditions.Add(
                $"(instr(v.NormalizedName, {parameterName}) > 0 " +
                $"OR instr(v.NormalizedName, {stemParameterName}) > 0)");
        }

        return string.Join(
            Environment.NewLine + " AND ",
            conditions);
    }

    public static string BuildRootSourceIdFilter(
        string columnName,
        IReadOnlyCollection<Guid> rootSourceIds,
        string parameterPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        ArgumentNullException.ThrowIfNull(rootSourceIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterPrefix);

        return $"{columnName} IN ({string.Join(", ",
            Enumerable.Range(0, rootSourceIds.Count)
                .Select(index => $"${parameterPrefix}{index}"))})";
    }

    public static void AddRootSourceIdParameters(
        SqliteCommand command,
        IReadOnlyList<Guid>? rootSourceIds,
        string parameterPrefix = "rootSourceId")
    {
        ArgumentNullException.ThrowIfNull(command);

        if (rootSourceIds is null)
        {
            return;
        }

        for (var index = 0; index < rootSourceIds.Count; index++)
        {
            command.Parameters.AddWithValue(
                $"${parameterPrefix}{index}",
                rootSourceIds[index].ToString("D"));
        }
    }

    /// <summary>
    /// Прежний запрос по папкам: без постраничной выборки, с ограничением
    /// количества результатов.
    /// </summary>
    public static string BuildFolderSelectCommand(
        string searchCondition,
        IReadOnlyCollection<Guid>? rootSourceIds)
    {
        return BuildFolderSelectCommandCore(
            searchCondition,
            rootSourceIds,
            "LIMIT $maxResults;");
    }

    /// <summary>
    /// Эталонный запрос по папкам: тот же фильтр и тот же порядок, но выборка
    /// через <c>OFFSET</c>. Используется тестами как независимый эталон.
    /// </summary>
    public static string BuildFolderOffsetCommand(
        string searchCondition,
        IReadOnlyCollection<Guid>? rootSourceIds)
    {
        return BuildFolderSelectCommandCore(
            searchCondition,
            rootSourceIds,
            "LIMIT $pageSize OFFSET $offset;");
    }

    /// <summary>
    /// Прежний запрос по видеофайлам: без постраничной выборки, с ограничением
    /// количества результатов.
    /// </summary>
    public static string BuildVideoSelectCommand(
        string searchCondition,
        IReadOnlyCollection<Guid> rootSourceIds)
    {
        return BuildVideoSelectCommandCore(
            searchCondition,
            rootSourceIds,
            "LIMIT $maxResults;");
    }

    /// <summary>
    /// Эталонный запрос по видеофайлам: тот же фильтр и тот же порядок, но
    /// выборка через <c>OFFSET</c>.
    /// </summary>
    public static string BuildVideoOffsetCommand(
        string searchCondition,
        IReadOnlyCollection<Guid> rootSourceIds)
    {
        return BuildVideoSelectCommandCore(
            searchCondition,
            rootSourceIds,
            "LIMIT $pageSize OFFSET $offset;");
    }

    /// <summary>
    /// Постраничная выборка по папкам: продолжение строго после курсора.
    /// </summary>
    public static string BuildFolderPageCommand(
        string searchCondition,
        IReadOnlyCollection<Guid>? rootSourceIds)
    {
        var commandText = new StringBuilder();

        commandText.AppendLine("WITH Candidates AS");
        commandText.AppendLine("(");
        commandText.AppendLine("    SELECT");
        commandText.AppendLine(Indent(FolderSelectColumns) + ",");
        commandText.AppendLine(
            Indent(BuildPriorityExpression("NormalizedName")) +
            " AS Priority");
        commandText.AppendLine("    FROM Folders");
        commandText.AppendLine("    WHERE");
        commandText.AppendLine(Indent(searchCondition));

        if (rootSourceIds is not null)
        {
            commandText.AppendLine(
                "      AND " + BuildRootSourceIdFilter(
                    "RootSourceId",
                    rootSourceIds,
                    "rootSourceId"));
        }

        commandText.AppendLine(")");
        commandText.AppendLine("SELECT");
        commandText.AppendLine(FolderSelectColumns + ",");
        commandText.AppendLine("    Priority,");
        commandText.AppendLine("    length(Name) AS NameLength,");
        commandText.AppendLine("    Id AS RowKey");
        commandText.AppendLine("FROM Candidates");
        commandText.AppendLine("WHERE");
        commandText.AppendLine(FolderCursorFilter);
        commandText.AppendLine("ORDER BY");
        commandText.AppendLine(
            BuildFolderOrderBy("NormalizedName", "Name", "Id"));
        commandText.AppendLine("LIMIT $pageSize;");

        return commandText.ToString();
    }

    /// <summary>
    /// Постраничная выборка по видеофайлам: продолжение строго после курсора.
    /// </summary>
    public static string BuildVideoPageCommand(
        string searchCondition,
        IReadOnlyCollection<Guid> rootSourceIds)
    {
        var commandText = new StringBuilder();

        commandText.AppendLine("WITH Candidates AS");
        commandText.AppendLine("(");
        commandText.AppendLine("    SELECT");
        commandText.AppendLine("        v.Id AS RowId,");
        commandText.AppendLine(Indent(VideoCandidateColumns) + ",");
        commandText.AppendLine(
            Indent(BuildPriorityExpression("v.NormalizedName")) +
            " AS Priority");
        commandText.AppendLine(Indent(VideoFromClause));
        commandText.AppendLine("    WHERE " + searchCondition);
        commandText.AppendLine("      AND v.IsAvailable = 1");
        commandText.AppendLine(
            "      AND " + BuildRootSourceIdFilter(
                "v.RootSourceId",
                rootSourceIds,
                "videoRootSourceId"));
        commandText.AppendLine(")");
        commandText.AppendLine("SELECT");
        commandText.AppendLine("    Id,");
        commandText.AppendLine("    FullPath,");
        commandText.AppendLine("    Name,");
        commandText.AppendLine("    NormalizedName,");
        commandText.AppendLine("    NavigationFolderId,");
        commandText.AppendLine("    RootSourceId,");
        commandText.AppendLine("    IsAvailable,");
        commandText.AppendLine("    0,");
        commandText.AppendLine("    0,");
        commandText.AppendLine("    NavigationFolderFullPath,");
        commandText.AppendLine("    Priority,");
        commandText.AppendLine("    length(Name) AS NameLength,");
        commandText.AppendLine("    RowId AS RowKey");
        commandText.AppendLine("FROM Candidates");
        commandText.AppendLine("WHERE");
        commandText.AppendLine(VideoCursorFilter);
        commandText.AppendLine("ORDER BY");
        commandText.AppendLine(
            BuildVideoOrderBy("NormalizedName", "Name", "RowId"));
        commandText.AppendLine("LIMIT $pageSize;");

        return commandText.ToString();
    }
    /// <summary>
    /// Позиции колонок ключа сортировки в постраничной выдаче. Дописаны в конец
    /// списка колонок, чтобы <see cref="ReadResult"/> и
    /// <see cref="ReadVideoResult"/> читали начало строки без изменений.
    /// Курсор берётся именно отсюда, а не пересчитывается в C#: <c>length()</c>
    /// в SQLite считает символы, а <c>string.Length</c> — единицы UTF-16, и для
    /// имён с символами вне BMP они разошлись бы.
    /// </summary>
    public const int FolderPagePriorityColumn = 9;

    public const int FolderPageNameLengthColumn = 10;

    public const int FolderPageRowKeyColumn = 11;

    public const int VideoPagePriorityColumn = 10;

    public const int VideoPageNameLengthColumn = 11;

    public const int VideoPageRowKeyColumn = 12;

    /// <summary>
    /// Имя совпадения находится во второй колонке обеих постраничных выдач.
    /// </summary>
    private const int PageNameColumn = 2;

    public static FolderSearchCursor ReadPageCursor(
        SqliteDataReader reader,
        int priorityColumn,
        int nameLengthColumn,
        int rowKeyColumn)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return new FolderSearchCursor(
            Priority: reader.GetInt32(priorityColumn),
            Length: reader.GetInt32(nameLengthColumn),
            Name: reader.GetString(PageNameColumn),
            Id: reader.GetInt64(rowKeyColumn));
    }

    public static FolderSearchResult ReadResult(SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var rootSourceIdText = reader.GetString(5);

        if (!Guid.TryParse(rootSourceIdText, out var rootSourceId))
        {
            throw new InvalidDataException(
                "Invalid root source identifier: " +
                rootSourceIdText + ".");
        }

        return new FolderSearchResult(
            Id: reader.GetInt64(0),
            FullPath: reader.GetString(1),
            Name: reader.GetString(2),
            NormalizedName: reader.GetString(3),
            ParentFolderId: reader.IsDBNull(4)
                ? null
                : reader.GetInt64(4),
            RootSourceId: rootSourceId,
            IsAvailable: reader.GetInt64(6) != 0,
            DirectSubfolderCount: reader.GetInt32(7),
            DirectVideoFileCount: reader.GetInt32(8));
    }

    public static FolderSearchResult ReadVideoResult(
        SqliteDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return ReadResult(reader) with
        {
            IsVideoFile = true,
            NavigationFolderId = reader.GetInt64(4),
            NavigationFolderFullPath = reader.GetString(9)
        };
    }

    private const string FolderCursorFilter = """
        (Priority, length(Name), Name COLLATE NOCASE, Id)
            > ($cursorPriority,
               $cursorLength,
               $cursorName COLLATE NOCASE,
               $cursorId)
        """;

    private const string VideoCursorFilter = """
        (Priority, length(Name), Name COLLATE NOCASE, RowId)
            > ($cursorPriority,
               $cursorLength,
               $cursorName COLLATE NOCASE,
               $cursorId)
        """;

    private static string BuildFolderSelectCommandCore(
        string searchCondition,
        IReadOnlyCollection<Guid>? rootSourceIds,
        string limitClause)
    {
        var commandText = new StringBuilder();

        commandText.AppendLine("SELECT");
        commandText.AppendLine(FolderSelectColumns);
        commandText.AppendLine("FROM Folders");
        commandText.AppendLine("WHERE");
        commandText.AppendLine(searchCondition);

        if (rootSourceIds is not null)
        {
            commandText.AppendLine(
                "AND " + BuildRootSourceIdFilter(
                    "RootSourceId",
                    rootSourceIds,
                    "rootSourceId"));
        }

        commandText.AppendLine("ORDER BY");
        commandText.AppendLine(
            BuildFolderOrderBy(
                "NormalizedName",
                "Name",
                "Id"));
        commandText.AppendLine(limitClause);

        return commandText.ToString();
    }

    private static string BuildVideoSelectCommandCore(
        string searchCondition,
        IReadOnlyCollection<Guid> rootSourceIds,
        string limitClause)
    {
        var commandText = new StringBuilder();

        commandText.AppendLine("SELECT");
        commandText.AppendLine(VideoSelectColumns);
        commandText.AppendLine(VideoFromClause);
        commandText.AppendLine("WHERE " + searchCondition);
        commandText.AppendLine("  AND v.IsAvailable = 1");
        commandText.AppendLine(
            "  AND " + BuildRootSourceIdFilter(
                "v.RootSourceId",
                rootSourceIds,
                "videoRootSourceId"));
        commandText.AppendLine("ORDER BY");
        commandText.AppendLine(
            BuildVideoOrderBy(
                "v.NormalizedName",
                "v.Name",
                "v.Id"));
        commandText.AppendLine(limitClause);

        return commandText.ToString();
    }

    private static string Indent(string value)
    {
        return "    " + value.Replace(
            Environment.NewLine,
            Environment.NewLine + "    ");
    }
}
