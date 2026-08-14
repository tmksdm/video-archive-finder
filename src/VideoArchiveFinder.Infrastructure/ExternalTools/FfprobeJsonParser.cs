using System.Globalization;
using System.Text.Json;
using VideoArchiveFinder.Application.ExternalTools;

namespace VideoArchiveFinder.Infrastructure.ExternalTools;

public sealed class FfprobeJsonParser
    : IFfprobeJsonParser
{
    public FfprobeJsonParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateFailure(
                "FFprobe вернул пустой JSON.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind !=
                JsonValueKind.Object)
            {
                return CreateFailure(
                    "Корневой элемент JSON FFprobe " +
                    "не является объектом.");
            }

            var root = document.RootElement;
            var duration = ReadDuration(root);
            var videoStream = FindVideoStream(root);

            if (videoStream is null)
            {
                return new FfprobeJsonParseResult(
                    new FfprobeVideoMetadata(
                        false,
                        duration,
                        null,
                        null,
                        null),
                    "Видеопоток не обнаружен.");
            }

            var stream = videoStream.Value;

            var metadata = new FfprobeVideoMetadata(
                true,
                duration,
                ReadPositiveInt(stream, "width"),
                ReadPositiveInt(stream, "height"),
                ReadString(stream, "codec_name"));

            return new FfprobeJsonParseResult(
                metadata,
                "Метаданные FFprobe успешно разобраны.");
        }
        catch (JsonException exception)
        {
            return CreateFailure(
                "Не удалось разобрать JSON FFprobe: " +
                exception.Message);
        }
    }

    private static JsonElement? FindVideoStream(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "streams",
                out var streams) ||
            streams.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var stream in streams.EnumerateArray())
        {
            if (stream.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var codecType = ReadString(
                stream,
                "codec_type");

            if (string.Equals(
                    codecType,
                    "video",
                    StringComparison.OrdinalIgnoreCase))
            {
                return stream;
            }
        }

        return null;
    }

    private static TimeSpan? ReadDuration(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "format",
                out var format) ||
            format.ValueKind != JsonValueKind.Object ||
            !format.TryGetProperty(
                "duration",
                out var durationElement))
        {
            return null;
        }

        if (!TryReadDouble(
                durationElement,
                out var seconds) ||
            !double.IsFinite(seconds) ||
            seconds < 0)
        {
            return null;
        }

        try
        {
            return TimeSpan.FromSeconds(seconds);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static bool TryReadDouble(
        JsonElement element,
        out double value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetDouble(out value);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return double.TryParse(
                element.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        value = default;
        return false;
    }

    private static int? ReadPositiveInt(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var property))
        {
            return null;
        }

        int value;

        if (property.ValueKind == JsonValueKind.Number)
        {
            if (!property.TryGetInt32(out value))
            {
                return null;
            }
        }
        else if (property.ValueKind == JsonValueKind.String)
        {
            if (!int.TryParse(
                    property.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        return value > 0
            ? value
            : null;
    }

    private static string? ReadString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static FfprobeJsonParseResult CreateFailure(
        string diagnosticMessage)
    {
        return new FfprobeJsonParseResult(
            null,
            diagnosticMessage);
    }
}
