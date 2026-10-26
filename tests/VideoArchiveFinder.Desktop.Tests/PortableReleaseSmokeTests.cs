using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Xunit.Abstractions;

namespace VideoArchiveFinder.Desktop.Tests;

public sealed class PortableReleaseSmokeTests(ITestOutputHelper output)
{
    private const string ReleaseExeVariable = "VAF_RELEASE_EXE";
    private const string ExpectedWindowTitle = "Video Archive Finder";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(15);
    private readonly StringBuilder _diagnostics = new();

    [ReleaseExeFact]
    public async Task PortableRelease_StartsMainWindowAndExitsCleanly()
    {
        var configuredPath = Environment.GetEnvironmentVariable(ReleaseExeVariable);
        Assert.False(string.IsNullOrWhiteSpace(configuredPath),
            $"{ReleaseExeVariable} must point to the portable release exe.");
        var releaseExePath = configuredPath!;
        Assert.True(Path.IsPathFullyQualified(releaseExePath),
            $"{ReleaseExeVariable} must be an absolute path: {releaseExePath}");
        Assert.True(File.Exists(releaseExePath),
            $"Release executable does not exist: {releaseExePath}");

        var releaseDirectory = Path.GetDirectoryName(releaseExePath)!;
        var bundledLibVlcPath = Path.Combine(
            releaseDirectory, "app", "libvlc", "libvlc.dll");
        Assert.True(File.Exists(bundledLibVlcPath),
            $"Bundled LibVLC is missing from the portable release: {bundledLibVlcPath}");

        var dataDirectory = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), "VAF-ReleaseSmoke", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(dataDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = releaseExePath,
            WorkingDirectory = releaseDirectory,
            UseShellExecute = false
        };
        startInfo.Environment["VAF_DATA_DIRECTORY"] = dataDirectory;

