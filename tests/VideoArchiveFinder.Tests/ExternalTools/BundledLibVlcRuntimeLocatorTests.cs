using VideoArchiveFinder.Infrastructure.ExternalTools;

namespace VideoArchiveFinder.Tests.ExternalTools;

public sealed class BundledLibVlcRuntimeLocatorTests
    : IDisposable
{
    private readonly string _baseDirectory = Path.Combine(
        Path.GetTempPath(),
        nameof(BundledLibVlcRuntimeLocatorTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Locate_RuntimeDirectoryDoesNotExist_ReportsAllComponentsMissing()
    {
        var locator = new BundledLibVlcRuntimeLocator(
            _baseDirectory);

        var result = locator.Locate();

        var expectedRuntimeDirectory = Path.Combine(
            _baseDirectory,
            "app",
            "libvlc");

        Assert.False(result.IsReady);
        Assert.Equal(
            ["libvlc.dll", "libvlccore.dll", "plugins"],
            result.MissingComponents);
        Assert.Contains(
            expectedRuntimeDirectory,
            result.DiagnosticMessage);
    }

    [Fact]
    public void Locate_AllComponentsExist_ReportsReady()
    {
        CreateRuntimeFile("libvlc.dll");
        CreateRuntimeFile("libvlccore.dll");
        CreateRuntimeFile(
            Path.Combine(
                "plugins",
                "codec",
                "libcodec_plugin.dll"));

        var locator = new BundledLibVlcRuntimeLocator(
            _baseDirectory);

        var result = locator.Locate();

        Assert.True(result.IsReady);
        Assert.Empty(result.MissingComponents);
        Assert.Equal(
            "LibVLC готов к использованию.",
            result.DiagnosticMessage);
    }

    [Fact]
    public void Locate_PluginsDirectoryIsEmpty_ReportsPluginsMissing()
    {
        CreateRuntimeFile("libvlc.dll");
        CreateRuntimeFile("libvlccore.dll");
        Directory.CreateDirectory(
            Path.Combine(
                GetRuntimeDirectory(),
                "plugins"));

        var locator = new BundledLibVlcRuntimeLocator(
            _baseDirectory);

        var result = locator.Locate();

        Assert.False(result.IsReady);
        Assert.Equal(
            ["plugins"],
            result.MissingComponents);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(
                _baseDirectory,
                recursive: true);
        }
    }

    private void CreateRuntimeFile(
        string relativePath)
    {
        var path = Path.Combine(
            GetRuntimeDirectory(),
            relativePath);

        Directory.CreateDirectory(
            Path.GetDirectoryName(path)!);

        File.WriteAllText(
            path,
            string.Empty);
    }

    private string GetRuntimeDirectory() =>
        Path.Combine(
            _baseDirectory,
            "app",
            "libvlc");
}
