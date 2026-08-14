using System.IO;
using System.Security;
using Microsoft.Win32;

namespace RunCatDashboard.App.Theming;

public sealed class WindowsAppThemeDetector : IWindowsAppThemeDetector
{
    private const string PersonalizeKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    private readonly Func<WindowsAppTheme> _readTheme;
    private readonly bool _subscribed;
    private WindowsAppTheme _current;
    private bool _isDisposed;

    public WindowsAppThemeDetector()
        : this(ReadCurrentTheme, subscribe: true)
    {
    }

    internal WindowsAppThemeDetector(
        Func<WindowsAppTheme> readTheme,
        bool subscribe)
    {
        ArgumentNullException.ThrowIfNull(readTheme);
        _readTheme = readTheme;
        _current = readTheme();
        _subscribed = subscribe;
        if (subscribe)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
    }

    public WindowsAppTheme Current => _current;

    public event Action<WindowsAppTheme>? ThemeChanged;

    internal void Refresh()
    {
        if (_isDisposed)
        {
            return;
        }

        WindowsAppTheme next = _readTheme();
        if (next == _current)
        {
            return;
        }

        _current = next;
        ThemeChanged?.Invoke(next);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_subscribed)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        ThemeChanged = null;
    }

    private void OnUserPreferenceChanged(
        object sender,
        UserPreferenceChangedEventArgs e) => Refresh();

    private static WindowsAppTheme ReadCurrentTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            object? value = key?.GetValue(AppsUseLightThemeValue);
            return InterpretRegistryValue(value);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return WindowsAppTheme.Light;
        }
    }

    internal static WindowsAppTheme InterpretRegistryValue(object? value) => value switch
    {
        int integer when integer == 0 => WindowsAppTheme.Dark,
        int integer when integer == 1 => WindowsAppTheme.Light,
        _ => WindowsAppTheme.Light
    };
}
