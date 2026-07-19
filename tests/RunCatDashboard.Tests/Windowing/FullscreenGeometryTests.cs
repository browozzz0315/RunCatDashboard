using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class FullscreenGeometryTests
{
    [Fact]
    public void CoversMonitor_WithExactFullMonitorBounds_IsFullscreen()
    {
        var monitor = new PixelBounds(0, 0, 1920, 1080);

        Assert.True(FullscreenGeometry.CoversMonitor(monitor, monitor));
    }

    [Fact]
    public void CoversMonitor_WithOnlyWorkAreaBounds_IsNotFullscreen()
    {
        var maximizedWindow = new PixelBounds(0, 0, 1920, 1040);
        var monitor = new PixelBounds(0, 0, 1920, 1080);

        Assert.False(FullscreenGeometry.CoversMonitor(maximizedWindow, monitor));
    }

    [Theory]
    [InlineData(-20, -20, 1940, 1100, true)]
    [InlineData(2, 2, 1918, 1078, true)]
    [InlineData(3, 0, 1920, 1080, false)]
    [InlineData(0, 0, 1917, 1080, false)]
    public void CoversMonitor_UsesInclusiveTwoPixelTolerance(
        int left,
        int top,
        int right,
        int bottom,
        bool expected)
    {
        var window = new PixelBounds(left, top, right, bottom);
        var monitor = new PixelBounds(0, 0, 1920, 1080);

        Assert.Equal(expected, FullscreenGeometry.CoversMonitor(window, monitor));
    }

    [Fact]
    public void CoversMonitor_WithNegativeCoordinateMonitor_IsFullscreen()
    {
        var monitor = new PixelBounds(-2560, -200, 0, 1240);
        var window = new PixelBounds(-2559, -201, -1, 1241);

        Assert.True(FullscreenGeometry.CoversMonitor(window, monitor));
    }
}
