using VideoArchiveFinder.Application.ExternalTools;

namespace VideoArchiveFinder.Infrastructure.ExternalTools;

public sealed class BundledLibVlcRuntimeLocator
    : ILibVlcRuntimeLocator
{
    private readonly string _applicationBaseDirectory;

    public BundledLibVlcRuntimeLocator()
        : this(AppContext.BaseDirectory)
    {
    }

    public BundledLibVlcRuntimeLocator(
        string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            applicationBaseDirectory);

        _applicationBaseDirectory =
            Path.GetFullPath(applicationBaseDirectory);
    }

    public LibVlcRuntimeStatus Locate()
    {
        var runtimeDirectory = Path.Combine(
            _applicationBaseDirectory,
            "app",
            "libvlc");

        var libVlcPath = Path.Combine(
            runtimeDirectory,
            "libvlc.dll");

        var libVlcCorePath = Path.Combine(
            runtimeDirectory,
            "libvlccore.dll");

        var pluginsDirectory = Path.Combine(
            runtimeDirectory,
            "plugins");

        return new LibVlcRuntimeStatus(
            runtimeDirectory,
            libVlcPath,
            libVlcCorePath,
            pluginsDirectory,
            File.Exists(libVlcPath),
            File.Exists(libVlcCorePath),
            ContainsPlugin(pluginsDirectory));
    }

    private static bool ContainsPlugin(
        string pluginsDirectory)
    {
        try
        {
            return Directory.Exists(pluginsDirectory) &&
                   Directory.EnumerateFiles(
                       pluginsDirectory,
                       "*.dll",
                       SearchOption.AllDirectories)
                       .Any();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
