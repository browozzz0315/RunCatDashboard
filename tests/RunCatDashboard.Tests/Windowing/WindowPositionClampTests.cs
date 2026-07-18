using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class WindowPositionClampTests
{
    [Fact]
    public void Clamp_WhenWindowIsInsideWorkArea_PreservesPosition()
    {
        var workArea = new WindowWorkArea(0, 0, 1920, 1040);
        var window = new WindowBounds(100, 120, 620, 510);

        WindowBounds result = WindowPositionClamp.Clamp(window, workArea);

        Assert.Equal(window, result);
    }

    [Fact]
    public void Clamp_WithNegativeCoordinateWorkArea_UsesNegativeBounds()
    {
        var workArea = new WindowWorkArea(-1920, -200, 1920, 1080);
        var window = new WindowBounds(-2200, -500, 620, 510);

        WindowBounds result = WindowPositionClamp.Clamp(window, workArea);

        Assert.Equal(-1920, result.Left);
        Assert.Equal(-200, result.Top);
    }

    [Fact]
    public void Clamp_WhenWindowIsCompletelyOutside_MovesItInsideWorkArea()
    {
        var workArea = new WindowWorkArea(1920, 0, 2560, 1400);
        var window = new WindowBounds(6000, 3000, 620, 510);

        WindowBounds result = WindowPositionClamp.Clamp(window, workArea);

        Assert.Equal(workArea.Right - window.Width, result.Left);
        Assert.Equal(workArea.Bottom - window.Height, result.Top);
    }

    [Fact]
    public void Clamp_WhenWindowIsLargerThanWorkArea_AnchorsAtWorkAreaOrigin()
    {
        var workArea = new WindowWorkArea(-1280, 100, 800, 600);
        var window = new WindowBounds(3000, 3000, 1200, 900);

        WindowBounds result = WindowPositionClamp.Clamp(window, workArea);

        Assert.Equal(workArea.Left, result.Left);
        Assert.Equal(workArea.Top, result.Top);
        Assert.Equal(window.Width, result.Width);
        Assert.Equal(window.Height, result.Height);
    }
}
