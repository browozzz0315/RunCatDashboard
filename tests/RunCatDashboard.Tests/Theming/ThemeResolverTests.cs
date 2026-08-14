using RunCatDashboard.App.Settings;
using RunCatDashboard.App.Theming;

namespace RunCatDashboard.Tests.Theming;

public sealed class ThemeResolverTests
{
    [Theory]
    [InlineData(ThemePreference.System, WindowsAppTheme.Light, ResolvedTheme.Light)]
    [InlineData(ThemePreference.System, WindowsAppTheme.Dark, ResolvedTheme.Dark)]
    [InlineData(ThemePreference.Light, WindowsAppTheme.Light, ResolvedTheme.Light)]
    [InlineData(ThemePreference.Light, WindowsAppTheme.Dark, ResolvedTheme.Light)]
    [InlineData(ThemePreference.Dark, WindowsAppTheme.Light, ResolvedTheme.Dark)]
    [InlineData(ThemePreference.Dark, WindowsAppTheme.Dark, ResolvedTheme.Dark)]
    public void Resolve_UsesPreferenceUnlessSystem(
        ThemePreference preference,
        WindowsAppTheme windowsTheme,
        ResolvedTheme expected)
    {
        Assert.Equal(expected, ThemeResolver.Resolve(preference, windowsTheme));
    }
}
