namespace RunCatDashboard.App.Windowing;

internal readonly record struct PixelBounds(int Left, int Top, int Right, int Bottom)
{
    internal int Width => Right - Left;

    internal int Height => Bottom - Top;

    public override string ToString() => $"[{Left},{Top}]-[{Right},{Bottom}]";
}

internal static class FullscreenGeometry
{
    internal const int DefaultTolerancePixels = 2;

    internal static bool CoversMonitor(
        PixelBounds windowBounds,
        PixelBounds monitorBounds,
        int tolerancePixels = DefaultTolerancePixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tolerancePixels);

        if (windowBounds.Width <= 0 ||
            windowBounds.Height <= 0 ||
            monitorBounds.Width <= 0 ||
            monitorBounds.Height <= 0)
        {
            return false;
        }

        return (long)windowBounds.Left <= (long)monitorBounds.Left + tolerancePixels &&
            (long)windowBounds.Top <= (long)monitorBounds.Top + tolerancePixels &&
            (long)windowBounds.Right >= (long)monitorBounds.Right - tolerancePixels &&
            (long)windowBounds.Bottom >= (long)monitorBounds.Bottom - tolerancePixels;
    }
}
