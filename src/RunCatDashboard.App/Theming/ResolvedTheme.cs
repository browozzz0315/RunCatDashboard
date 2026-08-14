using RunCatDashboard.App.Settings;

namespace RunCatDashboard.App.Theming;

public enum WindowsAppTheme
{
    Light,
    Dark
}

public enum ResolvedTheme
{
    Light,
    Dark
}

public static class ThemeResolver
{
    public static ResolvedTheme Resolve(
        ThemePreference preference,
        WindowsAppTheme windowsTheme) => preference switch
        {
            ThemePreference.Dark => ResolvedTheme.Dark,
            ThemePreference.Light => ResolvedTheme.Light,
            ThemePreference.System => windowsTheme == WindowsAppTheme.Dark
                ? ResolvedTheme.Dark
                : ResolvedTheme.Light,
            _ => ResolvedTheme.Light
        };
}
