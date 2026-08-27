using System.Windows;

namespace VideoArchiveFinder.HoverScrubPrototype;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var initialFilePath =
            e.Args.FirstOrDefault(argument =>
                !string.IsNullOrWhiteSpace(argument));

        var window = new MainWindow(initialFilePath);

        MainWindow = window;
        window.Show();
    }
}
