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
