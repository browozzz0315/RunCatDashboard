namespace RunCatDashboard.App.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark
}

public sealed record AppearanceSettings(ThemePreference ThemePreference)
{
    public static AppearanceSettings Defaults { get; } =
        new(ThemePreference.System);
}
