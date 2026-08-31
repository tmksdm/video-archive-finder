namespace VideoArchiveFinder.Application.ExternalTools;

public sealed record LibVlcRuntimeStatus(
    string RuntimeDirectory,
    string LibVlcPath,
    string LibVlcCorePath,
    string PluginsDirectory,
    bool LibVlcExists,
    bool LibVlcCoreExists,
    bool PluginsExist)
{
    public bool IsReady =>
        LibVlcExists &&
        LibVlcCoreExists &&
        PluginsExist;

    public IReadOnlyList<string> MissingComponents
    {
        get
        {
            var missingComponents =
                new List<string>();

            if (!LibVlcExists)
            {
                missingComponents.Add("libvlc.dll");
            }

            if (!LibVlcCoreExists)
            {
                missingComponents.Add("libvlccore.dll");
            }

            if (!PluginsExist)
            {
                missingComponents.Add("plugins");
            }

            return missingComponents;
        }
    }

    public string DiagnosticMessage =>
        IsReady
            ? "LibVLC готов к использованию."
            : $"Hover-просмотр отключён: не найдены компоненты " +
              $"{string.Join(", ", MissingComponents)}. " +
              $"Ожидаемая папка: {RuntimeDirectory}";
}