        using var process = new Process { StartInfo = startInfo };
        var started = false;
        var succeeded = false;
        try
        {
            var version = FileVersionInfo.GetVersionInfo(releaseExePath);
            Report($"Exe={releaseExePath}");
            Report($"FileVersion={version.FileVersion ?? "<none>"}");
            Report($"BundledLibVlc={bundledLibVlcPath}");
            Report($"DataDirectory={dataDirectory}");
            process.Start();
            started = true;
            Report($"Started PID={process.Id}");

            var ready = await WaitForMainWindowAsync(process, StartupTimeout);
            Assert.False(process.HasExited, "VAF exited during startup.");
            Assert.True(ready,
                $"VAF main window was not ready within {StartupTimeout.TotalSeconds:0}s.");
            process.Refresh();
            Report($"Title={process.MainWindowTitle}");
            Report($"Responding={process.Responding}");
            Report($"MainWindowHandle=0x{process.MainWindowHandle.ToInt64():X}");
            Assert.Equal(ExpectedWindowTitle, process.MainWindowTitle);
            Assert.True(process.Responding);

            var indexPath = Path.Combine(dataDirectory, "Index", "video-archive-finder.db");
            var logsPath = Path.Combine(dataDirectory, "Logs");
            Assert.True(File.Exists(indexPath),
                $"Isolated index database was not created: {indexPath}");
            Assert.True(Directory.Exists(logsPath),
                $"Isolated logs directory was not created: {logsPath}");

            var closeRequested = process.CloseMainWindow();
            Report($"CloseMainWindow={closeRequested}");
            Assert.True(closeRequested, "Could not close the VAF main window.");

            var exited = await WaitForExitWithDiagnosticsAsync(
                process,
                ShutdownTimeout);
            if (!exited)
            {
                DumpProcessWindows(process.Id);
                DumpOwnLogs(dataDirectory);
            }

            Assert.True(exited,
                $"VAF did not exit within {ShutdownTimeout.TotalSeconds:0}s.");
            Report($"ExitCode={process.ExitCode}");
            Assert.Equal(0, process.ExitCode);
            Assert.True(Directory.EnumerateFiles(logsPath, "video-archive-finder-*.log").Any(),
                "VAF did not write a log file to the isolated logs directory.");

            var logText = ReadOwnLogText(logsPath);
            Assert.Contains("Video Archive Finder started", logText);
            Assert.DoesNotContain("LibVLC is unavailable", logText);
            Assert.Contains("Video Archive Finder stopped", logText);
            Assert.Contains("Shutdown: host disposed", logText);
            Report("Shutdown log confirms graceful host disposal.");
            succeeded = true;
        }
        finally
        {
            if (!succeeded && started)
            {
                ReportProcessState(process, "finally");
                if (!process.HasExited)
                {
                    DumpProcessWindows(process.Id);
                }

                DumpOwnLogs(dataDirectory);
                PreserveFailureArtifacts(dataDirectory);
            }

            TerminateOwnProcess(process, started);
            DeleteOwnDataDirectory(dataDirectory);
        }
    }

    private async Task<bool> WaitForExitWithDiagnosticsAsync(
        Process process,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        var nextReport = TimeSpan.Zero;
        ReportProcessState(process, "immediately-after-close");

        while (!process.HasExited && stopwatch.Elapsed < timeout)
        {
            if (stopwatch.Elapsed >= nextReport)
            {
                ReportProcessState(
                    process,
                    $"after-close-{stopwatch.Elapsed.TotalSeconds:0.0}s");
                nextReport = stopwatch.Elapsed + TimeSpan.FromSeconds(3);
            }

            await Task.Delay(200);
        }

        ReportProcessState(
            process,
            process.HasExited
                ? $"exited-{stopwatch.Elapsed.TotalSeconds:0.0}s"
                : $"still-running-{stopwatch.Elapsed.TotalSeconds:0.0}s");
        return process.HasExited;
    }

    private void ReportProcessState(Process process, string stage)
    {
        try
        {
            process.Refresh();
            if (process.HasExited)
            {
                Report($"[{stage}] HasExited=true ExitCode={process.ExitCode}");
                return;
            }

            Report($"[{stage}] HasExited=false " +
                $"Responding={process.Responding} " +
                $"Title='{process.MainWindowTitle}' " +
                $"MainWindowHandle=0x{process.MainWindowHandle.ToInt64():X} " +
                $"Threads={process.Threads.Count}");
        }
        catch (InvalidOperationException exception)
        {
            Report($"[{stage}] Process state unavailable: {exception.Message}");
        }
    }

    private void DumpProcessWindows(int processId)
    {
        Report($"Top-level windows for PID {processId}:");
        var windowCount = 0;

        try
        {
            EnumWindows((windowHandle, _) =>
            {
                GetWindowThreadProcessId(windowHandle, out var windowProcessId);
                if (windowProcessId != (uint)processId)
                {
                    return true;
                }

                var title = new StringBuilder(512);
                _ = GetWindowText(windowHandle, title, title.Capacity);
                var className = new StringBuilder(256);
                _ = GetClassName(windowHandle, className, className.Capacity);
                var owner = GetWindow(windowHandle, GetWindowOwner);

                Report($"  Handle=0x{windowHandle.ToInt64():X} " +
                    $"Visible={IsWindowVisible(windowHandle)} " +
                    $"Enabled={IsWindowEnabled(windowHandle)} " +
                    $"Class='{className}' Title='{title}' " +
                    $"Owner=0x{owner.ToInt64():X}");
                windowCount++;
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception exception)
        {
            Report($"Window enumeration failed: {exception.Message}");
        }

        if (windowCount == 0)
        {
            Report("  <none>");
        }
    }

    private static string ReadOwnLogText(string logsPath)
    {
        var logFile = Directory
            .EnumerateFiles(logsPath, "video-archive-finder-*.log")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .LastOrDefault();

        if (logFile is null)
        {
            return string.Empty;
        }

        using var stream = new FileStream(
            logFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void DumpOwnLogs(string dataDirectory)
    {
        var logsPath = Path.Combine(dataDirectory, "Logs");
        if (!Directory.Exists(logsPath))
        {
            Report($"Logs directory is missing: {logsPath}");
            return;
        }

        var logFiles = Directory
            .EnumerateFiles(logsPath, "video-archive-finder-*.log")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (logFiles.Length == 0)
        {
            Report($"No log files found in {logsPath}");
            return;
        }

        foreach (var logFile in logFiles)
        {
            Report($"--- log {logFile} ({new FileInfo(logFile).Length} bytes) ---");
            try
            {
                using var stream = new FileStream(
                    logFile,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();
                const int maximumDumpedCharacters = 60000;
                if (content.Length > maximumDumpedCharacters)
                {
                    Report($"[truncated to last {maximumDumpedCharacters} characters]");
                    content = content[^maximumDumpedCharacters..];
                }

                Report(content);
            }
            catch (IOException exception)
            {
                Report($"Could not read log file: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                Report($"Could not read log file: {exception.Message}");
            }
        }
    }

    private void PreserveFailureArtifacts(string dataDirectory)
    {
        try
        {
            var destination = Path.Combine(
                Path.GetTempPath(),
                "VAF-ReleaseSmokeFailures",
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}");
            Directory.CreateDirectory(destination);

            var sourceLogsPath = Path.Combine(dataDirectory, "Logs");
            if (Directory.Exists(sourceLogsPath))
            {
                foreach (var logFile in Directory.EnumerateFiles(sourceLogsPath))
                {
                    File.Copy(
                        logFile,
                        Path.Combine(destination, Path.GetFileName(logFile)),
                        overwrite: true);
                }
            }

            File.WriteAllText(
                Path.Combine(destination, "diagnostics.txt"),
                _diagnostics.ToString());
            Report($"Preserved failure artifacts: {destination}");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            Report($"Could not preserve failure artifacts: {exception.Message}");
        }
    }

    private void Report(string message)
    {
        output.WriteLine(message);
        _diagnostics.AppendLine(message);
    }

    private static async Task<bool> WaitForMainWindowAsync(Process process, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!process.HasExited && stopwatch.Elapsed < timeout)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                try
                {
                    var inputIdle = process.WaitForInputIdle(1000);
                    process.Refresh();
                    if (inputIdle && process.Responding &&
                        !string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException) { }
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static void TerminateOwnProcess(Process process, bool started)
    {
        if (!started || process.HasExited) return;
        try
        {
            if (process.CloseMainWindow() && process.WaitForExit(5000)) return;
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    private static void DeleteOwnDataDirectory(string dataDirectory)
    {
        try
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private const uint GetWindowOwner = 4;

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsCallback callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr windowHandle,
        StringBuilder text,
        int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr windowHandle,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(
        IntPtr windowHandle,
        uint command);

    public sealed class ReleaseExeFactAttribute : FactAttribute
    {
        public ReleaseExeFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ReleaseExeVariable)))
            {
                Skip = $"Opt-in release smoke: set {ReleaseExeVariable} " +
                    "to the portable release exe.";
            }
        }
    }
}
