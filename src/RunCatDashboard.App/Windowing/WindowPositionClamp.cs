namespace RunCatDashboard.App.Windowing;

internal readonly record struct WindowBounds(
    double Left,
    double Top,
    double Width,
    double Height);

internal static class WindowPositionClamp
{
    internal static WindowBounds Clamp(WindowBounds window, WindowWorkArea workArea)
    {
        Validate(window, workArea);

        double left = window.Width <= workArea.Width
            ? Math.Clamp(window.Left, workArea.Left, workArea.Right - window.Width)
            : workArea.Left;
        double top = window.Height <= workArea.Height
            ? Math.Clamp(window.Top, workArea.Top, workArea.Bottom - window.Height)
            : workArea.Top;

        return window with { Left = left, Top = top };
    }

    private static void Validate(WindowBounds window, WindowWorkArea workArea)
    {
        if (!double.IsFinite(window.Left) ||
            !double.IsFinite(window.Top) ||
            !double.IsFinite(window.Width) ||
            !double.IsFinite(window.Height) ||
            window.Width <= 0 ||
            window.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Window bounds must be finite and positive.");
        }

        if (!double.IsFinite(workArea.Left) ||
            !double.IsFinite(workArea.Top) ||
            !double.IsFinite(workArea.Width) ||
            !double.IsFinite(workArea.Height) ||
            workArea.Width <= 0 ||
            workArea.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea), "Work area must be finite and positive.");
        }
    }
}
