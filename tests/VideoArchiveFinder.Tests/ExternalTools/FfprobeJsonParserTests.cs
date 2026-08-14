using VideoArchiveFinder.Application.ExternalTools;
using VideoArchiveFinder.Infrastructure.ExternalTools;

namespace VideoArchiveFinder.Tests.ExternalTools;

public sealed class FfprobeJsonParserTests
{
    private readonly FfprobeJsonParser _parser = new();

    [Fact]
    public void Parse_ValidJson_ReturnsVideoMetadata()
    {
        const string json =
            """
            {
              "streams": [
                {
                  "codec_type": "audio",
                  "codec_name": "aac"
                },
                {
                  "codec_type": "video",
                  "codec_name": "h264",
                  "width": 1920,
                  "height": 1080
                }
              ],
              "format": {
                "duration": "12.5"
              }
            }
            """;

        var result = _parser.Parse(json);

        Assert.True(result.IsSuccess);

        var metadata =
            Assert.IsType<FfprobeVideoMetadata>(
                result.Metadata);

        Assert.True(metadata.HasVideoStream);
        Assert.Equal(
            TimeSpan.FromSeconds(12.5),
            metadata.Duration);

        Assert.Equal(1920, metadata.Width);
        Assert.Equal(1080, metadata.Height);
        Assert.Equal("h264", metadata.CodecName);
    }

    [Fact]
    public void Parse_IncompleteVideoStream_ReturnsNullOptionalValues()
    {
        const string json =
            """
            {
              "streams": [
                {
                  "codec_type": "video"
                }
              ],
              "format": {}
            }
            """;

        var result = _parser.Parse(json);

        Assert.True(result.IsSuccess);

        var metadata =
            Assert.IsType<FfprobeVideoMetadata>(
                result.Metadata);

        Assert.True(metadata.HasVideoStream);
        Assert.Null(metadata.Duration);
        Assert.Null(metadata.Width);
        Assert.Null(metadata.Height);
        Assert.Null(metadata.CodecName);
    }

    [Fact]
    public void Parse_NoVideoStream_ReturnsSuccessfulResult()
    {
        const string json =
            """
            {
              "streams": [
                {
                  "codec_type": "audio",
                  "codec_name": "aac"
                }
              ],
              "format": {
                "duration": "3.25"
              }
            }
            """;

        var result = _parser.Parse(json);

        Assert.True(result.IsSuccess);

        var metadata =
            Assert.IsType<FfprobeVideoMetadata>(
                result.Metadata);

        Assert.False(metadata.HasVideoStream);
        Assert.Equal(
            TimeSpan.FromSeconds(3.25),
            metadata.Duration);

        Assert.Null(metadata.Width);
        Assert.Null(metadata.Height);
        Assert.Null(metadata.CodecName);
        Assert.Contains(
            "не обнаружен",
            result.DiagnosticMessage);
    }

    [Fact]
    public void Parse_InvalidOptionalValues_IgnoresThem()
    {
        const string json =
            """
            {
              "streams": [
                {
                  "codec_type": "VIDEO",
                  "codec_name": "  hevc  ",
                  "width": 0,
                  "height": -1080
                }
              ],
              "format": {
                "duration": "N/A"
              }
            }
            """;

        var result = _parser.Parse(json);

        Assert.True(result.IsSuccess);

        var metadata =
            Assert.IsType<FfprobeVideoMetadata>(
                result.Metadata);

        Assert.True(metadata.HasVideoStream);
        Assert.Null(metadata.Duration);
        Assert.Null(metadata.Width);
        Assert.Null(metadata.Height);
        Assert.Equal("hevc", metadata.CodecName);
    }

    [Fact]
    public void Parse_DamagedJson_ReturnsFailure()
    {
        const string json =
            """
            {
              "streams": [
                {
                  "codec_type": "video"
            """;

        var result = _parser.Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Metadata);
        Assert.Contains(
            "Не удалось разобрать JSON FFprobe",
            result.DiagnosticMessage);
    }

    [Fact]
    public void Parse_EmptyJson_ReturnsFailure()
    {
        var result = _parser.Parse("   ");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Metadata);
        Assert.Contains(
            "пустой JSON",
            result.DiagnosticMessage);
    }
}
