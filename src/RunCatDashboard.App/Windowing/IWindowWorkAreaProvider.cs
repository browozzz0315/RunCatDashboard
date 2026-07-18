namespace RunCatDashboard.App.Windowing;

public readonly record struct WindowWorkArea(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

public interface IWindowWorkAreaProvider
{
    WindowWorkArea GetForWindow(nint windowHandle);
}
