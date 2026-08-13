using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Threading;
using VideoArchiveFinder.Application.Settings;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;



namespace VideoArchiveFinder.Desktop.Services;

public sealed class AppThemeService
    : IAppThemeService, IDisposable
{
    private const string PersonalizeRegistryPath =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string AppsUseLightThemeValueName =
        "AppsUseLightTheme";

    private const int DwmUseImmersiveDarkMode =
        20;

    private const int DwmUseImmersiveDarkModeBefore20H1 =
        19;

    private const int DwmCaptionColor =
        35;

    private const int DwmTextColor =
        36;

    private const uint SwpNoSize =
        0x0001;

    private const uint SwpNoMove =
        0x0002;

    private const uint SwpNoZOrder =
        0x0004;

    private const uint SwpNoActivate =
        0x0010;

    private const uint SwpFrameChanged =
        0x0020;


    private readonly IUserSettingsStore
        _userSettingsStore;

    private readonly ILogger<AppThemeService>
        _logger;

    private bool _isInitialized;
    private bool _isDisposed;

    public AppThemeService(
        IUserSettingsStore userSettingsStore,
        ILogger<AppThemeService> logger)
    {
        _userSettingsStore = userSettingsStore;
        _logger = logger;
    }

    public AppThemeMode SelectedMode
    {
        get;
        private set;
    } = AppThemeMode.System;

    public AppThemeMode EffectiveMode
    {
        get;
        private set;
    } = AppThemeMode.Light;

    public event EventHandler? ThemeChanged;

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_isInitialized)
        {
            return;
        }

        var settings =
            await _userSettingsStore.LoadAsync(
                cancellationToken);

        SelectedMode =
            settings.ThemeMode;

        await ApplySelectedThemeAsync();

        SystemEvents.UserPreferenceChanged +=
            SystemEvents_UserPreferenceChanged;

        var application =
            System.Windows.Application.Current;

        if (application is not null)
        {
            application.Activated +=
                Application_Activated;
        }

        _isInitialized = true;

    }

    public async Task SetThemeAsync(
        AppThemeMode mode,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var normalizedMode =
            Enum.IsDefined(mode)
                ? mode
                : AppThemeMode.System;

        var currentSettings =
            await _userSettingsStore.LoadAsync(
                cancellationToken);

        var updatedSettings = currentSettings with
        {
            ThemeMode = normalizedMode
        };

        await _userSettingsStore.SaveAsync(
            updatedSettings,
            cancellationToken);

        SelectedMode = normalizedMode;

        await ApplySelectedThemeAsync();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_isInitialized)
        {
            SystemEvents.UserPreferenceChanged -=
                SystemEvents_UserPreferenceChanged;

            var application =
                System.Windows.Application.Current;

            if (application is not null)
            {
                application.Activated -=
                    Application_Activated;
            }
        }


        _isDisposed = true;
    }

    private async Task ApplySelectedThemeAsync()
    {
        var application =
            System.Windows.Application.Current;

        if (application is null)
        {
            return;
        }

        var dispatcher =
            application.Dispatcher;

        if (dispatcher.CheckAccess())
        {
            ApplySelectedTheme();
            return;
        }

        await dispatcher.InvokeAsync(
            ApplySelectedTheme,
            DispatcherPriority.Normal);
    }

    private void ApplySelectedTheme()
    {
        var effectiveMode =
            SelectedMode switch
            {
                AppThemeMode.Dark =>
                    AppThemeMode.Dark,

                AppThemeMode.Light =>
                    AppThemeMode.Light,

                _ =>
                    ReadSystemTheme()
            };

        var application =
            System.Windows.Application.Current;

        if (application is null)
        {
            return;
        }

        var dictionaries =
            application.Resources.MergedDictionaries;

        var existingDictionary =
            dictionaries.FirstOrDefault(
                IsApplicationThemeDictionary);

        var fileName =
            effectiveMode == AppThemeMode.Dark
                ? "DarkTheme.xaml"
                : "LightTheme.xaml";

        var replacementDictionary =
            new ResourceDictionary
            {
                Source = new Uri(
                    $"Themes/{fileName}",
                    UriKind.Relative)
            };

        if (existingDictionary is null)
        {
            dictionaries.Insert(
                0,
                replacementDictionary);
        }
        else
        {
            var index =
                dictionaries.IndexOf(
                    existingDictionary);

            dictionaries[index] =
                replacementDictionary;
        }

        EffectiveMode = effectiveMode;

        ApplyWindowTitleBars();

        ThemeChanged?.Invoke(
            this,
            EventArgs.Empty);


        _logger.LogInformation(
            "Applied {EffectiveTheme} application theme for selected mode {SelectedTheme}.",
            EffectiveMode,
            SelectedMode);
    }

    private AppThemeMode ReadSystemTheme()
    {
        try
        {
            var value =
                Registry.GetValue(
                    PersonalizeRegistryPath,
                    AppsUseLightThemeValueName,
                    1);

            return value is int intValue &&
                   intValue == 0
                ? AppThemeMode.Dark
                : AppThemeMode.Light;
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  SecurityException)
        {
            _logger.LogWarning(
                exception,
                "Could not read the Windows application theme.");

            return AppThemeMode.Light;
        }
    }

    private static bool IsApplicationThemeDictionary(
        ResourceDictionary dictionary)
    {
        var source =
            dictionary.Source?.OriginalString;

        return source is not null &&
               (source.EndsWith(
                    "Themes/LightTheme.xaml",
                    StringComparison.OrdinalIgnoreCase) ||
                source.EndsWith(
                    "Themes/DarkTheme.xaml",
                    StringComparison.OrdinalIgnoreCase));
    }


    private void Application_Activated(
        object? sender,
        EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        ApplyWindowTitleBars();
    }

    private void ApplyWindowTitleBars()
    {
        var application =
            System.Windows.Application.Current;

        if (application is null)
        {
            return;
        }

        var useDarkMode =
            EffectiveMode == AppThemeMode.Dark
                ? 1
                : 0;

        var captionColor =
            GetColorReference(
                application,
                "AppWindowBackgroundBrush",
                EffectiveMode == AppThemeMode.Dark
                    ? Color.FromRgb(17, 24, 39)
                    : Color.FromRgb(243, 244, 246));

        var captionTextColor =
            GetColorReference(
                application,
                "AppPrimaryTextBrush",
                EffectiveMode == AppThemeMode.Dark
                    ? Color.FromRgb(249, 250, 251)
                    : Color.FromRgb(17, 24, 39));

        foreach (Window window in application.Windows)
        {
            var windowHandle =
                new WindowInteropHelper(window).Handle;

            if (windowHandle == IntPtr.Zero)
            {
                continue;
            }

            var result =
                DwmSetWindowAttribute(
                    windowHandle,
                    DwmUseImmersiveDarkMode,
                    ref useDarkMode,
                    Marshal.SizeOf<int>());

            if (result != 0)
            {
                result =
                    DwmSetWindowAttribute(
                        windowHandle,
                        DwmUseImmersiveDarkModeBefore20H1,
                        ref useDarkMode,
                        Marshal.SizeOf<int>());
            }

            DwmSetWindowAttribute(
                windowHandle,
                DwmCaptionColor,
                ref captionColor,
                Marshal.SizeOf<int>());

            DwmSetWindowAttribute(
                windowHandle,
                DwmTextColor,
                ref captionTextColor,
                Marshal.SizeOf<int>());

            SetWindowPos(
                windowHandle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SwpNoSize |
                SwpNoMove |
                SwpNoZOrder |
                SwpNoActivate |
                SwpFrameChanged);

            if (result != 0)
            {
                _logger.LogDebug(
                    "Windows did not apply the requested immersive title bar theme. DWM result: {DwmResult}.",
                    result);
            }
        }
    }

    private static int GetColorReference(
        System.Windows.Application application,
        string resourceKey,
        Color fallbackColor)
    {
        var color =
            application.TryFindResource(resourceKey)
                is SolidColorBrush brush
                    ? brush.Color
                    : fallbackColor;

        return color.R |
               color.G << 8 |
               color.B << 16;
    }


    private void SystemEvents_UserPreferenceChanged(
        object sender,
        UserPreferenceChangedEventArgs e)
    {
        if (_isDisposed ||
            SelectedMode != AppThemeMode.System)
        {
            return;
        }

        var dispatcher =
            System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null ||
            dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(
            ApplySelectedTheme,
            DispatcherPriority.Normal);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _isDisposed,
            this);
    }

    [DllImport(
        "dwmapi.dll",
        PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfterWindowHandle,
        int x,
        int y,
        int width,
        int height,
        uint flags);


}
