namespace VideoArchiveFinder.Application.ExternalTools;

public sealed record FfmpegToolsStatus(
    string ToolsDirectory,
    string FfmpegPath,
    string FfprobePath,
    bool FfmpegExists,
    bool FfprobeExists)
{
    public bool IsReady =>
        FfmpegExists &&
        FfprobeExists;

    public IReadOnlyList<string> MissingFileNames =>
        (FfmpegExists, FfprobeExists) switch
        {
            (true, true) => Array.Empty<string>(),
            (false, true) => ["ffmpeg.exe"],
            (true, false) => ["ffprobe.exe"],
            (false, false) => ["ffmpeg.exe", "ffprobe.exe"]
        };

    public string DiagnosticMessage =>
        IsReady
            ? "FFmpeg и FFprobe готовы к использованию."
            : $"Не найдены обязательные инструменты: " +
              $"{string.Join(", ", MissingFileNames)}. " +
              $"Ожидаемая папка: {ToolsDirectory}";
}
