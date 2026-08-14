using RunCatDashboard.App.Theming;

namespace RunCatDashboard.Tests.Theming;

public sealed class WindowsAppThemeDetectorTests
{
    [Theory]
    [InlineData(1, WindowsAppTheme.Light)]
    [InlineData(0, WindowsAppTheme.Dark)]
    [InlineData(null, WindowsAppTheme.Light)]
    public void RegistryValue_MapsInvalidOrMissingToLight(
        int? value,
        WindowsAppTheme expected)
    {
        Assert.Equal(expected, WindowsAppThemeDetector.InterpretRegistryValue(value));
    }

    [Fact]
    public void Refresh_ReReadsAndPublishesOnlyWhenEffectiveThemeChanges()
    {
        WindowsAppTheme current = WindowsAppTheme.Light;
        using var detector = new WindowsAppThemeDetector(() => current, subscribe: false);
        var changes = new List<WindowsAppTheme>();
        detector.ThemeChanged += changes.Add;

        detector.Refresh();
        current = WindowsAppTheme.Dark;
        detector.Refresh();
        detector.Refresh();

        Assert.Equal([WindowsAppTheme.Dark], changes);
    }

    [Fact]
    public void Dispose_StopsRefreshNotifications()
    {
        WindowsAppTheme current = WindowsAppTheme.Light;
        var detector = new WindowsAppThemeDetector(() => current, subscribe: false);
        int count = 0;
        detector.ThemeChanged += _ => count++;

        detector.Dispose();
        current = WindowsAppTheme.Dark;
        detector.Refresh();

        Assert.Equal(0, count);
    }
}
