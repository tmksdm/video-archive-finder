using VideoArchiveFinder.SearchBenchmark;

using var cancellationTokenSource =
    new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationTokenSource.Cancel();

    Console.WriteLine();
    Console.WriteLine(
        "Получен запрос на остановку. Завершение...");
};

using var dataDirectoryProvider =
    new TemporaryApplicationDataDirectoryProvider();

try
{
    if (args.Length == 2 &&
        string.Equals(
            args[0],
            "--archive",
            StringComparison.OrdinalIgnoreCase))
    {
        var archiveRunner =
            new ArchiveIndexBenchmarkRunner(
                dataDirectoryProvider);

        await archiveRunner.RunAsync(
            args[1],
            cancellationTokenSource.Token);

        Console.WriteLine(
            "Измерение завершено успешно.");

        return 0;
    }

    if (args.Length != 0)
    {
        Console.Error.WriteLine(
            "Использование:");

        Console.Error.WriteLine(
            "  VideoArchiveFinder.SearchBenchmark");

        Console.Error.WriteLine(
            "  VideoArchiveFinder.SearchBenchmark " +
            "--archive <путь>");

        return 64;
    }

    var runner =
        new SearchBenchmarkRunner(
            dataDirectoryProvider);

    await runner.RunAsync(
        cancellationTokenSource.Token);

    Console.WriteLine(
        "Измерение завершено успешно.");

    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine(
        "Измерение отменено пользователем.");

    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        "Ошибка при выполнении измерения:");

    Console.Error.WriteLine(exception);

    return 1;
}
